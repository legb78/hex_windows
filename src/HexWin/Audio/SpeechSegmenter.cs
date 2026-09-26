namespace HexWin.Audio;

/// <summary>
/// Cuts a recording at the pauses in speech, so that each piece can be
/// transcribed and inserted while the user keeps talking.
///
/// <para>The engine is an offline model: it needs a whole utterance before it
/// says anything, and it rewrites its earlier words as more context arrives.
/// Streaming it word by word is out. A pause, on the other hand, ends an
/// utterance for good: what came before it can be transcribed on its own, and
/// it will never be revised. On a long dictation the text then lands sentence
/// by sentence instead of all at once at the end.</para>
///
/// <para>The detection is a plain level threshold, the one already used to
/// tell a dead microphone from a silent room. Background noise above it means
/// no pause is ever seen and the recording stays in one piece — exactly what
/// happened before segmentation existed, not a failure.</para>
///
/// <para>Pure logic, so entirely testable without a microphone.</para>
/// </summary>
public sealed class SpeechSegmenter
{
    private readonly TimeSpan _pause;
    private readonly MemoryStream _current = new();

    private long _quietBytes;
    private bool _heardSpeech;
    private int _closed;

    /// <param name="pause">
    /// Silence that closes a segment. Zero disables the cutting: the whole
    /// recording then comes out of <see cref="Flush"/> in one piece.
    /// </param>
    public SpeechSegmenter(TimeSpan pause)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pause, TimeSpan.Zero);

        _pause = pause;
    }

    public bool IsEnabled => _pause > TimeSpan.Zero;

    /// <summary>Raw samples accumulated in the segment being built.</summary>
    public long PendingBytes => _current.Length;

    /// <summary>
    /// Adds captured samples. Returns the segment they just closed, or null
    /// while the current one is still open.
    /// </summary>
    public byte[]? Push(ReadOnlySpan<byte> pcm)
    {
        _current.Write(pcm);

        if (!IsEnabled)
        {
            return null;
        }

        if (AudioLevel.IsSilent(pcm))
        {
            _quietBytes += pcm.Length;
        }
        else
        {
            _heardSpeech = true;
            _quietBytes = 0;
        }

        // Silence before any speech is not a pause, just the user drawing
        // breath: it stays attached to the words that follow.
        if (!_heardSpeech || RecordingFormat.DurationOf(_quietBytes) < _pause)
        {
            return null;
        }

        return Cut();
    }

    /// <summary>
    /// Ends the recording and returns what is left.
    ///
    /// A remainder with no speech in it is dropped rather than sent to the
    /// engine — unless nothing was cut before it, in which case it is the whole
    /// recording, and a silent recording is precisely what the level
    /// diagnostic needs to see.
    /// </summary>
    public byte[]? Flush()
    {
        if (_current.Length == 0)
        {
            return null;
        }

        if (_closed > 0 && !_heardSpeech)
        {
            Reset();
            return null;
        }

        return Cut();
    }

    private byte[] Cut()
    {
        byte[] segment = _current.ToArray();
        Reset();
        _closed++;

        return segment;
    }

    private void Reset()
    {
        _current.SetLength(0);
        _quietBytes = 0;
        _heardSpeech = false;
    }
}
