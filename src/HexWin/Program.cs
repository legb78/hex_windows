using System.Diagnostics.CodeAnalysis;
using HexWin.Audio;
using HexWin.Configuration;
using HexWin.Diagnostics;
using HexWin.Input;
using HexWin.Interop;
using HexWin.Output;
using HexWin.Transcription;
using HexWin.Tray;

namespace HexWin;

[ExcludeFromCodeCoverage(Justification = "Entry point: routes to the modes, with no logic of its own.")]
internal static class Program
{
    /// <summary>
    /// Entry point. STAThread is required by Windows Forms and by the
    /// clipboard APIs used to insert the text.
    /// </summary>
    [STAThread]
    private static int Main(string[] args)
    {
        if (HasFlag(args, "--help", "-h"))
        {
            ConsoleBridge.Attach();
            PrintUsage();
            return 0;
        }

        string? wavPath = ReadOption(args, "--transcribe");

        if (wavPath is not null)
        {
            ConsoleBridge.Attach();
            return TranscribeFile(wavPath, ReadOption(args, "--model"), ReadOption(args, "--provider"));
        }

        string? recordTarget = ReadOption(args, "--record");

        if (recordTarget is not null)
        {
            ConsoleBridge.Attach();
            return RecordToFile(recordTarget, ReadOption(args, "--seconds"));
        }

        if (HasFlag(args, "--watch-hotkey"))
        {
            ConsoleBridge.Attach();
            return WatchHotkey();
        }

        string? textToInject = ReadOption(args, "--inject");

        if (textToInject is not null)
        {
            ConsoleBridge.Attach();
            return InjectText(textToInject, ReadOption(args, "--mode"), ReadOption(args, "--delay"));
        }

        return RunTrayApplication();
    }

    /// <summary>
    /// Normal mode: the application lives in the system tray and has no
    /// window.
    /// </summary>
    private static int RunTrayApplication()
    {
        // Two instances would install two keyboard hooks on the same shortcut,
        // and every dictation would be inserted twice.
        using var singleInstance = new Mutex(initiallyOwned: true, @"Local\HexWin", out bool isFirst);

        if (!isFirst)
        {
            return 0;
        }

        string baseDirectory = AppContext.BaseDirectory;
        AppSettings settings = AppSettings.Load(Path.Combine(baseDirectory, AppSettings.FileName));

        string? modelPath = ModelLocator.Resolve(settings.ModelPath, baseDirectory);

        if (modelPath is null)
        {
            MessageBox.Show(
                $"Modèle introuvable : {settings.ModelPath}\n\n"
                + "Lancez scripts/get-model.ps1 pour le télécharger.",
                "HexWin",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

            return 2;
        }

        ApplicationConfiguration.Initialize();

        // The Windows Forms synchronisation context is only installed when the
        // message loop starts, so too late for the TrayContext constructor —
        // which needs it to bring the transcription back to the interface
        // thread. So we install it by hand.
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());

        try
        {
            using var context = new TrayContext(settings, modelPath);
            Application.Run(context);
            return 0;
        }
        catch (Exception ex)
        {
            // A windowless application that vanishes in silence cannot be
            // diagnosed. The trace written here is sometimes the only usable
            // clue.
            ReportFatal(ex);
            return 1;
        }
    }

    private static void ReportFatal(Exception error)
    {
        string path = Path.Combine(SessionLog.Directory, "crash.log");

        try
        {
            Directory.CreateDirectory(SessionLog.Directory);
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}{error}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            path = "(journal inaccessible)";
        }

        MessageBox.Show(
            $"HexWin s'est arrêté sur une erreur :\n\n{error.Message}\n\nDétails dans {path}",
            "HexWin",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    /// <summary>
    /// Insertion diagnostic mode: waits a few seconds, long enough to place
    /// the cursor in the target application, then inserts text into it.
    ///
    /// The delay is indispensable: without it the text would land in the
    /// console that launched the command, which would prove nothing.
    /// </summary>
    private static int InjectText(string text, string? modeOption, string? delayOption)
    {
        InsertionMode mode = string.Equals(modeOption, "Type", StringComparison.OrdinalIgnoreCase)
            ? InsertionMode.Type
            : InsertionMode.Paste;

        if (!int.TryParse(delayOption, out int delay) || delay <= 0)
        {
            delay = 4;
        }

        Console.WriteLine($"Mode   : {mode}");
        Console.WriteLine($"Texte  : {text}");
        Console.WriteLine();
        Console.WriteLine($"Placez le curseur dans la fenêtre visée. Insertion dans {delay} s...");

        TargetWindow? target = null;

        for (int remaining = delay; remaining > 0; remaining--)
        {
            Console.Write($" {remaining}");
            Thread.Sleep(TimeSpan.FromSeconds(1));

            // The window is remembered after the first second, giving time for
            // the cursor to be placed. Mirrors what the application does:
            // capture the target early and return to it just before inserting,
            // so a window change in between cannot divert the text.
            if (target is null && (target = TargetWindow.Capture()) is not null)
            {
                Console.WriteLine();
                Console.WriteLine($"Cible memorisee : {target.Title}");
            }
        }

        Console.WriteLine();

        if (target?.Restore() == false)
        {
            Console.WriteLine("(fenêtre d'origine introuvable, insertion dans la fenêtre courante)");
        }

        TextInjector.Insert(text, mode);

        // Gives the paste time to land and the clipboard time to be restored
        // before the process ends.
        Thread.Sleep(TimeSpan.FromSeconds(1));

        Console.WriteLine("Insertion demandée.");
        return 0;
    }

    /// <summary>
    /// Shortcut diagnostic mode: installs the hook and reports every trigger,
    /// without recording or transcribing.
    ///
    /// This is the only way to check that layer: Windows marks every keystroke
    /// injected by a program, and the hook deliberately ignores those so as not
    /// to react to what it produces itself. A real finger press is therefore
    /// required.
    /// </summary>
    private static int WatchHotkey()
    {
        AppSettings settings = AppSettings.Load(
            Path.Combine(AppContext.BaseDirectory, AppSettings.FileName));

        Console.WriteLine($"Raccourci : {string.Join(" + ", settings.Hotkey)}");
        Console.WriteLine();
        Console.WriteLine("Maintenez-le quelques secondes, puis relâchez.");
        Console.WriteLine("Vérifiez surtout que le menu Démarrer ne s'ouvre PAS.");
        Console.WriteLine("Ctrl+C pour quitter.");
        Console.WriteLine();

        using var hook = new KeyboardHook(new ChordDetector(settings.Hotkey));

        hook.Started += (_, _) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}]  début");
        hook.Stopped += (_, _) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}]  fin");
        hook.Cancelled += (_, _) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}]  annulé");

        try
        {
            hook.Install();
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 5;
        }

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Application.ExitThread();
        };

        // A low-level hook is only fed by a message loop: without one, the
        // callback would never be called.
        Application.Run();

        return 0;
    }

    /// <summary>
    /// Microphone diagnostic mode: records for a few seconds, writes the WAV
    /// and measures the amplitude obtained.
    ///
    /// The measurement is the point. A muted, unplugged or privacy-blocked
    /// microphone produces a perfectly valid file, of the right duration, and
    /// entirely silent — which the engine then transcribes as an invented
    /// sentence. With no level shown, the fault would be hunted on the
    /// transcription side.
    /// </summary>
    private static int RecordToFile(string outputPath, string? secondsOption)
    {
        if (!int.TryParse(secondsOption, out int seconds) || seconds <= 0)
        {
            seconds = 5;
        }

        AppSettings settings = AppSettings.Load(
            Path.Combine(AppContext.BaseDirectory, AppSettings.FileName));

        try
        {
            using var recorder = new AudioRecorder(RecordingGuards.From(settings));

            Console.WriteLine($"Enregistrement pendant {seconds} s — parlez maintenant.");
            recorder.Start();
            Thread.Sleep(TimeSpan.FromSeconds(seconds));

            RecordedAudio? recorded = recorder.Stop();

            if (recorded is not { } audio)
            {
                Console.Error.WriteLine("Enregistrement trop court, rien n'a été retenu.");
                return 1;
            }

            File.WriteAllBytes(outputPath, audio.Wav);

            ReadOnlySpan<byte> pcm = audio.Wav.AsSpan(WavFile.HeaderSize);
            double peak = AudioLevel.Peak(pcm);

            Console.WriteLine($"Écrit     : {outputPath}");
            Console.WriteLine($"Durée     : {audio.Duration.TotalSeconds:F2} s");
            Console.WriteLine($"Niveau    : {peak:P1}");

            if (AudioLevel.IsSilent(pcm))
            {
                Console.WriteLine();
                Console.Error.WriteLine("Le signal est silencieux. Vérifiez que le bon micro est");
                Console.Error.WriteLine("sélectionné par défaut, et que Paramètres > Confidentialité");
                Console.Error.WriteLine("> Microphone autorise les applications de bureau.");
                return 4;
            }

            return 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            Console.Error.WriteLine($"Échec de l'enregistrement : {ex.Message}");
            return 3;
        }
    }

    /// <summary>
    /// Diagnostic mode: transcribes a WAV and reports the engine actually used
    /// and the time it took.
    ///
    /// The main troubleshooting tool of the project. It exercises the whole
    /// transcription chain without depending on the microphone or the keyboard
    /// shortcut: when dictation does not work, this is where to start in order
    /// to know which side to look at.
    ///
    /// The --model and --provider options serve to compare two configurations
    /// on the same recording.
    /// </summary>
    private static int TranscribeFile(string wavPath, string? modelOverride, string? providerOverride)
    {
        if (!File.Exists(wavPath))
        {
            Console.Error.WriteLine($"Fichier introuvable : {wavPath}");
            return 1;
        }

        string baseDirectory = AppContext.BaseDirectory;
        AppSettings settings = AppSettings.Load(Path.Combine(baseDirectory, AppSettings.FileName));

        if (modelOverride is not null)
        {
            settings.ModelPath = modelOverride;
        }

        if (providerOverride is not null)
        {
            settings.Provider = providerOverride;
        }

        // Reapplies the guards: a command-line option goes through the same
        // validation as the configuration file.
        settings.Normalize();

        string? modelPath = ModelLocator.Resolve(settings.ModelPath, baseDirectory);

        if (modelPath is null)
        {
            Console.Error.WriteLine($"Modèle introuvable : {settings.ModelPath}");
            Console.Error.WriteLine(@"Téléchargez-le avec : .\scripts\get-model.ps1");
            return 2;
        }

        try
        {
            Console.WriteLine($"Modèle    : {Path.GetFileName(modelPath)}");
            Console.WriteLine($"Calcul    : {settings.Provider}, {settings.Threads} fils");
            Console.WriteLine("Chargement du modèle...");

            using var engine = ParakeetEngine.Load(modelPath, settings.Provider, settings.Threads);

            Console.WriteLine();

            using FileStream wav = File.OpenRead(wavPath);
            TranscriptionResult result = engine
                .TranscribeAsync(wav)
                .GetAwaiter()
                .GetResult();

            Console.WriteLine(result.Text.Length > 0 ? result.Text : "(rien d'exploitable)");
            Console.WriteLine();
            Console.WriteLine($"Transcrit en {result.Duration.TotalSeconds:F2} s");

            return 0;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or DllNotFoundException)
        {
            Console.Error.WriteLine($"Échec de la transcription : {ex.Message}");
            return 3;
        }
    }

    private static string? ReadOption(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);

        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static bool HasFlag(string[] args, params string[] names) =>
        args.Any(arg => names.Contains(arg, StringComparer.Ordinal));

    private static void PrintUsage()
    {
        Console.WriteLine("HexWin — dictée vocale locale");
        Console.WriteLine();
        Console.WriteLine("  HexWin.exe");
        Console.WriteLine("      lance l'application dans la barre système");
        Console.WriteLine();
        Console.WriteLine("  HexWin.exe --transcribe fichier.wav [--model dossier] [--provider cpu]");
        Console.WriteLine("      transcrit un fichier et affiche le texte, le moteur et la durée");
        Console.WriteLine();
        Console.WriteLine("      --model    remplace le modèle de settings.json");
        Console.WriteLine("      --provider remplace le fournisseur de calcul (cpu uniquement)");
        Console.WriteLine();
        Console.WriteLine("  HexWin.exe --inject \"du texte\" [--mode Paste|Type] [--delay 4]");
        Console.WriteLine("      insère un texte dans la fenêtre active après un délai");
        Console.WriteLine();
        Console.WriteLine("  HexWin.exe --watch-hotkey");
        Console.WriteLine("      affiche les déclenchements du raccourci, sans transcrire");
        Console.WriteLine();
        Console.WriteLine("  HexWin.exe --record sortie.wav [--seconds 5]");
        Console.WriteLine("      enregistre le micro, écrit le WAV et mesure le niveau capté");
        Console.WriteLine();
        Console.WriteLine("  HexWin.exe --help");
    }
}
