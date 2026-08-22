using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using HexWin.Configuration;
using HexWin.Input;

namespace HexWin.Output;

/// <summary>
/// Remet le texte transcrit à l'application active.
///
/// Deux voies. Le collage passe par le presse-papiers puis Ctrl+V : instantané
/// quel que soit le volume, c'est le défaut. La frappe simulée envoie les
/// caractères un à un, plus lentement, mais elle passe dans les applications
/// qui ignorent le presse-papiers.
///
/// Coquille Win32 : la construction des frappes appartient à
/// <see cref="UnicodeKeystrokes"/>, qui se teste.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Coquille Win32 : SendInput écrit dans la fenêtre active du bureau réel.")]
internal sealed partial class TextInjector
{
    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint KeyEventUnicode = 0x0004;

    private const int ControlKey = VirtualKeys.LeftControl;
    private const ushort KeyV = 0x56;

    /// <summary>
    /// Délai laissé à l'application cible pour lire le presse-papiers avant
    /// qu'on ne le restaure. Trop court, le collage récupérerait l'ancien
    /// contenu ; trop long, l'utilisateur retrouverait le texte dicté dans son
    /// presse-papiers s'il enchaîne aussitôt sur un autre collage.
    /// </summary>
    private static readonly TimeSpan ClipboardRestoreDelay = TimeSpan.FromMilliseconds(400);

    /// <summary>
    /// Insère le texte dans le champ actif selon le mode demandé.
    /// </summary>
    public static void Insert(string text, InsertionMode mode)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (mode == InsertionMode.Type)
        {
            SendAsKeystrokes(text);
            return;
        }

        SendAsPaste(text);
    }

    /// <summary>
    /// Colle par le presse-papiers, en restaurant ensuite ce qui s'y trouvait.
    ///
    /// La restauration compte : sans elle, chaque dictée écraserait ce que
    /// l'utilisateur avait copié, ce qui se remarque au pire moment.
    /// </summary>
    private static void SendAsPaste(string text)
    {
        ClipboardSnapshot previous = ClipboardSnapshot.Capture();

        // copy: true conserve les données après la fin du processus. Sans ce
        // drapeau, le texte disparaîtrait du presse-papiers dès la fermeture
        // de l'application, et l'utilisateur ne pourrait plus le recoller.
        Clipboard.SetDataObject(text, copy: true);
        SendPasteShortcut();

        // La restauration est différée : Ctrl+V est asynchrone, l'application
        // cible n'a pas encore lu le presse-papiers au retour de SendInput.
        RestoreAfterDelay(previous);
    }

    /// <summary>
    /// Réécrit le contenu sauvegardé après un court délai, sur le fil courant.
    ///
    /// <para>Ce délai bloque volontairement l'appelant. Une première version
    /// déportait la restauration sur un fil dédié : marquer ce fil STA ne
    /// suffit pas, les API de presse-papiers exigent aussi une initialisation
    /// OLE que <c>Thread</c> ne fournit pas. La restauration échouait alors en
    /// silence, et le presse-papiers de l'utilisateur restait perdu.</para>
    ///
    /// <para>Bloquer est ici sans conséquence : l'application n'a pas de
    /// fenêtre, et le texte est déjà collé quand l'attente commence.</para>
    /// </summary>
    private static void RestoreAfterDelay(ClipboardSnapshot previous)
    {
        if (!previous.HasContent)
        {
            return;
        }

        Thread.Sleep(ClipboardRestoreDelay);
        previous.Restore();
    }

    /// <summary>
    /// Relâche les modificateurs que le système croit encore enfoncés, avant
    /// d'injecter un raccourci.
    ///
    /// <para>Sans cette précaution, <c>Ctrl+V</c> se combine avec ce qui reste
    /// actif et devient un tout autre raccourci. Le cas rencontré : la touche
    /// Windows encore tenue transformait le collage en <b>Win+Ctrl+V</b>, qui
    /// ouvre le panneau de sortie audio de Windows. Le panneau volait le
    /// focus, et le texte transcrit disparaissait — sans erreur, sans trace,
    /// et de façon intermittente selon l'ordre dans lequel l'utilisateur
    /// relâchait ses touches.</para>
    ///
    /// <para>On n'injecte le relâchement que pour les touches réellement
    /// actives : un relâchement superflu est inoffensif, mais autant ne pas
    /// polluer la file d'événements.</para>
    /// </summary>
    private static void ReleaseStrayModifiers()
    {
        int[] stray = [.. ModifiersToClear.Where(IsPhysicallyDown)];

        if (stray.Length == 0)
        {
            return;
        }

        Input[] inputs = [.. stray.Select(key => NewVirtualKey((ushort)key, keyUp: true))];

        Send(inputs);
    }

    /// <summary>
    /// Modificateurs susceptibles de détourner Ctrl+V. Le Ctrl que l'on
    /// injecte soi-même n'y figure pas, évidemment.
    /// </summary>
    private static readonly int[] ModifiersToClear =
    [
        VirtualKeys.LeftWindows,
        VirtualKeys.RightWindows,
        VirtualKeys.LeftMenu,
        VirtualKeys.RightMenu,
        VirtualKeys.LeftShift,
        VirtualKeys.RightShift,
    ];

    /// <summary>Le bit de poids fort indique une touche actuellement enfoncée.</summary>
    private static bool IsPhysicallyDown(int virtualKey) =>
        (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    private static void SendPasteShortcut()
    {
        ReleaseStrayModifiers();

        Span<Input> inputs =
        [
            NewVirtualKey((ushort)ControlKey, keyUp: false),
            NewVirtualKey(KeyV, keyUp: false),
            NewVirtualKey(KeyV, keyUp: true),
            NewVirtualKey((ushort)ControlKey, keyUp: true),
        ];

        Send(inputs);
    }

    private static void SendAsKeystrokes(string text)
    {
        Keystroke[] strokes = UnicodeKeystrokes.Build(text);
        Input[] inputs = new Input[strokes.Length];

        for (int i = 0; i < strokes.Length; i++)
        {
            Keystroke stroke = strokes[i];

            inputs[i] = UnicodeKeystrokes.IsReturn(stroke)
                ? NewVirtualKey(stroke.Unit, stroke.IsKeyUp)
                : NewUnicodeKey(stroke.Unit, stroke.IsKeyUp);
        }

        Send(inputs);
    }

    private static void Send(Span<Input> inputs)
    {
        if (inputs.Length == 0)
        {
            return;
        }

        SendInput((uint)inputs.Length, ref MemoryMarshal.GetReference(inputs), Marshal.SizeOf<Input>());
    }

    private static Input NewUnicodeKey(ushort unit, bool keyUp) => new()
    {
        Type = InputKeyboard,
        Data = new InputUnion
        {
            Keyboard = new KeyboardInputData
            {
                // En mode Unicode, le caractère voyage dans le code de scan et
                // le code virtuel doit rester nul.
                VirtualKey = 0,
                ScanCode = unit,
                Flags = KeyEventUnicode | (keyUp ? KeyEventKeyUp : 0),
            },
        },
    };

    private static Input NewVirtualKey(ushort virtualKey, bool keyUp) => new()
    {
        Type = InputKeyboard,
        Data = new InputUnion
        {
            Keyboard = new KeyboardInputData
            {
                VirtualKey = virtualKey,
                Flags = keyUp ? KeyEventKeyUp : 0,
            },
        },
    };

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
        /// Réserve la place de la plus grande variante de l'union, pour que la
        /// taille de la structure soit celle qu'attend Win32.
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
    private static partial uint SendInput(uint cInputs, ref Input pInputs, int cbSize);

    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int vKey);
}
