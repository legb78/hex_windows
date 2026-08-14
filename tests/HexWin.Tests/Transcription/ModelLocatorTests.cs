using HexWin.Transcription;
using Xunit;

namespace HexWin.Tests.Transcription;

/// <summary>
/// La recherche est testée sans toucher au disque : le prédicat d'existence
/// est injecté, ce qui permet de décrire des arborescences entières en une
/// ligne et de vérifier l'ordre exact des emplacements essayés.
/// </summary>
public class ModelLocatorTests
{
    private const string BaseDirectory = @"C:\app\bin\x64\Release\net9.0-windows";

    private static Func<string, bool> ExistsOnly(params string[] paths) =>
        candidate => paths.Contains(candidate, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Le_modele_pose_a_cote_de_l_executable_est_trouve_immediatement()
    {
        // Cas de l'application publiée : models/ est livré avec l'exécutable.
        const string expected = @"C:\app\bin\x64\Release\net9.0-windows\models\m.bin";

        string? found = ModelLocator.Resolve("models/m.bin", BaseDirectory, ExistsOnly(expected));

        Assert.Equal(expected, found);
    }

    [Fact]
    public void Le_modele_range_a_la_racine_du_depot_est_trouve_en_remontant()
    {
        // Cas du développement : l'exécutable est au fond de bin/, le modèle
        // à la racine. Sans remontée, il faudrait dupliquer 1,5 Go par
        // configuration de compilation.
        const string expected = @"C:\app\models\m.bin";

        string? found = ModelLocator.Resolve("models/m.bin", BaseDirectory, ExistsOnly(expected));

        Assert.Equal(expected, found);
    }

    [Fact]
    public void Le_plus_proche_de_l_executable_l_emporte()
    {
        const string closest = @"C:\app\bin\x64\Release\net9.0-windows\models\m.bin";
        const string farther = @"C:\app\models\m.bin";

        string? found = ModelLocator.Resolve("models/m.bin", BaseDirectory, ExistsOnly(closest, farther));

        Assert.Equal(closest, found);
    }

    [Fact]
    public void Un_modele_absent_partout_rend_null()
    {
        string? found = ModelLocator.Resolve("models/m.bin", BaseDirectory, _ => false);

        Assert.Null(found);
    }

    [Fact]
    public void La_remontee_s_arrete_a_la_limite_demandee()
    {
        // Sans limite, une recherche infructueuse remonterait jusqu'à la
        // racine du disque en interrogeant le système à chaque niveau.
        const string tooFarUp = @"C:\models\m.bin";

        string? found = ModelLocator.Resolve(
            "models/m.bin", BaseDirectory, ExistsOnly(tooFarUp), maxAscent: 1);

        Assert.Null(found);
    }

    [Fact]
    public void Un_chemin_absolu_est_pris_au_mot()
    {
        const string absolute = @"D:\mes-modeles\m.bin";

        string? found = ModelLocator.Resolve(absolute, BaseDirectory, ExistsOnly(absolute));

        Assert.Equal(absolute, found);
    }

    [Fact]
    public void Un_chemin_absolu_inexistant_ne_declenche_aucune_remontee()
    {
        // Un chemin absolu exprime une intention explicite : aller chercher
        // ailleurs serait surprenant.
        const string absolute = @"D:\mes-modeles\m.bin";
        const string elsewhere = @"C:\app\models\m.bin";

        string? found = ModelLocator.Resolve(absolute, BaseDirectory, ExistsOnly(elsewhere));

        Assert.Null(found);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Un_chemin_vide_est_refuse(string configured)
    {
        Assert.Throws<ArgumentException>(
            () => ModelLocator.Resolve(configured, BaseDirectory, _ => true));
    }
}
