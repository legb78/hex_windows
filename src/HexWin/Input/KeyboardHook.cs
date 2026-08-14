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

    private nint OnKeyboardEvent(int code, nint message, nint data)
    {
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
    /// Injecte une touche sans effet pour rompre la séquence « Windows pressée
    /// puis relâchée », seule condition d'ouverture du menu Démarrer.
    ///
    /// F13 n'existe sur aucun clavier physique vendu aujourd'hui : rien ne lui
    /// est associé, et la combinaison Ctrl+Win+F13 n'est revendiquée par
    /// aucune application connue.
    /// </summary>
    private static void SendNeutralKey()
    {
        Span<Input> inputs =
        [
            NewKeyboardInput(VirtualKeys.F13, keyUp: false),
            NewKeyboardInput(VirtualKeys.F13, keyUp: true),
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
