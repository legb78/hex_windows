using HexWin.Audio;
using HexWin.Configuration;
using HexWin.Input;
using HexWin.Interop;
using HexWin.Output;
using HexWin.Transcription;

namespace HexWin;

internal static class Program
{
    /// <summary>
    /// Point d'entrée. STAThread est exigé par Windows Forms et par les API
    /// de presse-papiers utilisées pour l'insertion du texte.
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

        // Le mode normal (barre système, raccourci global) arrive dans une
        // PR ultérieure.
        return 0;
    }

    /// <summary>
    /// Mode de diagnostic de l'insertion : attend quelques secondes, le temps
    /// de placer le curseur dans l'application visée, puis y insère un texte.
    ///
    /// Le délai est indispensable : sans lui, le texte partirait dans la
    /// console qui a lancé la commande, ce qui ne prouverait rien.
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

        for (int remaining = delay; remaining > 0; remaining--)
        {
            Console.Write($" {remaining}");
            Thread.Sleep(TimeSpan.FromSeconds(1));
        }

        Console.WriteLine();
        TextInjector.Insert(text, mode);

        // Laisse au collage le temps d'aboutir et au presse-papiers celui
        // d'être restauré avant que le processus ne se termine.
        Thread.Sleep(TimeSpan.FromSeconds(1));

        Console.WriteLine("Insertion demandée.");
        return 0;
    }

    /// <summary>
    /// Mode de diagnostic du raccourci : installe le hook et rend compte de
    /// chaque déclenchement, sans enregistrer ni transcrire.
    ///
    /// C'est le seul moyen de vérifier cette couche : Windows marque toute
    /// frappe injectée par un programme, et le hook les ignore délibérément
    /// pour ne pas réagir à ce qu'il produit lui-même. Il faut donc un
    /// véritable appui de doigt.
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

        // Un hook bas niveau n'est alimenté que par une boucle de messages :
        // sans elle, le rappel ne serait jamais appelé.
        Application.Run();

        return 0;
    }

    /// <summary>
    /// Mode de diagnostic du micro : enregistre quelques secondes, écrit le
    /// WAV et mesure l'amplitude obtenue.
    ///
    /// La mesure est le point important. Un micro coupé, débranché ou interdit
    /// par les réglages de confidentialité produit un fichier parfaitement
    /// valide, de la bonne durée, et totalement silencieux — que le moteur
    /// transcrit ensuite en une phrase inventée. Sans niveau affiché, on
    /// chercherait la panne du côté de la transcription.
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
    /// Mode de diagnostic : transcrit un WAV et rend compte du moteur
    /// réellement utilisé et du temps passé.
    ///
    /// C'est l'outil de dépannage principal du projet. Il valide toute la
    /// chaîne de transcription sans dépendre du micro ni du raccourci clavier : quand
    /// la dictée ne fonctionne pas, c'est par là qu'on commence pour savoir
    /// de quel côté chercher.
    ///
    /// Les options --model et --provider servent à comparer deux
    /// configurations sur le même enregistrement.
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

        // Réapplique les garde-fous : une option de ligne de commande passe
        // par les mêmes validations que le fichier de configuration.
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
        Console.WriteLine("      --provider remplace le fournisseur de calcul : cpu, directml, cuda");
        Console.WriteLine("");
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
