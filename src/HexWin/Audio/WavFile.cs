namespace HexWin.Audio;

/// <summary>
/// Écrit l'en-tête WAV qui précède les échantillons bruts.
///
/// whisper.cpp attend un fichier WAV complet, pas du PCM nu. On construit
/// l'en-tête à la main plutôt que de passer par un écrivain de bibliothèque :
/// ces écrivains referment le flux sous-jacent quand on les libère, alors que
/// l'enregistrement vit dans un MemoryStream qu'il faut ensuite relire.
///
/// Un en-tête faux ne provoque pas une erreur claire : whisper le charge et
/// transcrit du bruit. D'où les tests sur les octets produits.
/// </summary>
public static class WavFile
{
    /// <summary>Taille de l'en-tête RIFF/WAVE canonique, en octets.</summary>
    public const int HeaderSize = 44;

    /// <summary>
    /// Assemble un fichier WAV complet à partir d'échantillons bruts au
    /// format <see cref="RecordingFormat"/>.
    /// </summary>
    public static byte[] Create(ReadOnlySpan<byte> pcm)
    {
        byte[] file = new byte[HeaderSize + pcm.Length];

        WriteHeader(file, pcm.Length);
        pcm.CopyTo(file.AsSpan(HeaderSize));

        return file;
    }

    /// <summary>Fichier WAV entièrement silencieux, utile au préchauffage.</summary>
    public static byte[] CreateSilence(TimeSpan duration) =>
        Create(new byte[RecordingFormat.BytesFor(duration)]);

    /// <summary>
    /// Écrit l'en-tête dans les 44 premiers octets de <paramref name="target"/>.
    /// </summary>
    public static void WriteHeader(Span<byte> target, int pcmByteCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pcmByteCount);

        if (target.Length < HeaderSize)
        {
            throw new ArgumentException(
                $"L'en-tête WAV occupe {HeaderSize} octets.", nameof(target));
        }

        const int fmtChunkSize = 16;
        const short pcmFormat = 1;

        Write(target, 0, "RIFF"u8);
        WriteInt32(target, 4, 36 + pcmByteCount);          // taille totale moins 8
        Write(target, 8, "WAVE"u8);

        Write(target, 12, "fmt "u8);
        WriteInt32(target, 16, fmtChunkSize);
        WriteInt16(target, 20, pcmFormat);                 // PCM non compressé
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
        // WAV est petit-boutiste, quelle que soit la machine.
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
