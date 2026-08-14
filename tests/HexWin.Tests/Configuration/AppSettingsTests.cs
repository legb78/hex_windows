using HexWin.Configuration;
using Xunit;

namespace HexWin.Tests.Configuration;

/// <summary>
/// La règle vérifiée ici de bout en bout : aucune entrée, si abîmée soit-elle,
/// ne doit empêcher l'application de démarrer avec une configuration utilisable.
/// </summary>
public class AppSettingsTests
{
    private const string DefaultModel = "models/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8";

    [Fact]
    public void Un_json_vide_rend_les_valeurs_par_defaut()
    {
        AppSettings settings = AppSettings.Parse("{}");

        Assert.Equal(["Ctrl", "Win"], settings.Hotkey);
        Assert.Equal("cpu", settings.Provider);
        Assert.Equal(InsertionMode.Paste, settings.Insertion);
        Assert.Equal(DefaultModel, settings.ModelPath);
        Assert.True(settings.LogEnabled);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("pas du json")]
    [InlineData("{ceci n'est pas valide}")]
    [InlineData("[1, 2, 3]")]
    public void Un_json_illisible_rend_les_valeurs_par_defaut(string json)
    {
        AppSettings settings = AppSettings.Parse(json);

        Assert.Equal("cpu", settings.Provider);
        Assert.Equal(["Ctrl", "Win"], settings.Hotkey);
    }

    [Fact]
    public void Les_cles_inconnues_sont_ignorees()
    {
        // Une clé restée d'une version précédente ne doit pas tout faire
        // échouer. « language » en est un cas réel : le réglage existait au
        // temps de Whisper, Parakeet détecte seul la langue parlée.
        AppSettings settings = AppSettings.Parse(
            """{"provider": "cpu", "language": "fr", "runtimePreference": ["Vulkan"]}""");

        Assert.Equal("cpu", settings.Provider);
    }

    // --- Fournisseur de calcul ------------------------------------------------

    [Theory]
    [InlineData("cpu", "cpu")]
    [InlineData("CPU", "cpu")]
    [InlineData("  Cpu  ", "cpu")]
    public void Le_processeur_est_reconnu_quelle_que_soit_la_casse(string written, string expected)
    {
        AppSettings settings = AppSettings.Parse($$"""{"provider": "{{written}}"}""");

        Assert.Equal(expected, settings.Provider);
    }

    [Theory]
    [InlineData("directml")]
    [InlineData("cuda")]
    public void Les_fournisseurs_GPU_sont_refuses_car_indisponibles(string provider)
    {
        // Retirés après essai : sherpa-onnx les acceptait, affichait un
        // avertissement sur sa sortie native, puis retombait sur le
        // processeur. Le réglage promettait une accélération inatteignable
        // sans que rien ne le signale. Les paquets NuGet de sherpa-onnx ne
        // sont compilés que pour le processeur.
        AppSettings settings = AppSettings.Parse($$"""{"provider": "{{provider}}"}""");

        Assert.Equal("cpu", settings.Provider);
    }

    [Theory]
    [InlineData("vulkan")]
    [InlineData("metal")]
    [InlineData("")]
    [InlineData("   ")]
    public void Un_fournisseur_inconnu_repli_sur_le_processeur(string provider)
    {
        AppSettings settings = AppSettings.Parse($$"""{"provider": "{{provider}}"}""");

        Assert.Equal("cpu", settings.Provider);
    }

    // --- Fils d'exécution -----------------------------------------------------

    [Theory]
    [InlineData(-4, 1)]
    [InlineData(0, 1)]
    [InlineData(4, 4)]
    [InlineData(1_000, 32)]
    public void Le_nombre_de_fils_est_ramene_dans_ses_bornes(int written, int expected)
    {
        // Zéro fil bloquerait le décodage ; mille saturerait la machine sans
        // rien accélérer, le modèle étant petit.
        AppSettings settings = AppSettings.Parse($$"""{"threads": {{written}}}""");

        Assert.Equal(expected, settings.Threads);
    }

    // --- Raccourci ------------------------------------------------------------

    [Fact]
    public void La_touche_Fn_est_refusee_et_repli_sur_le_defaut()
    {
        // Fn est gérée par le contrôleur embarqué du clavier : elle n'émet
        // aucun code que Windows puisse observer. L'accepter donnerait un
        // raccourci qui ne se déclenche jamais, sans le moindre message.
        AppSettings settings = AppSettings.Parse("""{"hotkey": ["Ctrl", "Fn"]}""");

        Assert.Equal(["Ctrl", "Win"], settings.Hotkey);
    }

    [Fact]
    public void Une_touche_inconnue_invalide_le_raccourci_entier()
    {
        // Retirer la touche fautive élargirait la combinaison au lieu de la
        // restreindre : ["CapsLock", "Inconnue"] deviendrait ["CapsLock"] et
        // la dictée partirait au moindre appui sur Verr Maj. Le repli sur le
        // défaut est le seul comportement qui ne surprend pas.
        AppSettings settings = AppSettings.Parse("""{"hotkey": ["CapsLock", "Inconnue"]}""");

        Assert.Equal(["Ctrl", "Win"], settings.Hotkey);
    }

    [Fact]
    public void Un_raccourci_entierement_valide_est_conserve()
    {
        AppSettings settings = AppSettings.Parse("""{"hotkey": ["CapsLock"]}""");

        Assert.Equal(["CapsLock"], settings.Hotkey);
    }

    [Fact]
    public void Le_raccourci_est_reconnu_quelle_que_soit_la_casse()
    {
        AppSettings settings = AppSettings.Parse("""{"hotkey": ["ctrl", "WIN"]}""");

        Assert.Equal(["Ctrl", "Win"], settings.Hotkey);
    }

    [Fact]
    public void Le_raccourci_est_dedoublonne()
    {
        AppSettings settings = AppSettings.Parse("""{"hotkey": ["Ctrl", "ctrl", "Win"]}""");

        Assert.Equal(["Ctrl", "Win"], settings.Hotkey);
    }

    [Theory]
    [InlineData("""{"hotkey": []}""")]
    [InlineData("""{"hotkey": null}""")]
    public void Un_raccourci_vide_repli_sur_le_defaut(string json)
    {
        // Sans cette règle, l'application démarrerait sans aucun moyen de
        // déclencher la dictée, en silence.
        AppSettings settings = AppSettings.Parse(json);

        Assert.Equal(["Ctrl", "Win"], settings.Hotkey);
    }

    // --- Durées ---------------------------------------------------------------

    [Theory]
    [InlineData(-100, 0)]
    [InlineData(0, 0)]
    [InlineData(250, 250)]
    [InlineData(999_999, 5_000)]
    public void La_duree_minimale_est_ramenee_dans_ses_bornes(int written, int expected)
    {
        AppSettings settings = AppSettings.Parse($$"""{"minRecordingMilliseconds": {{written}}}""");

        Assert.Equal(expected, settings.MinRecordingMilliseconds);
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(120, 120)]
    [InlineData(10_000, 600)]
    public void La_duree_maximale_est_ramenee_dans_ses_bornes(int written, int expected)
    {
        AppSettings settings = AppSettings.Parse($$"""{"maxRecordingSeconds": {{written}}}""");

        Assert.Equal(expected, settings.MaxRecordingSeconds);
    }

    // --- Divers ---------------------------------------------------------------

    [Theory]
    [InlineData("""{"modelPath": ""}""")]
    [InlineData("""{"modelPath": "   "}""")]
    [InlineData("""{"modelPath": null}""")]
    public void Un_chemin_de_modele_vide_repli_sur_le_defaut(string json)
    {
        AppSettings settings = AppSettings.Parse(json);

        Assert.Equal(DefaultModel, settings.ModelPath);
    }

    [Fact]
    public void Les_commentaires_sont_acceptes_dans_le_fichier()
    {
        // settings.json en contient : il est écrit pour être lu et modifié
        // par quelqu'un qui ne programme pas.
        AppSettings settings = AppSettings.Parse(
            """
            {
              // le raccourci
              "hotkey": ["CapsLock"]
            }
            """);

        Assert.Equal(["CapsLock"], settings.Hotkey);
    }

    [Fact]
    public void Un_aller_retour_par_le_json_conserve_les_valeurs()
    {
        var original = new AppSettings
        {
            ModelPath = "models/autre-modele",
            Hotkey = ["CapsLock"],
            MinRecordingMilliseconds = 400,
            MaxRecordingSeconds = 60,
            Provider = "cpu",
            Threads = 8,
            Insertion = InsertionMode.Type,
            LogEnabled = false,
        };

        AppSettings relu = AppSettings.Parse(original.ToJson());

        Assert.Equal(original.ModelPath, relu.ModelPath);
        Assert.Equal(original.Hotkey, relu.Hotkey);
        Assert.Equal(original.MinRecordingMilliseconds, relu.MinRecordingMilliseconds);
        Assert.Equal(original.MaxRecordingSeconds, relu.MaxRecordingSeconds);
        Assert.Equal(original.Provider, relu.Provider);
        Assert.Equal(original.Threads, relu.Threads);
        Assert.Equal(original.Insertion, relu.Insertion);
        Assert.False(relu.LogEnabled);
    }

    [Fact]
    public void Le_mode_d_insertion_est_ecrit_en_toutes_lettres()
    {
        // Pour rester lisible dans settings.json, plutôt qu'un entier opaque.
        string json = new AppSettings { Insertion = InsertionMode.Type }.ToJson();

        Assert.Contains("\"Type\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Un_fichier_absent_rend_les_valeurs_par_defaut()
    {
        string absent = Path.Combine(Path.GetTempPath(), $"hexwin-{Guid.NewGuid():N}.json");

        AppSettings settings = AppSettings.Load(absent);

        Assert.Equal("cpu", settings.Provider);
    }

    [Fact]
    public void Un_fichier_present_est_relu_correctement()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hexwin-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """{"threads": 8, "hotkey": ["CapsLock"]}""");

        try
        {
            AppSettings settings = AppSettings.Load(path);

            Assert.Equal(8, settings.Threads);
            Assert.Equal(["CapsLock"], settings.Hotkey);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
