using NAudio.Wave;

namespace HexWin.Audio;

/// <summary>Un enregistrement retenu, prêt à être transcrit.</summary>
/// <param name="Wav">Fichier WAV complet, en-tête comprise.</param>
/// <param name="Duration">Durée réelle, déduite du nombre d'échantillons reçus.</param>
public readonly record struct RecordedAudio(byte[] Wav, TimeSpan Duration);

/// <summary>
/// Capture du micro par défaut de Windows, directement au format attendu par
/// le moteur de reconnaissance.
///
/// Coquille volontairement mince autour de NAudio : elle branche le
/// périphérique et accumule les échantillons. Les décisions — durée trop
/// courte, plafond atteint — appartiennent à <see cref="RecordingGuards"/>,
/// qui se teste sans micro.
/// </summary>
public sealed class AudioRecorder : IDisposable
{
    private readonly RecordingGuards _guards;
    private readonly Lock _sync = new();

    private WaveInEvent? _device;
    private MemoryStream? _samples;
    private bool _maximumReached;

    public AudioRecorder(RecordingGuards guards) => _guards = guards;

    /// <summary>
    /// Levé quand la capture s'est arrêtée seule, plafond atteint. Permet à
    /// l'interface de cesser d'afficher un enregistrement en cours.
    /// </summary>
    public event EventHandler? MaximumReached;

    public bool IsRecording
    {
        get
        {
            lock (_sync)
            {
                return _device is not null;
            }
        }
    }

    /// <summary>
    /// Ouvre le micro et commence à accumuler. Sans effet si un enregistrement
    /// est déjà en cours : la répétition automatique du clavier provoque des
    /// appels en rafale, qui ne doivent pas repartir de zéro à chaque touche.
    /// </summary>
    /// <exception cref="InvalidOperationException">Aucun micro disponible.</exception>
    public void Start()
    {
        lock (_sync)
        {
            if (_device is not null)
            {
                return;
            }

            if (WaveInEvent.DeviceCount == 0)
            {
                throw new InvalidOperationException(
                    "Aucun microphone détecté. Vérifiez qu'un périphérique d'entrée est "
                    + "branché et autorisé dans Paramètres > Confidentialité > Microphone.");
            }

            _maximumReached = false;
            _samples = new MemoryStream();

            var device = new WaveInEvent
            {
                WaveFormat = new WaveFormat(
                    RecordingFormat.SampleRate,
                    RecordingFormat.BitsPerSample,
                    RecordingFormat.Channels),

                // Des tampons courts rendent l'arrêt réactif : au relâchement
                // de la touche, on n'attend au pire que la fin du tampon
                // courant avant de pouvoir transcrire.
                BufferMilliseconds = 50,
            };

            device.DataAvailable += OnDataAvailable;
            device.StartRecording();

            _device = device;
        }
    }

    /// <summary>
    /// Referme le micro et rend l'enregistrement, ou <c>null</c> si l'appui
    /// a été trop bref pour contenir de la parole.
    /// </summary>
    public RecordedAudio? Stop()
    {
        MemoryStream? samples;

        lock (_sync)
        {
            if (_device is null)
            {
                return null;
            }

            _device.DataAvailable -= OnDataAvailable;
            _device.StopRecording();
            _device.Dispose();
            _device = null;

            samples = _samples;
            _samples = null;
        }

        if (samples is null)
        {
            return null;
        }

        using (samples)
        {
            // La durée est déduite du nombre d'échantillons réellement reçus,
            // et non de l'horloge : c'est ce que le moteur entendra.
            TimeSpan duration = RecordingFormat.DurationOf(samples.Length);

            if (_guards.IsTooShort(duration))
            {
                return null;
            }

            return new RecordedAudio(WavFile.Create(samples.GetBuffer().AsSpan(0, (int)samples.Length)), duration);
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        bool reachedMaximum = false;

        lock (_sync)
        {
            if (_samples is null)
            {
                return;
            }

            long room = _guards.MaximumBytes - _samples.Length;

            if (room <= 0)
            {
                return;
            }

            int toWrite = (int)Math.Min(e.BytesRecorded, room);
            _samples.Write(e.Buffer, 0, toWrite);

            if (_samples.Length >= _guards.MaximumBytes && !_maximumReached)
            {
                _maximumReached = true;
                reachedMaximum = true;
            }
        }

        // Hors du verrou : l'abonné peut vouloir appeler Stop().
        if (reachedMaximum)
        {
            MaximumReached?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_device is not null)
            {
                _device.DataAvailable -= OnDataAvailable;
                _device.StopRecording();
                _device.Dispose();
                _device = null;
            }

            _samples?.Dispose();
            _samples = null;
        }
    }
}
