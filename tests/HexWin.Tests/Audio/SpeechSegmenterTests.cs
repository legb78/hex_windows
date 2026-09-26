using HexWin.Audio;
using Xunit;

namespace HexWin.Tests.Audio;

public class SpeechSegmenterTests
{
    private static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(500);

    /// <summary>One capture buffer, as the microphone delivers them.</summary>
    private static readonly int Chunk = (int)RecordingFormat.BytesFor(TimeSpan.FromMilliseconds(50));

    private static byte[] Silence() => new byte[Chunk];

    private static byte[] Speech()
    {
        byte[] pcm = new byte[Chunk];

        // Alternating loud samples, well above the silence threshold.
        for (int i = 0; i + 1 < pcm.Length; i += RecordingFormat.BytesPerSample)
        {
            short sample = (i / RecordingFormat.BytesPerSample) % 2 == 0 ? (short)8_000 : (short)-8_000;
            pcm[i] = (byte)sample;
            pcm[i + 1] = (byte)(sample >> 8);
        }

        return pcm;
    }

    private static byte[]? Feed(SpeechSegmenter segmenter, params byte[][] chunks)
    {
        byte[]? closed = null;

        foreach (byte[] chunk in chunks)
        {
            closed ??= segmenter.Push(chunk);
        }

        return closed;
    }

    private static byte[][] Repeat(byte[] chunk, int count) =>
        [.. Enumerable.Repeat(chunk, count)];

    [Fact]
    public void Speech_followed_by_a_pause_closes_a_segment()
    {
        var segmenter = new SpeechSegmenter(Pause);

        Feed(segmenter, Repeat(Speech(), 4));
        byte[]? closed = Feed(segmenter, Repeat(Silence(), 10));

        Assert.NotNull(closed);
        Assert.Equal(14 * Chunk, closed.Length);
        Assert.Equal(0, segmenter.PendingBytes);
    }

    [Fact]
    public void A_shorter_gap_does_not_close_anything()
    {
        // Gaps between words are far shorter than a pause between sentences.
        var segmenter = new SpeechSegmenter(Pause);

        byte[]? closed = Feed(segmenter, Speech(), Silence(), Silence(), Speech());

        Assert.Null(closed);
        Assert.Equal(4 * Chunk, segmenter.PendingBytes);
    }

    [Fact]
    public void Silence_before_any_speech_is_not_a_pause()
    {
        var segmenter = new SpeechSegmenter(Pause);

        byte[]? closed = Feed(segmenter, Repeat(Silence(), 40));

        Assert.Null(closed);
        Assert.Equal(40 * Chunk, segmenter.PendingBytes);
    }

    [Fact]
    public void Each_pause_closes_one_segment()
    {
        var segmenter = new SpeechSegmenter(Pause);

        byte[]? first = Feed(segmenter, [.. Repeat(Speech(), 2), .. Repeat(Silence(), 10)]);
        byte[]? second = Feed(segmenter, [.. Repeat(Speech(), 3), .. Repeat(Silence(), 10)]);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(12 * Chunk, first.Length);
        Assert.Equal(13 * Chunk, second.Length);
    }

    [Fact]
    public void Flushing_returns_the_remainder()
    {
        var segmenter = new SpeechSegmenter(Pause);
        Feed(segmenter, Repeat(Speech(), 3));

        byte[]? remainder = segmenter.Flush();

        Assert.NotNull(remainder);
        Assert.Equal(3 * Chunk, remainder.Length);
        Assert.Equal(0, segmenter.PendingBytes);
    }

    [Fact]
    public void A_silent_tail_after_a_cut_is_dropped()
    {
        // The user stopped talking and let go of the key a moment later:
        // there is nothing left worth transcribing.
        var segmenter = new SpeechSegmenter(Pause);
        Feed(segmenter, [.. Repeat(Speech(), 2), .. Repeat(Silence(), 10)]);
        Feed(segmenter, Repeat(Silence(), 3));

        Assert.Null(segmenter.Flush());
        Assert.Equal(0, segmenter.PendingBytes);
    }

    [Fact]
    public void A_recording_with_no_speech_at_all_is_still_returned()
    {
        // A dead microphone must reach the level diagnostic, which is the
        // only thing that can name it.
        var segmenter = new SpeechSegmenter(Pause);
        Feed(segmenter, Repeat(Silence(), 20));

        byte[]? whole = segmenter.Flush();

        Assert.NotNull(whole);
        Assert.Equal(20 * Chunk, whole.Length);
    }

    [Fact]
    public void Flushing_an_empty_segmenter_returns_nothing()
    {
        Assert.Null(new SpeechSegmenter(Pause).Flush());
    }

    [Fact]
    public void A_zero_pause_keeps_the_recording_in_one_piece()
    {
        var segmenter = new SpeechSegmenter(TimeSpan.Zero);

        Assert.False(segmenter.IsEnabled);
        Assert.Null(Feed(segmenter, [.. Repeat(Speech(), 2), .. Repeat(Silence(), 40)]));
        Assert.Equal(42 * Chunk, segmenter.Flush()!.Length);
    }

    [Fact]
    public void A_negative_pause_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SpeechSegmenter(TimeSpan.FromSeconds(-1)));
    }
}
