namespace HexWin.Audio;

/// <summary>
/// Tells speech from silence, one block of captured samples at a time.
/// </summary>
public interface ISpeechDetector
{
    bool IsSpeech(ReadOnlySpan<byte> pcm);

    /// <summary>Forgets the previous recording before a new one starts.</summary>
    void Reset();
}

/// <summary>
/// The fallback detector: anything louder than the silence threshold counts
/// as speech. Enough in a quiet room; in a noisy one the noise never falls
/// below the threshold, no pause is ever seen, and the recording stays in one
/// piece.
/// </summary>
public sealed class LevelSpeechDetector : ISpeechDetector
{
    public bool IsSpeech(ReadOnlySpan<byte> pcm) => !AudioLevel.IsSilent(pcm);

    public void Reset()
    {
    }
}
