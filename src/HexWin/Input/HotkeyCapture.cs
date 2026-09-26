namespace HexWin.Input;

/// <summary>Where a shortcut capture stands after one keyboard event.</summary>
public enum CaptureState
{
    /// <summary>Still waiting: nothing pressed yet, or keys still held.</summary>
    Listening,

    /// <summary>Every key was released: <see cref="CaptureStep.Keys"/> is the shortcut.</summary>
    Captured,

    /// <summary>Escape on its own: the user gave up.</summary>
    Cancelled,

    /// <summary>
    /// A key that cannot be part of a shortcut was pressed. The gesture is
    /// dropped and the capture listens again from scratch.
    /// </summary>
    Unsupported,
}

/// <summary>Outcome of one keyboard event during a capture.</summary>
/// <param name="State">Where the capture stands.</param>
/// <param name="Keys">
/// While listening, the keys pressed so far, for display; once captured, the
/// shortcut in settings.json names.
/// </param>
public readonly record struct CaptureStep(CaptureState State, IReadOnlyList<string> Keys);

/// <summary>
/// Turns a gesture on the keyboard into a shortcut: press the keys, let go,
/// and whatever was held at its widest is the new shortcut.
///
/// <para>Pure logic, fed with virtual codes by the keyboard hook. Reading the
/// keys through the hook rather than through the window's KeyDown is not a
/// detail: a window is told "Shift", never which Shift, and never sees the
/// Windows key at all — while those are exactly the keys a hold-to-dictate
/// shortcut is made of.</para>
///
/// <para>The shortcut is complete on the release, not on the press. On the
/// press there is no way to tell "Ctrl" from "Ctrl, then Win in a moment".</para>
/// </summary>
public sealed class HotkeyCapture
{
    private const int Escape = 0x1B;

    /// <summary>Every key of the gesture, in the order it was first pressed.</summary>
    private readonly List<int> _pressed = [];

    private readonly HashSet<int> _held = [];

    public CaptureStep OnKeyDown(int virtualKey)
    {
        // Auto-repeat sends a stream of key-downs for a held key.
        if (_held.Contains(virtualKey))
        {
            return Listening();
        }

        if (virtualKey == Escape && _held.Count == 0)
        {
            Clear();
            return new CaptureStep(CaptureState.Cancelled, []);
        }

        if (VirtualKeys.NameOf(virtualKey) is null)
        {
            // A letter or any other ordinary key. Accepting it would make it
            // unusable for typing, which is never what the user meant.
            Clear();
            return new CaptureStep(CaptureState.Unsupported, []);
        }

        _held.Add(virtualKey);

        if (!_pressed.Contains(virtualKey))
        {
            _pressed.Add(virtualKey);
        }

        return Listening();
    }

    public CaptureStep OnKeyUp(int virtualKey)
    {
        // A key-up whose key-down came before the capture started, or belonged
        // to a gesture already dropped: it says nothing about the new shortcut.
        if (!_held.Remove(virtualKey))
        {
            return Listening();
        }

        if (_held.Count > 0)
        {
            return Listening();
        }

        string[] shortcut = Names();
        Clear();

        return new CaptureStep(CaptureState.Captured, shortcut);
    }

    private CaptureStep Listening() => new(CaptureState.Listening, Names());

    private string[] Names() => [.. _pressed.Select(key => VirtualKeys.NameOf(key)!)];

    private void Clear()
    {
        _pressed.Clear();
        _held.Clear();
    }
}
