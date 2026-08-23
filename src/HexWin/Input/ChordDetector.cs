namespace HexWin.Input;

/// <summary>What the shortcut is asking the application to do.</summary>
public enum ChordAction
{
    None,

    /// <summary>Every key is down: start recording.</summary>
    Start,

    /// <summary>A key was released: transcribe what was said.</summary>
    Stop,

    /// <summary>A foreign key intervened: give up without transcribing.</summary>
    Cancel,
}

/// <summary>Decision taken for one keyboard event.</summary>
/// <param name="Action">What the application must do.</param>
/// <param name="Swallow">
/// When true, the event must not be passed on to Windows.
/// </param>
/// <param name="NeutralizeStartMenu">
/// When true, the caller must inject a neutral key to stop the Start menu from
/// opening. See the note on press order.
/// </param>
public readonly record struct ChordDecision(
    ChordAction Action,
    bool Swallow,
    bool NeutralizeStartMenu = false)
{
    public static readonly ChordDecision Ignore = new(ChordAction.None, Swallow: false);
}

/// <summary>
/// State machine for the hold-to-dictate shortcut.
///
/// Entirely pure logic: it knows nothing of Win32 or of the keyboard, it just
/// receives key codes and decides. That is what makes it testable, whereas the
/// hook itself is not.
///
/// <para><b>The rule that governs everything: swallowing must be balanced.</b>
/// A swallowed key-down requires the matching key-up to be swallowed too, and
/// the other way round. Breaking that balance leaves Windows convinced a
/// modifier is still held: the keyboard becomes unusable until the phantom key
/// is pressed again.</para>
///
/// <para>On the Start menu: Windows opens it when the Windows key is pressed
/// and released with no other key in between. Since the key that completes the
/// shortcut is swallowed, Windows never sees that press — unless the user
/// pressed Windows <i>first</i>, in which case the press has already been
/// delivered. Hence <see cref="ChordDecision.NeutralizeStartMenu"/>, which then
/// asks the caller to inject a key with no effect.</para>
/// </summary>
public sealed class ChordDetector
{
    /// <summary>
    /// Codes accepted for each key of the shortcut. One slot per requested
    /// key; "Ctrl" accepts the left key as well as the right one.
    /// </summary>
    private readonly int[][] _requirements;

    /// <summary>Code actually held that satisfies each slot, or 0.</summary>
    private readonly int[] _satisfiedBy;

    /// <summary>Keys whose key-down we swallowed.</summary>
    private readonly HashSet<int> _swallowed = [];

    public ChordDetector(IEnumerable<string> keyNames)
    {
        ArgumentNullException.ThrowIfNull(keyNames);

        _requirements = [.. keyNames.Select(VirtualKeys.Resolve)];

        if (_requirements.Length == 0)
        {
            throw new ArgumentException("The shortcut must hold at least one key.", nameof(keyNames));
        }

        _satisfiedBy = new int[_requirements.Length];
    }

    /// <summary>True between the moment the shortcut completes and its release.</summary>
    public bool IsActive { get; private set; }

    public ChordDecision OnKeyDown(int virtualKey)
    {
        // Auto-repeat: holding a key sends key-downs in bursts. They must not
        // restart the recording, but they must stay swallowed if the initial
        // press was.
        if (IsAlreadySatisfying(virtualKey))
        {
            return new ChordDecision(ChordAction.None, _swallowed.Contains(virtualKey));
        }

        int slot = FindFreeSlot(virtualKey);

        if (slot < 0)
        {
            // Key foreign to the shortcut. During a dictation it interrupts:
            // Ctrl+Win+D creates a virtual desktop, and the user was not
            // asking for a transcription.
            if (IsActive)
            {
                IsActive = false;
                return new ChordDecision(ChordAction.Cancel, Swallow: false);
            }

            return ChordDecision.Ignore;
        }

        _satisfiedBy[slot] = virtualKey;

        if (!AllSatisfied() || IsActive)
        {
            // Shortcut still incomplete: let it through. Swallowing here would
            // break Ctrl+C, since there is no way yet to know whether the user
            // is aiming for our shortcut.
            return ChordDecision.Ignore;
        }

        IsActive = true;
        _swallowed.Add(virtualKey);

        return new ChordDecision(
            ChordAction.Start,
            Swallow: true,
            NeutralizeStartMenu: WindowsKeyAlreadyDelivered(virtualKey));
    }

    public ChordDecision OnKeyUp(int virtualKey)
    {
        bool swallow = _swallowed.Remove(virtualKey);
        int slot = FindSatisfiedSlot(virtualKey);

        if (slot < 0)
        {
            return new ChordDecision(ChordAction.None, swallow);
        }

        _satisfiedBy[slot] = 0;

        if (!IsActive)
        {
            return new ChordDecision(ChordAction.None, swallow);
        }

        IsActive = false;
        return new ChordDecision(ChordAction.Stop, swallow);
    }

    /// <summary>
    /// Forgets any state in progress.
    ///
    /// Indispensable when Windows stops delivering key-ups: a locked session,
    /// a user switch, an elevated dialog taking the keyboard. Without a reset,
    /// a key would stay "held" forever and the shortcut would stop responding.
    /// </summary>
    public ChordDecision Reset()
    {
        bool wasActive = IsActive;

        Array.Clear(_satisfiedBy);
        _swallowed.Clear();
        IsActive = false;

        return wasActive
            ? new ChordDecision(ChordAction.Cancel, Swallow: false)
            : ChordDecision.Ignore;
    }

    private bool IsAlreadySatisfying(int virtualKey) =>
        Array.IndexOf(_satisfiedBy, virtualKey) >= 0;

    private int FindFreeSlot(int virtualKey)
    {
        for (int i = 0; i < _requirements.Length; i++)
        {
            if (_satisfiedBy[i] == 0 && Array.IndexOf(_requirements[i], virtualKey) >= 0)
            {
                return i;
            }
        }

        return -1;
    }

    private int FindSatisfiedSlot(int virtualKey) => Array.IndexOf(_satisfiedBy, virtualKey);

    private bool AllSatisfied() => Array.IndexOf(_satisfiedBy, 0) < 0;

    /// <summary>
    /// True when a Windows key of the shortcut was delivered to Windows before
    /// the shortcut completed — that is, when the user pressed it first.
    /// </summary>
    private bool WindowsKeyAlreadyDelivered(int completingKey) =>
        _satisfiedBy.Any(key => key != 0 && key != completingKey && VirtualKeys.IsWindowsKey(key));
}
