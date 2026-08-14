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
    private readonly string _modelPath;
    private readonly SessionLog _log;

    private readonly DictationCoordinator _coordinator = new();
    private readonly TrayIcons _icons = new();
    private readonly NotifyIcon _notifyIcon;
    private readonly SynchronizationContext _uiThread;

    private readonly AudioRecorder _recorder;
    private readonly KeyboardHook _hook;

    private ParakeetEngine? _engine;

    public TrayContext(AppSettings settings, string modelPath)
    {
        _settings = settings;
        _modelPath = modelPath;
        _log = SessionLog.Create(settings.LogEnabled);

        _uiThread = SynchronizationContext.Current
            ?? throw new InvalidOperationException("TrayContext doit être créé sur le fil d'interface.");

        _recorder = new AudioRecorder(RecordingGuards.From(settings));
        _recorder.MaximumReached += (_, _) => _uiThread.Post(_ => OnDictationEnded(), null);

        _hook = new KeyboardHook(new ChordDetector(settings.Hotkey));
        _hook.Started += (_, _) => OnDictationStarted();
        _hook.Stopped += (_, _) => OnDictationEnded();
        _hook.Cancelled += (_, _) => OnDictationCancelled();

        _notifyIcon = BuildNotifyIcon();
        _coordinator.StateChanged += (_, state) => ApplyState(state);
        ApplyState(_coordinator.State);

        _hook.Install();
        _ = LoadEngineAsync();
    }

    // --- Cycle de vie du moteur -------------------------------------------------

    private async Task LoadEngineAsync()
    {
        try
        {
            ParakeetEngine engine = await Task.Run(
                () => ParakeetEngine.Load(_modelPath, _settings.Provider, _settings.Threads))
                .ConfigureAwait(true);

            // Absorbe le coût de la première inférence, sinon payé par la
            // première dictée de l'utilisateur.
            await engine.WarmUpAsync().ConfigureAwait(true);

            _engine = engine;
            _log.Write($"moteur chargé ({_settings.Provider}, {_settings.Threads} fils)");
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

        try
        {
            _recorder.Start();
        }
        catch (InvalidOperationException ex)
        {
            _log.Write($"micro indisponible : {ex.Message}");
            _coordinator.Cancel();
            ShowBalloon("Micro indisponible", ex.Message);
        }
    }

    private void OnDictationEnded()
    {
        if (!_coordinator.TryStartTranscribing())
        {
            return;
        }

        RecordedAudio? recorded = _recorder.Stop();

        if (recorded is not { } audio)
        {
            // Appui trop bref : l'utilisateur a effleuré la touche.
            _coordinator.Complete();
            return;
        }

        _ = TranscribeAsync(audio);
    }

    private void OnDictationCancelled()
    {
        if (_coordinator.Cancel())
        {
            _recorder.Stop();
            _log.Write("dictée annulée");
        }
    }

    private async Task TranscribeAsync(RecordedAudio audio)
    {
        if (_engine is not { } engine)
        {
            _coordinator.Complete();
            return;
        }

        try
        {
            using var wav = new MemoryStream(audio.Wav);
            TranscriptionResult result = await engine.TranscribeAsync(wav).ConfigureAwait(true);

            _log.Write(
                $"{audio.Duration.TotalSeconds:F1} s dictées, transcrites en "
                + $"{result.Duration.TotalSeconds:F2} s, {result.Text.Length} caractères");

            if (result.Text.Length > 0)
            {
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
            _hook.Dispose();
            _recorder.Dispose();
            _engine?.Dispose();

            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _icons.Dispose();
        }

        base.Dispose(disposing);
    }
}
