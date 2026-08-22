using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using HexWin.Audio;
using HexWin.Configuration;
using HexWin.Diagnostics;
using HexWin.Input;
using HexWin.Output;
using HexWin.Transcription;

namespace HexWin.Tray;

/// <summary>
/// L'application elle-même : icône de barre système, raccourci global, et
/// enchaînement enregistrement → transcription → insertion.
///
/// <para><b>Répartition du travail entre les fils.</b> Le rappel du hook
/// clavier s'exécute sur le fil de la boucle de messages, qu'il ne faut jamais
/// bloquer — au-delà du délai imparti, Windows désinstalle le hook en
/// silence. Démarrer et arrêter le micro y est acceptable, c'est immédiat. La
/// transcription part en revanche sur un fil de fond, puis revient sur le fil
/// d'interface pour l'insertion : les API de presse-papiers exigent un fil STA
/// initialisé pour OLE, ce que seul le fil d'interface garantit.</para>
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Coquille Windows Forms : exige une session interactive et une boucle de messages.")]
internal sealed class TrayContext : ApplicationContext
{
    private readonly AppSettings _settings;
    private readonly SessionLog _log;
    private readonly string _modelPath;

    private readonly DictationCoordinator _coordinator = new();
    private readonly TrayIcons _icons = new();
    private readonly NotifyIcon _notifyIcon;
    private readonly SynchronizationContext _uiThread;

    private readonly AudioRecorder _recorder;
    private readonly KeyboardHook _hook;

    private readonly EngineHost _engines;
    private readonly System.Windows.Forms.Timer _hookWatchdog;

    /// <summary>Fenêtre où l'utilisateur parlait, à retrouver avant d'insérer.</summary>
    private TargetWindow? _target;

    public TrayContext(AppSettings settings, string modelPath)
    {
        _settings = settings;
        _log = SessionLog.Create(settings.LogEnabled);
        _modelPath = modelPath;

        _uiThread = SynchronizationContext.Current
            ?? throw new InvalidOperationException("TrayContext doit être créé sur le fil d'interface.");

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
        _coordinator.StateChanged += (_, state) => ApplyState(state);
        ApplyState(_coordinator.State);

        // Le minuteur Windows Forms tourne sur le fil d'interface, celui-là
        // même qui détient le hook : la réinstallation se fait donc là où
        // Windows l'exige.
        _hookWatchdog = new System.Windows.Forms.Timer { Interval = (int)WatchdogInterval.TotalMilliseconds };
        _hookWatchdog.Tick += (_, _) => WatchHook();

        _hook.Install();
        _hookWatchdog.Start();

        _ = LoadEngineAsync();
    }

    /// <summary>
    /// Un clavier muet plus longtemps que ce délai déclenche une
    /// réinstallation du hook. Assez long pour que ce soit rare, assez court
    /// pour qu'une panne ne dure pas toute la journée.
    /// </summary>
    private static readonly TimeSpan WatchdogInterval = TimeSpan.FromMinutes(2);

    private void WatchHook()
    {
        if (_hook.RefreshIfSilent(WatchdogInterval))
        {
            _log.Write("hook clavier réinstallé après un silence prolongé");
        }
    }

    // --- Cycle de vie du moteur -------------------------------------------------

    /// <summary>
    /// Prépare le moteur au démarrage.
    ///
    /// <para><b>Le modèle n'est chargé que si l'utilisateur a demandé qu'il
    /// reste résident</b> (<c>unloadAfterMinutes = 0</c>). Sinon, le charger
    /// ici reviendrait à occuper un gigaoctet dès l'ouverture de session pour
    /// le rendre quelques minutes plus tard, sans qu'une seule dictée n'ait eu
    /// lieu — exactement ce que la libération après inactivité cherchait à
    /// éviter. Le chargement est alors différé à la première dictée, où il se
    /// déroule pendant que l'utilisateur parle.</para>
    ///
    /// <para>Le modèle est en revanche <i>vérifié</i> dans tous les cas :
    /// découvrir qu'il manque au moment où l'utilisateur parle serait le pire
    /// moment, sa phrase étant alors déjà perdue.</para>
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
                _log.Write($"modèle vérifié, chargement différé à la première dictée");
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

    // --- Enchaînement d'une dictée ----------------------------------------------

    private void OnDictationStarted()
    {
        if (!_coordinator.TryStartRecording())
        {
            return;
        }

        // Le rechargement est lancé ici, à l'enfoncement, et non au
        // relâchement : il se déroule pendant que l'utilisateur parle. Sur une
        // phrase de deux secondes, les trois secondes de chargement sont
        // presque entièrement masquées.
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

        // La fenêtre est mémorisée MAINTENANT, tant qu'elle est encore celle
        // où l'utilisateur parlait. Après un rechargement du modèle, deux
        // secondes peuvent s'écouler avant l'insertion — largement le temps
        // de basculer ailleurs, et d'y déverser un texte non désiré.
        _target = TargetWindow.Capture();

        RecordedAudio? recorded = _recorder.Stop();

        if (recorded is not { } audio)
        {
            // Appui trop bref : l'utilisateur a effleuré la touche.
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
            // Rend la main immédiatement si le modèle est déjà là, sinon
            // attend la fin du chargement commencé à l'enfoncement.
            ParakeetEngine engine = await _engines.GetAsync().ConfigureAwait(true);

            using var wav = new MemoryStream(audio.Wav);
            TranscriptionResult result = await engine.TranscribeAsync(wav).ConfigureAwait(true);

            // Le niveau capté est journalisé avec chaque dictée, et pas
            // seulement dans le mode diagnostic. Sans lui, « zéro caractère »
            // est indiagnosticable : impossible de distinguer un micro qui
            // n'entend rien d'un moteur qui ne reconnaît rien. Deux pannes
            // très différentes, au même symptôme.
            double peak = AudioLevel.Peak(audio.Wav.AsSpan(WavFile.HeaderSize));

            _log.Write(
                $"{audio.Duration.TotalSeconds:F1} s dictées, niveau {peak:P1}, "
                + $"transcrites en {result.Duration.TotalSeconds:F2} s, "
                + $"{result.Text.Length} caractères");

            if (result.Text.Length == 0)
            {
                _log.Write(AudioLevel.IsSilent(audio.Wav.AsSpan(WavFile.HeaderSize))
                    ? "  → rien inséré : le micro n'a capté aucun son"
                    : "  → rien inséré : du son a été capté mais aucune parole reconnue");
            }

            if (result.Text.Length > 0)
            {
                // Ramène la fenêtre où l'utilisateur parlait, si elle n'est
                // plus au premier plan. Sans effet si elle a disparu, ou si
                // Windows refuse le changement : on insère quand même, dans
                // la fenêtre courante, plutôt que de perdre la dictée.
                _target?.Restore();

                // De retour sur le fil d'interface grâce à ConfigureAwait(true) :
                // le presse-papiers exige un fil STA initialisé pour OLE.
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

            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _icons.Dispose();
        }

        base.Dispose(disposing);
    }
}
