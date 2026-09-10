using System.Diagnostics.CodeAnalysis;
using HexWin.Diagnostics;
using NAudio;
using NAudio.Wave;

namespace HexWin.Feedback;

/// <summary>
/// The two short tones marking the boundaries of a recording.
///
/// <para>Rendered into memory once at startup rather than on each dictation:
/// the start tone has to sound at the very moment the microphone opens, and
/// synthesising a waveform inside the keyboard hook callback would be time
/// spent where there is none to spare.</para>
///
/// <para>Each tone fades in and out over a few milliseconds. A sine cut off
/// squarely ends on a step in the waveform, heard as a click — on a tone this
/// short, louder than the tone itself.</para>
///
/// <para><b>No audio failure may cost a dictation.</b> A machine with no output
/// device, or one whose device is already taken, simply gets no tone: the
/// failure is logged once and dictation carries on.</para>
/// </summary>
[ExcludeFromCodeCoverage(Justification = "NAudio shell: needs a real output device.")]
internal sealed class CueTones
{
    private const int SampleRate = 44_100;
    private const int ToneMilliseconds = 70;
    private const int FadeMilliseconds = 5;

    /// <summary>Well below full scale: a cue, not an alarm.</summary>
    private const double Amplitude = 0.22;

    /// <summary>
    /// Higher going in, lower coming out. The direction is then audible without
    /// having to be learnt, which is what matters when the two tones are only a
    /// second apart.
    /// </summary>
    private const double StartFrequency = 880;
    private const double EndFrequency = 587;

    private readonly WaveFormat _format = new(SampleRate, 16, 1);
    private readonly byte[] _start = Render(StartFrequency);
    private readonly byte[] _end = Render(EndFrequency);
    private readonly SessionLog _log;

    private bool _failureLogged;

    public CueTones(SessionLog log) => _log = log;

    public void Play(CueTone tone)
    {
        byte[]? pcm = tone switch
        {
            CueTone.Start => _start,
            CueTone.End => _end,
            _ => null,
        };

        if (pcm is null)
        {
            return;
        }

        WaveOutEvent? output = null;

        try
        {
            // Default buffering, deliberately. A shorter one was tried while
            // chasing a tone that broke in two; the cause turned out to be the
            // microphone opening (see TrayContext.OnDictationStarted), and the
            // buffer size had nothing to do with it.
            output = new WaveOutEvent();

            // Released by the event rather than here: Play returns at once,
            // while the buffer is still being consumed.
            output.PlaybackStopped += (sender, _) => (sender as IDisposable)?.Dispose();
            output.Init(new RawSourceWaveStream(new MemoryStream(pcm), _format));
            output.Play();
        }
        catch (Exception ex) when (ex is MmException or InvalidOperationException or ArgumentException)
        {
            output?.Dispose();
            ReportOnce(ex);
        }
    }

    /// <summary>
    /// One line, not one per dictation: a machine with no sound card would
    /// otherwise fill the log with the same sentence.
    /// </summary>
    private void ReportOnce(Exception error)
    {
        if (_failureLogged)
        {
            return;
        }

        _failureLogged = true;
        _log.Write($"retour sonore indisponible : {error.Message}");
    }

    /// <summary>Builds one tone as 16-bit mono PCM.</summary>
    private static byte[] Render(double frequency)
    {
        int samples = SampleRate * ToneMilliseconds / 1000;
        int fade = SampleRate * FadeMilliseconds / 1000;

        byte[] pcm = new byte[samples * sizeof(short)];

        for (int index = 0; index < samples; index++)
        {
            double envelope = Math.Min(1.0, Math.Min(index, samples - 1 - index) / (double)fade);
            double value = Amplitude * envelope * Math.Sin(2 * Math.PI * frequency * index / SampleRate);

            short sample = (short)(value * short.MaxValue);

            pcm[index * 2] = (byte)(sample & 0xFF);
            pcm[(index * 2) + 1] = (byte)((sample >> 8) & 0xFF);
        }

        return pcm;
    }
}
