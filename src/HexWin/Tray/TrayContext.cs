using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using HexWin.Audio;
using HexWin.Configuration;
using HexWin.Diagnostics;
using HexWin.Feedback;
using HexWin.Input;
using HexWin.Output;
using HexWin.Transcription;
using HexWin.Ui;

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
    private AppSettings _settings;
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

    private SettingsWindow? _settingsWindow;

    // The menu entries a saved configuration has to bring back in line.
    private ToolStripMenuItem _showCircleItem = null!;
    private ToolStripMenuItem _playToneItem = null!;
    private ToolStripMenuItem _autoStartItem = null!;

    /// <summary>
    /// True while the menu is being brought in line with a saved configuration,
    /// so that its switches do not write back to the file what was just written.
    /// </summary>
    private bool _syncingMenu;

    /// <summary>
    /// Settings saved during a dictation, waiting for it to end before they
    /// reach the shortcut and the circle.
    /// </summary>
    private bool _pendingLiveApply;

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

        // The feedback comes first: the menu reads the state of its two
        // switches while it is being built.
        _feedback = new DictationFeedback(settings, _log);
        _notifyIcon = BuildNotifyIcon();

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
    /// Opening of a recording left out of the level measurement, long enough to
    /// cover the start cue that the microphone picks up off the speakers. Applied
    /// only when the tone is on, and capped by AudioLevel at half the recording,
    /// so a brief press keeps a level worth reading.
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
        // Checked before opening anything, and safe to check separately from the
        // transition below: everything here runs on the message-loop thread, so
        // no second dictation can slip in between the two.
        if (_coordinator.State != DictationState.Idle)
        {
            return;
        }

        try
        {
            // The microphone opens before the state changes, and so before the
            // cue. Opening a capture stream makes Windows reconfigure its audio
            // engine, which silences playback for about 200 ms: a tone started
            // first is cut clean in half by that silence and heard as two beeps.
            // Measured on the speaker output, cue first against microphone first:
            // "50 ms, 200 ms of silence, 25 ms" every time against a whole 70 ms
            // every time.
            _recorder.Start();
        }
        catch (InvalidOperationException ex)
        {
            _log.Write($"micro indisponible : {ex.Message}");
            ShowBalloon("Micro indisponible", ex.Message);
            return;
        }

        if (!_coordinator.TryStartRecording())
        {
            _recorder.Stop();
            return;
        }

        // The reload starts here, on the key press, not on the release: it runs
        // while the user is speaking. On a two-second sentence, the three
        // seconds of loading are almost entirely hidden.
        _engines.SetBusy(true);
        _engines.BeginLoad();
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
            // Only worth skipping when there is a cue to skip: with the tone
            // off, the opening of the recording is as trustworthy as the rest.
            double peak = AudioLevel.Peak(
                audio.Wav.AsSpan(WavFile.HeaderSize),
                _feedback.PlaysTone ? CueLead : TimeSpan.Zero);

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

        var preferences = new ToolStripMenuItem("Paramètres…", null, (_, _) => ShowSettingsWindow())
        {
            Font = new Font(menu.Font, FontStyle.Bold),
        };
        menu.Items.Add(preferences);
        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add("Ouvrir settings.json", null, (_, _) => OpenSettings());
        menu.Items.Add("Ouvrir le dossier des journaux", null, (_, _) => OpenLogFolder());
        menu.Items.Add(new ToolStripSeparator());

        var showCircle = new ToolStripMenuItem("Afficher le cercle pendant la dictée")
        {
            Checked = _feedback.ShowsCircle,
            CheckOnClick = true,
        };
        showCircle.CheckedChanged += (sender, _) =>
        {
            if (_syncingMenu)
            {
                return;
            }

            _feedback.SetShowsCircle(((ToolStripMenuItem)sender!).Checked);
            PersistFeedbackMode();
        };
        menu.Items.Add(showCircle);
        _showCircleItem = showCircle;

        var playTone = new ToolStripMenuItem("Jouer un son au début et à la fin")
        {
            Checked = _feedback.PlaysTone,
            CheckOnClick = true,
        };
        playTone.CheckedChanged += (sender, _) =>
        {
            if (_syncingMenu)
            {
                return;
            }

            _feedback.SetPlaysTone(((ToolStripMenuItem)sender!).Checked);
            PersistFeedbackMode();
        };
        menu.Items.Add(playTone);
        _playToneItem = playTone;

        menu.Items.Add(new ToolStripSeparator());

        var autoStart = new ToolStripMenuItem("Lancer au démarrage de Windows")
        {
            Checked = AutoStart.IsEnabled,
            CheckOnClick = true,
        };
        autoStart.CheckedChanged += (sender, _) =>
        {
            if (!_syncingMenu)
            {
                AutoStart.SetEnabled(((ToolStripMenuItem)sender!).Checked);
            }
        };
        menu.Items.Add(autoStart);
        _autoStartItem = autoStart;

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quitter", null, (_, _) => Quit());

        var notifyIcon = new NotifyIcon
        {
            ContextMenuStrip = menu,
            Visible = true,
            Icon = _icons[DictationState.Loading],
        };

        // The entry in bold is the one a double-click opens, as Windows does
        // for the default item of any tray menu.
        notifyIcon.DoubleClick += (_, _) => ShowSettingsWindow();

        return notifyIcon;
    }

    // --- Settings window ---------------------------------------------------------

    /// <summary>
    /// One window at a time: asking again brings the open one to the front
    /// rather than opening a second copy that would save over the first.
    /// </summary>
    private void ShowSettingsWindow()
    {
        if (_settingsWindow is { IsDisposed: false })
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(
            _settings,
            AutoStart.IsEnabled,
            _hook,
            SaveSettings,
            OpenSettings,
            OpenLogFolder,
            _icons[DictationState.Idle]);

        _settingsWindow.FormClosed += (_, _) =>
        {
            _settingsWindow?.Dispose();
            _settingsWindow = null;
        };

        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    /// <summary>
    /// Takes in what the settings window saved: into settings.json, then into
    /// the running application for whatever can change on the fly.
    ///
    /// <para>The file is written first and the application updated second, but
    /// a write that fails does not hold the rest back: the user asked for the
    /// change, and it holds until the next start either way. The window is told
    /// which keys did not make it to the file, and which wait for a
    /// restart.</para>
    /// </summary>
    private SaveOutcome SaveSettings(SettingsSubmission submission)
    {
        AppSettings next = submission.Settings;
        IReadOnlyList<SettingChange> changes = SettingsDiff.Between(_settings, next);

        IReadOnlyList<string> notPersisted = changes.Count == 0 ? [] : Persist(next, changes);

        if (submission.AutoStart != AutoStart.IsEnabled)
        {
            AutoStart.SetEnabled(submission.AutoStart);
        }

        _settings = next;

        if (_coordinator.State is DictationState.Recording or DictationState.Transcribing)
        {
            _pendingLiveApply = true;
        }
        else
        {
            ApplyLive();
        }

        if (changes.Count > 0)
        {
            _log.Write($"réglages enregistrés : {string.Join(", ", changes.Select(change => change.Key))}");
        }

        if (notPersisted.Count > 0)
        {
            _log.Write($"settings.json n'a pas pu être mis à jour pour : {string.Join(", ", notPersisted)}");
        }

        return new SaveOutcome(
            [.. changes.Where(change => change.RequiresRestart).Select(change => change.Key)],
            notPersisted);
    }

    /// <summary>
    /// Writes the changed keys, and only those, so the comments of the file
    /// survive. A file that does not exist is created whole: there are no
    /// comments to lose, and a partial file would read as missing settings.
    /// </summary>
    private static IReadOnlyList<string> Persist(AppSettings next, IReadOnlyList<SettingChange> changes)
    {
        string path = SettingsPath;

        if (!File.Exists(path))
        {
            try
            {
                File.WriteAllText(path, next.ToJson());
                return [];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return [.. changes.Select(change => change.Key)];
            }
        }

        return AppSettings.RewriteValues(
            path,
            [.. changes.Select(change => KeyValuePair.Create(change.Key, change.JsonValue))],
            appendMissing: true);
    }

    /// <summary>
    /// Puts the parts that can change on the fly in line with the current
    /// settings: the shortcut, the circle and the tones, the menu, the
    /// tooltip. The insertion mode needs nothing — it is read at every
    /// insertion.
    ///
    /// <para>Only ever run between two dictations. Swapping the shortcut while
    /// its keys are held would break the swallowing balance the detector
    /// keeps, and rebuilding the circle while it is on screen would make it
    /// flash.</para>
    /// </summary>
    private void ApplyLive()
    {
        _pendingLiveApply = false;

        _hook.ReplaceDetector(new ChordDetector(_settings.Hotkey));
        _feedback.Reconfigure(_settings);

        _syncingMenu = true;

        try
        {
            _showCircleItem.Checked = _feedback.ShowsCircle;
            _playToneItem.Checked = _feedback.PlaysTone;
            _autoStartItem.Checked = AutoStart.IsEnabled;
        }
        finally
        {
            _syncingMenu = false;
        }

        _notifyIcon.Text = Describe(_coordinator.State);
    }

    private void ApplyState(DictationState state)
    {
        _notifyIcon.Icon = _icons[state];
        _notifyIcon.Text = Describe(state);
        _feedback.Apply(state);

        if (_pendingLiveApply && state == DictationState.Idle)
        {
            ApplyLive();
        }
    }

    private string Describe(DictationState state) => state switch
    {
        DictationState.Loading => "HexWin — chargement du modèle...",
        DictationState.Idle => $"HexWin — prêt ({HotkeyText.Describe(_settings.Hotkey)})",
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

    /// <summary>
    /// Writes the two switches back to settings.json, so the choice survives a
    /// restart.
    ///
    /// <para>The change is already in effect when this runs: the menu acts on
    /// the live feedback first, and only then records it. A file that cannot be
    /// written — open in an editor, read-only folder — therefore costs the user
    /// nothing today, only the memory of the choice tomorrow. That is worth a
    /// line in the log, not an interruption.</para>
    /// </summary>
    private void PersistFeedbackMode()
    {
        _settings.Feedback = _feedback.Mode;

        bool written = AppSettings.TryRewriteValue(SettingsPath, "feedback", $"\"{_feedback.Mode}\"");

        if (!written)
        {
            _log.Write($"retour visuel et sonore réglé sur {_feedback.Mode}, mais settings.json n'a pas pu être mis à jour");
        }
    }

    private static string SettingsPath => Path.Combine(AppContext.BaseDirectory, AppSettings.FileName);

    private void OpenSettings() => OpenInShell(SettingsPath);

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
            _settingsWindow?.Dispose();
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
