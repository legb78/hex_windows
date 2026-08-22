using HexWin.Transcription;
using Xunit;

namespace HexWin.Tests.Transcription;

/// <summary>
/// The search is tested without touching the disk: the existence predicate is
/// injected, which makes it possible to describe whole trees in one line and
/// to check the exact order in which locations are tried.
/// </summary>
public class ModelLocatorTests
{
    private const string BaseDirectory = @"C:\app\bin\x64\Release\net9.0-windows";

    private static Func<string, bool> ExistsOnly(params string[] paths) =>
        candidate => paths.Contains(candidate, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void A_model_next_to_the_executable_is_found_immediately()
    {
        // The published application: models/ ships with the executable.
        const string expected = @"C:\app\bin\x64\Release\net9.0-windows\models\m.bin";

        string? found = ModelLocator.Resolve("models/m.bin", BaseDirectory, ExistsOnly(expected));

        Assert.Equal(expected, found);
    }

    [Fact]
    public void A_model_at_the_repository_root_is_found_by_walking_up()
    {
        // Development: the executable is deep inside bin/, the model at the
        // root. Without walking up, 1.5 GB would have to be duplicated per
        // build configuration.
        const string expected = @"C:\app\models\m.bin";

        string? found = ModelLocator.Resolve("models/m.bin", BaseDirectory, ExistsOnly(expected));

        Assert.Equal(expected, found);
    }

    [Fact]
    public void The_closest_to_the_executable_wins()
    {
        const string closest = @"C:\app\bin\x64\Release\net9.0-windows\models\m.bin";
        const string farther = @"C:\app\models\m.bin";

        string? found = ModelLocator.Resolve("models/m.bin", BaseDirectory, ExistsOnly(closest, farther));

        Assert.Equal(closest, found);
    }

    [Fact]
    public void A_model_missing_everywhere_returns_null()
    {
        string? found = ModelLocator.Resolve("models/m.bin", BaseDirectory, _ => false);

        Assert.Null(found);
    }

    [Fact]
    public void Walking_up_stops_at_the_requested_limit()
    {
        // With no limit, a fruitless search would climb to the root of the
        // disk, asking the system at every level.
        const string tooFarUp = @"C:\models\m.bin";

        string? found = ModelLocator.Resolve(
            "models/m.bin", BaseDirectory, ExistsOnly(tooFarUp), maxAscent: 1);

        Assert.Null(found);
    }

    [Fact]
    public void An_absolute_path_is_taken_at_its_word()
    {
        const string absolute = @"D:\mes-modeles\m.bin";

        string? found = ModelLocator.Resolve(absolute, BaseDirectory, ExistsOnly(absolute));

        Assert.Equal(absolute, found);
    }

    [Fact]
    public void A_missing_absolute_path_triggers_no_walking_up()
    {
        // An absolute path states an explicit intention: looking elsewhere
        // would be surprising.
        const string absolute = @"D:\mes-modeles\m.bin";
        const string elsewhere = @"C:\app\models\m.bin";

        string? found = ModelLocator.Resolve(absolute, BaseDirectory, ExistsOnly(elsewhere));

        Assert.Null(found);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_path_is_refused(string configured)
    {
        Assert.Throws<ArgumentException>(
            () => ModelLocator.Resolve(configured, BaseDirectory, _ => true));
    }
}
