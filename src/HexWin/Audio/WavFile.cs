namespace HexWin.Audio;

/// <summary>
/// Writes the WAV header that precedes the raw samples.
///
/// WAV is the internal exchange format: what the recorder returns, what the
/// diagnostic mode writes to disk, and what the engine reads back. The header
/// is built by hand because library writers close the underlying stream,
/// whereas a recording lives in a MemoryStream that has to be read afterwards.
/// A wrong header raises nothing: the engine loads it and transcribes noise.
/// Hence the tests on the bytes produced.
/// </summary>
public static class WavFile
{
    /// <summary>Size of the canonical RIFF/WAVE header, in bytes.</summary>
    public const int HeaderSize = 44;

    /// <summary>
    /// Assembles a complete WAV file from raw samples in
    /// <see cref="RecordingFormat"/>.
    /// </summary>
    public static byte[] Create(ReadOnlySpan<byte> pcm)
    {
        byte[] file = new byte[HeaderSize + pcm.Length];

        WriteHeader(file, pcm.Length);
        pcm.CopyTo(file.AsSpan(HeaderSize));

        return file;
    }

    /// <summary>Entirely silent WAV file, useful for warming up.</summary>
    public static byte[] CreateSilence(TimeSpan duration) =>
        Create(new byte[RecordingFormat.BytesFor(duration)]);

    /// <summary>
    /// Writes the header into the first 44 bytes of <paramref name="target"/>.
    /// </summary>
    public static void WriteHeader(Span<byte> target, int pcmByteCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pcmByteCount);

        if (target.Length < HeaderSize)
        {
            throw new ArgumentException(
                $"A WAV header takes up {HeaderSize} bytes.", nameof(target));
        }

        const int fmtChunkSize = 16;
        const short pcmFormat = 1;

        Write(target, 0, "RIFF"u8);
        WriteInt32(target, 4, 36 + pcmByteCount);          // total size minus 8
        Write(target, 8, "WAVE"u8);

        Write(target, 12, "fmt "u8);
        WriteInt32(target, 16, fmtChunkSize);
        WriteInt16(target, 20, pcmFormat);                 // uncompressed PCM
        WriteInt16(target, 22, RecordingFormat.Channels);
        WriteInt32(target, 24, RecordingFormat.SampleRate);
        WriteInt32(target, 28, RecordingFormat.BytesPerSecond);
        WriteInt16(target, 32, RecordingFormat.Channels * RecordingFormat.BytesPerSample);
        WriteInt16(target, 34, RecordingFormat.BitsPerSample);

        Write(target, 36, "data"u8);
        WriteInt32(target, 40, pcmByteCount);
    }

    private static void Write(Span<byte> target, int offset, ReadOnlySpan<byte> value) =>
        value.CopyTo(target[offset..]);

    private static void WriteInt32(Span<byte> target, int offset, int value)
    {
        // WAV is little-endian, whatever the machine.
        target[offset] = (byte)value;
        target[offset + 1] = (byte)(value >> 8);
        target[offset + 2] = (byte)(value >> 16);
        target[offset + 3] = (byte)(value >> 24);
    }

    private static void WriteInt16(Span<byte> target, int offset, int value)
    {
        target[offset] = (byte)value;
        target[offset + 1] = (byte)(value >> 8);
    }
}
