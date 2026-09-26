using HexWin.Transcription;
using Xunit;

namespace HexWin.Tests.Transcription;

/// <summary>
/// The folder picked in the settings window becomes a line of settings.json.
/// Relative inside the application, absolute outside it: a path that climbs
/// out with ".." would break the day the application is moved.
/// </summary>
public class ModelPathTests
{
    private static readonly string Base = Path.Combine(Path.GetTempPath(), "HexWin");

    [Fact]
    public void A_folder_under_the_application_is_written_relative_with_forward_slashes()
    {
        string selected = Path.Combine(Base, "models", "parakeet");

        Assert.Equal("models/parakeet", ModelLocator.ToConfiguredPath(selected, Base));
    }

    [Fact]
    public void A_folder_outside_the_application_is_written_absolute()
    {
        string selected = Path.Combine(Path.GetTempPath(), "elsewhere", "parakeet");

        Assert.Equal(Path.GetFullPath(selected).Replace('\\', '/'), ModelLocator.ToConfiguredPath(selected, Base));
    }

    [Fact]
    public void A_sibling_folder_sharing_a_prefix_is_not_taken_for_a_subfolder()
    {
        // "HexWin-models" starts with "HexWin" but is not inside it.
        string selected = Base + "-models";

        Assert.Equal(Path.GetFullPath(selected).Replace('\\', '/'), ModelLocator.ToConfiguredPath(selected, Base));
    }

    [Fact]
    public void A_folder_on_another_drive_is_written_absolute()
    {
        Assert.Equal("Z:/models/parakeet", ModelLocator.ToConfiguredPath(@"Z:\models\parakeet", @"C:\Program Files\HexWin"));
    }

    [Fact]
    public void The_written_path_is_found_again_by_the_locator()
    {
        string selected = Path.Combine(Base, "models", "parakeet");
        string configured = ModelLocator.ToConfiguredPath(selected, Base);

        string? resolved = ModelLocator.Resolve(
            configured,
            Base,
            candidate => string.Equals(Path.GetFullPath(candidate), Path.GetFullPath(selected), StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(resolved);
    }
}
