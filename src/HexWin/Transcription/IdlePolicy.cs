namespace HexWin.Transcription;

/// <summary>
/// Decides when the model should be released for lack of use.
///
/// The model takes about a gigabyte once loaded. Keeping it resident answers
/// in a few tens of milliseconds; releasing it hands that memory back to the
/// system at the cost of a reload of a few seconds on the next dictation.
///
/// That cost is largely hidden in practice: the reload is triggered on the key
/// <i>press</i>, so while the user is still speaking, rather than on release.
///
/// Pure logic: the clock is supplied by the caller, which makes every scenario
/// describable in a test without actually waiting.
/// </summary>
public readonly record struct IdlePolicy(TimeSpan Timeout)
{
    /// <summary>A zero or negative delay keeps the model resident forever.</summary>
    public bool IsEnabled => Timeout > TimeSpan.Zero;

    public static IdlePolicy FromMinutes(int minutes) =>
        new(minutes > 0 ? TimeSpan.FromMinutes(minutes) : TimeSpan.Zero);

    /// <summary>
    /// True if the model should be released now.
    /// </summary>
    /// <param name="sinceLastUse">Time elapsed since the last dictation.</param>
    /// <param name="isBusy">
    /// True if a dictation is under way. Releasing at that moment would fail
    /// the very transcription the user is waiting for — the idle deadline can
    /// fall exactly while they are speaking.
    /// </param>
    public bool ShouldUnload(TimeSpan sinceLastUse, bool isBusy)
    {
        if (!IsEnabled || isBusy)
        {
            return false;
        }

        return sinceLastUse >= Timeout;
    }
}
