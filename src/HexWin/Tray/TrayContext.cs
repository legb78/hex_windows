using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using HexWin.Audio;
using HexWin.Configuration;
using HexWin.Diagnostics;
using HexWin.Feedback;
using HexWin.Input;
using HexWin.Output;
using HexWin.Transcription;

namespace HexWin.Tray;

/// <summary>
/// The application itself: tray icon, global shortcut, and the chain of
/// recording, transcription and insertion.
///
/// <para><b>How the work is split across threads.</b> The keyboard hook
/// callback runs on the message-loop thread, which must never be blocked —
/// past the allotted deadline, Windows uninstalls the hook in silence.
/// Starting and stopping the microphone there is acceptable, being immediate.
/// Transcription, by contrast, goes to a background thread, then comes back to
/// the interface thread for the insertion: the clipboard APIs require an STA
/// thread initialised for OLE, which only the interface thread
/// guarantees.</para>
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Windows Forms shell: requires an interactive session and a message loop.")]
internal sealed class TrayContext : ApplicationContext
{
    private readonly AppSettings _settings;
    private readonly SessionLog _log;
    private readonly string _modelPath;

    private readonly DictationCoordinator _coordinator = new();
    private readonly TrayIcons _icons = new();
    private readonly NotifyIcon _notifyIcon;
    private readonly DictationFeedback _feedback;
    private readonly SynchronizationContext _uiThread;

    private readonly AudioRecorder _recorder;
    private readonly KeyboardHook _hook;

    private readonly EngineHost _engines;
    private readonly System.Windows.Forms.Timer _hookWatchdog;

    /// <summary>Window the user was speaking into, to find again before inserting.</summary>
    private TargetWindow? _target;

    public TrayContext(AppSettings settings, string modelPath)
    {
        _settings = settings;
        _log = SessionLog.Create(settings.LogEnabled);
        _modelPath = modelPath;

        _uiThread = SynchronizationContext.Current
            ?? throw new InvalidOperationException("TrayContext must be created on the interface thread.");

        _engines = new EngineHost(
            modelPath,
            settings.Provider,
            settings.Threads,
            IdlePolicy.FromMinutes(settings.UnloadAfterMinutes),
            _log);

        _recorder = new AudioRecorder(RecordingGuards.From(settings));
        _recorder.MaximumReached += (_, _) => _uiThread.Post(_ => OnDictationEnded(), null);

        _hook = new KeyboardHook(new ChordDetector(settings.Hotkey));
        _hook.Started += (_, _) => OnDictationStarted();
        _hook.Stopped += (_, _) => OnDictationEnded();
        _hook.Cancelled += (_, _) => OnDictationCancelled();

        _notifyIcon = BuildNotifyIcon();
        _feedback = new DictationFeedback(settings.Feedback, _log);

        _coordinator.StateChanged += (_, state) => ApplyState(state);
        ApplyState(_coordinator.State);

        // The Windows Forms timer runs on the interface thread, the very one
        // that holds the hook: the reinstall therefore happens where Windows
        // requires it.
        _hookWatchdog = new System.Windows.Forms.Timer { Interval = (int)WatchdogInterval.TotalMilliseconds };
        _hookWatchdog.Tick += (_, _) => WatchHook();

        _hook.Install();
        _hookWatchdog.Start();

        _ = LoadEngineAsync();
    }

    /// <summary>
    /// A keyboard silent for longer than this triggers a reinstall of the
    /// hook. Long enough to be rare, short enough that a failure does not last
    /// all day.
    /// </summary>
    private static readonly TimeSpan WatchdogInterval = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Opening of a recording left out of the level measurement. Long enough
    /// to cover the start cue, which the microphone picks up off the speakers,
    /// and shorter than the minimum recording length, so no speech can fall
    /// entirely inside it.
    /// </summary>
    private static readonly TimeSpan CueLead = TimeSpan.FromMilliseconds(250);

    private void WatchHook()
    {
        if (_hook.RefreshIfSilent(WatchdogInterval))
        {
            _log.Write("hook clavier réinstallé après un silence prolongé");
        }
    }

    // --- Engine lifecycle -------------------------------------------------------

    /// <summary>
    /// Prepares the engine at startup.
    ///
    /// <para><b>The model is only loaded if the user asked for it to stay
    /// resident</b> (<c>unloadAfterMinutes = 0</c>). Otherwise, loading it here
    /// would mean taking up a gigabyte from sign-in only to hand it back a few
    /// minutes later, without a single dictation having happened — exactly what
    /// releasing after inactivity was meant to avoid. Loading is then deferred
    /// to the first dictation, where it runs while the user is speaking.</para>
    ///
    /// <para>The model is nonetheless <i>checked</i> in every case: discovering
    /// it is missing while the user is speaking would be the worst possible
    /// moment, their sentence being already lost by then.</para>
    /// </summary>
    private async Task LoadEngineAsync()
    {
        try
        {
            if (_settings.UnloadAfterMinutes == 0)
            {
                await _engines.GetAsync().ConfigureAwait(true);
            }
            else
            {
                await Task.Run(() => ParakeetEngine.Validate(_modelPath)).ConfigureAwait(true);
                _log.Write("modèle vérifié, chargement différé à la première dictée");
            }

            _log.Write($"prêt ({_settings.Provider}, {_settings.Threads} fils)");
            _coordinator.MarkReady();
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidOperationException or DllNotFoundException)
        {
            _log.Write($"échec du chargement : {ex.Message}");
            _coordinator.MarkFailed();

            ShowBalloon("Modèle introuvable", $"{ex.Message}\n\nLancez scripts/get-model.ps1.");
        }
    }

    // --- One dictation, end to end ----------------------------------------------

    private void OnDictationStarted()
    {
        if (!_coordinator.TryStartRecording())
        {
            return;
        }

        // The reload starts here, on the key press, not on the release: it runs
        // while the user is speaking. On a two-second sentence, the three
        // seconds of loading are almost entirely hidden.
        _engines.SetBusy(true);
        _engines.BeginLoad();

        try
        {
            _recorder.Start();
        }
        catch (InvalidOperationException ex)
        {
            _log.Write($"micro indisponible : {ex.Message}");
            _coordinator.Cancel();
            _engines.SetBusy(false);
            ShowBalloon("Micro indisponible", ex.Message);
        }
    }

    private void OnDictationEnded()
    {
        if (!_coordinator.TryStartTranscribing())
        {
            return;
        }

        // The window is remembered NOW, while it is still the one the user was
        // speaking into. After a model reload, two seconds can pass before the
        // insertion — ample time to switch elsewhere, and to dump unwanted text
        // there.
        _target = TargetWindow.Capture();

        RecordedAudio? recorded = _recorder.Stop();

        if (recorded is not { } audio)
        {
            // Press too brief: the user brushed the key.
            _coordinator.Complete();
            _engines.SetBusy(false);
            return;
        }

        _ = TranscribeAsync(audio);
    }

    private void OnDictationCancelled()
    {
        if (_coordinator.Cancel())
        {
            _recorder.Stop();
            _engines.SetBusy(false);
            _log.Write("dictée annulée");
        }
    }

    private async Task TranscribeAsync(RecordedAudio audio)
    {
        try
        {
            // Returns immediately if the model is already there, otherwise
            // waits for the load that started on the key press.
            ParakeetEngine engine = await _engines.GetAsync().ConfigureAwait(true);

            using var wav = new MemoryStream(audio.Wav);
            TranscriptionResult result = await engine.TranscribeAsync(wav).ConfigureAwait(true);

            // The captured level is logged with every dictation, not only in
            // the diagnostic mode. Without it, "zero characters" cannot be
            // diagnosed: there is no telling a microphone that hears nothing
            // from an engine that recognises nothing. Two very different
            // faults, with the same symptom.
            double peak = AudioLevel.Peak(audio.Wav.AsSpan(WavFile.HeaderSize), CueLead);

            _log.Write(
                $"{audio.Duration.TotalSeconds:F1} s dictées, niveau {peak:P1}, "
                + $"transcrites en {result.Duration.TotalSeconds:F2} s, "
                + $"{result.Text.Length} caractères");

            if (result.Text.Length == 0)
            {
                _log.Write(peak < AudioLevel.SilenceThreshold
                    ? "  → rien inséré : le micro n'a capté aucun son"
                    : "  → rien inséré : du son a été capté mais aucune parole reconnue");
            }

            if (result.Text.Length > 0)
            {
                // Brings back the window the user was speaking into, if it is
                // no longer in the foreground. Does nothing if it has vanished,
                // or if Windows refuses the change: we insert anyway, into the
                // current window, rather than lose the dictation.
                _target?.Restore();

                // Back on the interface thread thanks to ConfigureAwait(true):
                // the clipboard requires an STA thread initialised for OLE.
                TextInjector.Insert(result.Text, _settings.Insertion);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            _log.Write($"échec de la transcription : {ex.Message}");
        }
        finally
        {
            _coordinator.Complete();
            _engines.SetBusy(false);
        }
    }

    // --- Interface ---------------------------------------------------------------

    private NotifyIcon BuildNotifyIcon()
    {
        var menu = new ContextMenuStrip();

        menu.Items.Add("Ouvrir settings.json", null, (_, _) => OpenSettings());
        menu.Items.Add("Ouvrir le dossier des journaux", null, (_, _) => OpenLogFolder());
        menu.Items.Add(new ToolStripSeparator());

        var autoStart = new ToolStripMenuItem("Lancer au démarrage de Windows")
        {
            Checked = AutoStart.IsEnabled,
            CheckOnClick = true,
        };
        autoStart.CheckedChanged += (sender, _) =>
            AutoStart.SetEnabled(((ToolStripMenuItem)sender!).Checked);
        menu.Items.Add(autoStart);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quitter", null, (_, _) => Quit());

        return new NotifyIcon
        {
            ContextMenuStrip = menu,
            Visible = true,
            Icon = _icons[DictationState.Loading],
        };
    }

    private void ApplyState(DictationState state)
    {
        _notifyIcon.Icon = _icons[state];
        _notifyIcon.Text = Describe(state);
        _feedback.Apply(state);
    }

    private string Describe(DictationState state) => state switch
    {
        DictationState.Loading => "HexWin — chargement du modèle...",
        DictationState.Idle => $"HexWin — prêt ({string.Join(" + ", _settings.Hotkey)})",
        DictationState.Recording => "HexWin — enregistrement",
        DictationState.Transcribing => "HexWin — transcription...",
        DictationState.Failed => "HexWin — modèle introuvable",
        _ => "HexWin",
    };

    private void ShowBalloon(string title, string message)
    {
        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.ShowBalloonTip(10_000);
    }

    private void OpenSettings() =>
        OpenInShell(Path.Combine(AppContext.BaseDirectory, AppSettings.FileName));

    private void OpenLogFolder() => OpenInShell(SessionLog.Directory);

    private void OpenInShell(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        {
            ShowBalloon("Ouverture impossible", path);
        }
    }

    private void Quit()
    {
        _notifyIcon.Visible = false;
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hookWatchdog.Dispose();
            _hook.Dispose();
            _recorder.Dispose();
            _engines.Dispose();
            _feedback.Dispose();

            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _icons.Dispose();
        }

        base.Dispose(disposing);
    }
}
