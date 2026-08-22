using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace HexWin.Input;

/// <summary>
/// Hook clavier bas niveau : voit toutes les frappes du système, quelle que
/// soit la fenêtre active.
///
/// Coquille volontairement mince. Toute la décision appartient à
/// <see cref="ChordDetector"/>, qui se teste ; ici on ne fait que brancher
/// Win32 et relayer.
///
/// <para><b>Contrainte impérative : ne jamais bloquer dans le rappel.</b>
/// Windows accorde au rappel un délai — <c>LowLevelHooksTimeout</c>, 5 s par
/// défaut mais souvent bien moins. Passé ce délai, le système désinstalle le
/// hook <i>silencieusement</i> : le raccourci cesse de répondre sans le
/// moindre message, et seul un redémarrage de l'application le rétablit.
/// Les abonnés doivent donc rendre la main immédiatement et confier le
/// travail à un fil de fond.</para>
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Coquille Win32 : un hook clavier global ne peut pas être déclenché par un test : Windows marque comme injectées les frappes produites par un programme.")]
internal sealed partial class KeyboardHook : IDisposable
{
    private const int WhKeyboardLowLevel = 13;
    private const int HcAction = 0;

    private const nint WmKeyDown = 0x0100;
    private const nint WmKeyUp = 0x0101;
    private const nint WmSysKeyDown = 0x0104;
    private const nint WmSysKeyUp = 0x0105;

    /// <summary>Marque les événements que nous avons nous-mêmes injectés.</summary>
    private const uint LlkhfInjected = 0x10;

    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;

    private readonly ChordDetector _detector;

    /// <summary>
    /// Le délégué doit être conservé dans un champ. Sans cela, le ramasse-
    /// miettes le collecte alors que Windows en détient encore l'adresse, et
    /// le processus s'effondre au premier appui de touche.
    /// </summary>
    private readonly HookProc _callback;

    private nint _hook;
    private bool _disposed;

    /// <summary>
    /// Date du dernier événement clavier observé. Sert au chien de garde à
    /// distinguer « personne ne tape » de « le hook est mort ».
    /// </summary>
    private long _lastEventTicks = DateTime.UtcNow.Ticks;

    public KeyboardHook(ChordDetector detector)
    {
        _detector = detector;
        _callback = OnKeyboardEvent;
    }

    /// <summary>Le raccourci vient d'être complété : démarrer l'enregistrement.</summary>
    public event EventHandler? Started;

    /// <summary>Le raccourci vient d'être relâché : transcrire.</summary>
    public event EventHandler? Stopped;

    /// <summary>La dictée est abandonnée sans transcrire.</summary>
    public event EventHandler? Cancelled;

    public void Install()
    {
        if (_hook != 0)
        {
            return;
        }

        _hook = SetWindowsHookExW(WhKeyboardLowLevel, _callback, 0, 0);

        if (_hook == 0)
        {
            throw new InvalidOperationException(
                $"Installation du hook clavier impossible (erreur {Marshal.GetLastWin32Error()}).");
        }

        // Le verrouillage de session interrompt la livraison des
        // relâchements : sans remise à zéro, une touche resterait
        // éternellement considérée comme enfoncée.
        SystemEvents.SessionSwitch += OnSessionSwitch;
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (_detector.Reset().Action == ChordAction.Cancel)
        {
            Cancelled?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Réinstalle le hook s'il n'a rien vu passer depuis <paramref name="silence"/>.
    ///
    /// <para>Windows désinstalle un hook bas niveau <b>sans le dire</b> quand
    /// son rappel dépasse le délai imparti. L'application reste alors
    /// parfaitement saine en apparence — icône bleue, aucune erreur — mais le
    /// raccourci ne répond plus, et seul un redémarrage le rétablit.</para>
    ///
    /// <para>Aucune API ne permet d'interroger l'état d'un hook. On compare
    /// donc ce que <i>nous</i> avons vu à ce que <i>Windows</i> a vu :
    /// <c>GetLastInputInfo</c> rend la date de la dernière saisie du système,
    /// indépendamment de notre hook. Si Windows a reçu des frappes que nous
    /// n'avons pas vues, notre hook est mort. Si personne n'a rien tapé, il
    /// n'y a rien à réparer.</para>
    ///
    /// <para>Une première version se contentait du silence de notre côté, ce
    /// qui réinstallait le hook toutes les deux minutes pendant une nuit
    /// entière : inutile, et le journal en devenait illisible — au point qu'un
    /// vrai incident s'y serait noyé.</para>
    /// </summary>
    /// <returns>Vrai si une réinstallation a eu lieu.</returns>
    public bool RefreshIfSilent(TimeSpan silence)
    {
        if (_hook == 0 || _disposed)
        {
            return false;
        }

        long ourLastEvent = Interlocked.Read(ref _lastEventTicks);
        var since = TimeSpan.FromTicks(DateTime.UtcNow.Ticks - ourLastEvent);

        if (since < silence)
        {
            return false;
        }

        // Le système a-t-il vu des frappes que nous avons manquées ?
        if (!SystemSawInputAfter(ourLastEvent))
        {
            return false;
        }

        // Le nouveau hook est posé AVANT de retirer l'ancien : dans l'autre
        // ordre, une frappe survenant entre les deux appels serait perdue.
        nint renewed = SetWindowsHookExW(WhKeyboardLowLevel, _callback, 0, 0);

        if (renewed == 0)
        {
            return false;
        }

        UnhookWindowsHookEx(_hook);
        _hook = renewed;
        Interlocked.Exchange(ref _lastEventTicks, DateTime.UtcNow.Ticks);

        return true;
    }

    private nint OnKeyboardEvent(int code, nint message, nint data)
    {
        Interlocked.Exchange(ref _lastEventTicks, DateTime.UtcNow.Ticks);

        if (code != HcAction)
        {
            return CallNextHookEx(0, code, message, data);
        }

        KeyboardInput input = Marshal.PtrToStructure<KeyboardInput>(data);

        // Ne pas réagir à ce que l'on injecte soi-même, sous peine de boucle.
        if ((input.Flags & LlkhfInjected) != 0)
        {
            return CallNextHookEx(0, code, message, data);
        }

        ChordDecision decision = message switch
        {
            WmKeyDown or WmSysKeyDown => _detector.OnKeyDown((int)input.VirtualKey),
            WmKeyUp or WmSysKeyUp => _detector.OnKeyUp((int)input.VirtualKey),
            _ => ChordDecision.Ignore,
        };

        if (decision.NeutralizeStartMenu)
        {
            SendNeutralKey();
        }

        switch (decision.Action)
        {
            case ChordAction.Start:
                Started?.Invoke(this, EventArgs.Empty);
                break;
            case ChordAction.Stop:
                Stopped?.Invoke(this, EventArgs.Empty);
                break;
            case ChordAction.Cancel:
                Cancelled?.Invoke(this, EventArgs.Empty);
                break;
            case ChordAction.None:
            default:
                break;
        }

        // Retourner 1 consomme l'événement : Windows ne le verra jamais.
        return decision.Swallow ? 1 : CallNextHookEx(0, code, message, data);
    }

    /// <summary>
    /// Neutralise une touche Windows que le système a déjà reçue.
    ///
    /// <para>Deux problèmes distincts, réglés par la même séquence.</para>
    ///
    /// <para><b>Le menu Démarrer.</b> Windows l'ouvre sur une touche Windows
    /// pressée puis relâchée sans autre touche entre les deux. F13 rompt cette
    /// séquence : elle n'existe sur aucun clavier vendu aujourd'hui, rien ne
    /// lui est associé.</para>
    ///
    /// <para><b>Le modificateur resté enfoncé.</b> Celui-là a coûté cher à
    /// diagnostiquer. Quand l'utilisateur presse Windows <i>avant</i> l'autre
    /// touche, l'appui a déjà été transmis au système, qui considère le
    /// modificateur actif pour toute la durée de la dictée. L'insertion
    /// injecte alors Ctrl+V — et Windows lit <b>Win+Ctrl+V</b>, son raccourci
    /// d'ouverture du panneau de sortie audio. Le panneau apparaît, vole le
    /// focus, et le texte transcrit se perd. On relâche donc explicitement les
    /// touches Windows après F13.</para>
    ///
    /// <para>L'ordre importe : F13 d'abord, sinon le relâchement injecté
    /// ouvrirait précisément le menu Démarrer qu'on cherche à éviter.</para>
    /// </summary>
    private static void SendNeutralKey()
    {
        Span<Input> inputs =
        [
            NewKeyboardInput(VirtualKeys.F13, keyUp: false),
            NewKeyboardInput(VirtualKeys.F13, keyUp: true),
            NewKeyboardInput(VirtualKeys.LeftWindows, keyUp: true),
            NewKeyboardInput(VirtualKeys.RightWindows, keyUp: true),
        ];

        SendInput((uint)inputs.Length, ref MemoryMarshal.GetReference(inputs), Marshal.SizeOf<Input>());
    }

    private static Input NewKeyboardInput(int virtualKey, bool keyUp) => new()
    {
        Type = InputKeyboard,
        Data = new InputUnion
        {
            Keyboard = new KeyboardInputData
            {
                VirtualKey = (ushort)virtualKey,
                Flags = keyUp ? KeyEventKeyUp : 0,
            },
        },
    };

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SystemEvents.SessionSwitch -= OnSessionSwitch;

        if (_hook != 0)
        {
            UnhookWindowsHookEx(_hook);
            _hook = 0;
        }
    }

    /// <summary>
    /// Vrai si Windows a enregistré une saisie utilisateur postérieure à la
    /// date fournie. Le système compte en millisecondes depuis son démarrage,
    /// d'où la conversion.
    /// </summary>
    private static bool SystemSawInputAfter(long ticks)
    {
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };

        if (!GetLastInputInfo(ref info))
        {
            // Sans information, on préfère réinstaller : un hook mort coûte
            // plus cher qu'une réinstallation inutile.
            return true;
        }

        long idleMilliseconds = Environment.TickCount64 - info.LastInputTick;
        long systemLastInputTicks = DateTime.UtcNow.Ticks - (idleMilliseconds * TimeSpan.TicksPerMillisecond);

        return systemLastInputTicks > ticks;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint LastInputTick;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetLastInputInfo(ref LastInputInfo info);

    private delegate nint HookProc(int code, nint message, nint data);

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public uint VirtualKey;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public KeyboardInputData Keyboard;

        /// <summary>
        /// Réserve la place de la plus grande variante de l'union (la souris),
        /// pour que la taille de la structure soit celle qu'attend Win32.
        /// </summary>
        [FieldOffset(0)]
        private MouseInputData _mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInputData
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInputData
    {
        public int X;
        public int Y;
        public uint Data;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint SetWindowsHookExW(int idHook, HookProc lpfn, nint hMod, uint dwThreadId);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWindowsHookEx(nint hhk);

    [LibraryImport("user32.dll")]
    private static partial nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint SendInput(uint cInputs, ref Input pInputs, int cbSize);
}
