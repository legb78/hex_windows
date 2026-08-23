using System.Text;
using HexWin.Audio;
using Xunit;

namespace HexWin.Tests.Audio;

/// <summary>
/// The header is checked byte by byte, because a mistake here raises no
/// exception: the engine loads the file and transcribes noise. The symptom
/// would be "dictation returns nonsense", a long way from the cause.
/// </summary>
public class WavFileTests
{
    private static readonly byte[] Pcm = [1, 2, 3, 4, 5, 6, 7, 8];

    private static string Ascii(byte[] file, int offset, int length) =>
        Encoding.ASCII.GetString(file, offset, length);

    private static int Int32At(byte[] file, int offset) => BitConverter.ToInt32(file, offset);

    private static short Int16At(byte[] file, int offset) => BitConverter.ToInt16(file, offset);

    [Fact]
    public void The_file_starts_with_the_RIFF_WAVE_signature()
    {
        byte[] file = WavFile.Create(Pcm);

        Assert.Equal("RIFF", Ascii(file, 0, 4));
        Assert.Equal("WAVE", Ascii(file, 8, 4));
        Assert.Equal("fmt ", Ascii(file, 12, 4));
        Assert.Equal("data", Ascii(file, 36, 4));
    }

    [Fact]
    public void The_RIFF_size_is_the_total_size_minus_eight()
    {
        byte[] file = WavFile.Create(Pcm);

        Assert.Equal(file.Length - 8, Int32At(file, 4));
    }

    [Fact]
    public void The_data_chunk_announces_the_real_sample_size()
    {
        byte[] file = WavFile.Create(Pcm);

        Assert.Equal(Pcm.Length, Int32At(file, 40));
    }

    [Fact]
    public void The_announced_format_is_the_one_the_engine_expects()
    {
        byte[] file = WavFile.Create(Pcm);

        Assert.Equal(16, Int32At(file, 16));                          // fmt chunk size
        Assert.Equal(1, Int16At(file, 20));                           // uncompressed PCM
        Assert.Equal(RecordingFormat.Channels, Int16At(file, 22));    // mono
        Assert.Equal(RecordingFormat.SampleRate, Int32At(file, 24));  // 16 kHz
        Assert.Equal(RecordingFormat.BitsPerSample, Int16At(file, 34));
    }

    [Fact]
    public void The_bitrate_and_alignment_agree_with_the_format()
    {
        // These two fields are redundant with the ones above. A reader that
        // trusts them would play the stream at the wrong speed if they were
        // wrong.
        byte[] file = WavFile.Create(Pcm);

        Assert.Equal(RecordingFormat.BytesPerSecond, Int32At(file, 28));
        Assert.Equal(RecordingFormat.Channels * RecordingFormat.BytesPerSample, Int16At(file, 32));
    }

    [Fact]
    public void The_samples_follow_the_header_unaltered()
    {
        byte[] file = WavFile.Create(Pcm);

        Assert.Equal(WavFile.HeaderSize + Pcm.Length, file.Length);
        Assert.Equal(Pcm, file[WavFile.HeaderSize..]);
    }

    [Fact]
    public void An_empty_recording_produces_a_valid_header_alone()
    {
        byte[] file = WavFile.Create([]);

        Assert.Equal(WavFile.HeaderSize, file.Length);
        Assert.Equal("RIFF", Ascii(file, 0, 4));
        Assert.Equal(0, Int32At(file, 40));
    }

    [Fact]
    public void The_silence_lasts_as_long_as_asked()
    {
        byte[] file = WavFile.CreateSilence(TimeSpan.FromSeconds(1));

        Assert.Equal(RecordingFormat.BytesPerSecond, Int32At(file, 40));
        Assert.All(file[WavFile.HeaderSize..], sample => Assert.Equal(0, sample));
    }

    [Fact]
    public void An_area_too_small_for_the_header_is_refused()
    {
        Assert.Throws<ArgumentException>(() => WavFile.WriteHeader(new byte[10], 0));
    }
}
