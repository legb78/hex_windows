using HexWin.Configuration;
using HexWin.Interop;
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
            return TranscribeFile(wavPath, ReadOption(args, "--model"), ReadOption(args, "--runtime"));
        }

        // Le mode normal (barre système, raccourci global) arrive dans une
        // PR ultérieure.
        return 0;
    }

    /// <summary>
    /// Mode de diagnostic : transcrit un WAV et rend compte du moteur
    /// réellement utilisé et du temps passé.
    ///
    /// C'est l'outil de dépannage principal du projet. Il valide toute la
    /// chaîne Whisper sans dépendre du micro ni du raccourci clavier : quand
    /// la dictée ne fonctionne pas, c'est par là qu'on commence pour savoir
    /// de quel côté chercher.
    ///
    /// Les options --model et --runtime servent à comparer deux
    /// configurations sur le même enregistrement, notamment pour vérifier ce
    /// que Vulkan apporte réellement face au processeur.
    /// </summary>
    private static int TranscribeFile(string wavPath, string? modelOverride, string? runtimeOverride)
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

        if (runtimeOverride is not null)
        {
            settings.RuntimePreference = runtimeOverride.Split(',', StringSplitOptions.RemoveEmptyEntries
                | StringSplitOptions.TrimEntries);
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
            Console.WriteLine($"Langue    : {settings.Language}");
            Console.WriteLine($"Demandé   : {string.Join(", ", settings.RuntimePreference)}");
            Console.WriteLine("Chargement du modèle...");

            using var engine = WhisperEngine.Load(modelPath, settings.RuntimePreference);

            Console.WriteLine($"Moteur    : {engine.LoadedRuntime}");
            Console.WriteLine();

            using FileStream wav = File.OpenRead(wavPath);
            TranscriptionResult result = engine
                .TranscribeAsync(wav, settings.Language)
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
        Console.WriteLine("  HexWin.exe --transcribe fichier.wav [--model chemin] [--runtime liste]");
        Console.WriteLine("      transcrit un fichier et affiche le texte, le moteur et la durée");
        Console.WriteLine();
        Console.WriteLine("      --model    remplace le modèle de settings.json");
        Console.WriteLine("      --runtime  remplace l'ordre des moteurs, séparés par des virgules");
        Console.WriteLine("                 exemple : --runtime Cpu   pour comparer avec Vulkan");
        Console.WriteLine();
        Console.WriteLine("  HexWin.exe --help");
    }
}
