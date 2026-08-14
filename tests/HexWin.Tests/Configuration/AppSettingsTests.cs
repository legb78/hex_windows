using HexWin.Configuration;
using Xunit;

namespace HexWin.Tests.Configuration;

/// <summary>
/// La règle vérifiée ici de bout en bout : aucune entrée, si abîmée soit-elle,
/// ne doit empêcher l'application de démarrer avec une configuration utilisable.
/// </summary>
public class AppSettingsTests
{
    [Fact]
    public void Un_json_vide_rend_les_valeurs_par_defaut()
    {
        AppSettings settings = AppSettings.Parse("{}");

        Assert.Equal("fr", settings.Language);
        Assert.Equal(["Ctrl", "Win"], settings.Hotkey);
        Assert.Equal(["Vulkan", "Cpu"], settings.RuntimePreference);
        Assert.Equal(InsertionMode.Paste, settings.Insertion);
        Assert.Equal("models/ggml-large-v3-turbo.bin", settings.ModelPath);
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

        Assert.Equal("fr", settings.Language);
        Assert.Equal(["Ctrl", "Win"], settings.Hotkey);
    }

    [Fact]
    public void Les_cles_inconnues_sont_ignorees()
    {
        // Une clé restée d'une version précédente ne doit pas tout faire échouer.
        AppSettings settings = AppSettings.Parse(
            """{"language": "en", "reglageDisparu": 42, "autreChose": {"a": 1}}""");

        Assert.Equal("en", settings.Language);
    }

    // --- Langue ---------------------------------------------------------------

    [Theory]
    [InlineData("de")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("francais")]
    public void Une_langue_non_supportee_repli_sur_le_francais(string language)
    {
        AppSettings settings = AppSettings.Parse($$"""{"language": "{{language}}"}""");

        Assert.Equal("fr", settings.Language);
    }

    [Theory]
    [InlineData("FR", "fr")]
    [InlineData("En", "en")]
    [InlineData("  en  ", "en")]
    public void La_langue_est_reconnue_quelle_que_soit_la_casse(string written, string expected)
    {
        AppSettings settings = AppSettings.Parse($$"""{"language": "{{written}}"}""");

        Assert.Equal(expected, settings.Language);
    }

    // --- Moteurs de calcul ----------------------------------------------------

    [Fact]
    public void Cpu_est_ajoute_en_dernier_recours_quand_il_manque()
    {
        // Invariant central : une configuration ne listant qu'un moteur
        // potentiellement indisponible laisserait sinon l'application
        // incapable de transcrire quoi que ce soit.
        AppSettings settings = AppSettings.Parse("""{"runtimePreference": ["Vulkan"]}""");

        Assert.Equal(["Vulkan", "Cpu"], settings.RuntimePreference);
    }

    [Fact]
    public void Cpu_deja_present_garde_sa_place()
    {
        AppSettings settings = AppSettings.Parse("""{"runtimePreference": ["Cpu", "Vulkan"]}""");

        Assert.Equal(["Cpu", "Vulkan"], settings.RuntimePreference);
    }

    [Fact]
    public void Les_moteurs_inconnus_sont_ecartes()
    {
        AppSettings settings = AppSettings.Parse(
            """{"runtimePreference": ["Metal", "Vulkan", "TotalementInvente"]}""");

        Assert.Equal(["Vulkan", "Cpu"], settings.RuntimePreference);
    }

    [Theory]
    [InlineData("""{"runtimePreference": []}""")]
    [InlineData("""{"runtimePreference": ["Metal"]}""")]
    [InlineData("""{"runtimePreference": null}""")]
    public void Une_liste_de_moteurs_inexploitable_repli_sur_le_defaut(string json)
    {
        AppSettings settings = AppSettings.Parse(json);

        Assert.Equal(["Vulkan", "Cpu"], settings.RuntimePreference);
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

        Assert.Equal("models/ggml-large-v3-turbo.bin", settings.ModelPath);
    }

    [Fact]
    public void Les_commentaires_sont_acceptes_dans_le_fichier()
    {
        // settings.json en contient : il est écrit pour être lu et modifié
        // par quelqu'un qui ne programme pas.
        AppSettings settings = AppSettings.Parse(
            """
            {
              // la langue de dictée
              "language": "en"
            }
            """);

        Assert.Equal("en", settings.Language);
    }

    [Fact]
    public void Un_aller_retour_par_le_json_conserve_les_valeurs()
    {
        var original = new AppSettings
        {
            ModelPath = "models/ggml-tiny.bin",
            Language = "en",
            Hotkey = ["CapsLock"],
            MinRecordingMilliseconds = 400,
            MaxRecordingSeconds = 60,
            RuntimePreference = ["Cpu"],
            Insertion = InsertionMode.Type,
            LogEnabled = false,
        };

        AppSettings relu = AppSettings.Parse(original.ToJson());

        Assert.Equal(original.ModelPath, relu.ModelPath);
        Assert.Equal(original.Language, relu.Language);
        Assert.Equal(original.Hotkey, relu.Hotkey);
        Assert.Equal(original.MinRecordingMilliseconds, relu.MinRecordingMilliseconds);
        Assert.Equal(original.MaxRecordingSeconds, relu.MaxRecordingSeconds);
        Assert.Equal(original.RuntimePreference, relu.RuntimePreference);
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

        Assert.Equal("fr", settings.Language);
    }

    [Fact]
    public void Un_fichier_present_est_relu_correctement()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hexwin-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """{"language": "en", "hotkey": ["CapsLock"]}""");

        try
        {
            AppSettings settings = AppSettings.Load(path);

            Assert.Equal("en", settings.Language);
            Assert.Equal(["CapsLock"], settings.Hotkey);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
