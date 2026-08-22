using HexWin.Transcription;
using Xunit;

namespace HexWin.Tests;

/// <summary>
/// A test that needs the real recognition model on disk, and skips itself with
/// a usable message when it is absent.
///
/// <para>The problem this solves is a first impression. Cloning the repository
/// and running <c>dotnet test</c> used to execute all 215 tests and fail five
/// of them — not because anything was broken, but because the 480 MB model had
/// not been downloaded yet. A newcomer had no way to tell those failures apart
/// from real ones.</para>
///
/// <para>A run-settings filter was tried first and turned out worse: VSTest
/// combines a command-line filter with the configured one instead of letting it
/// win, so <c>Category=Integration</c> became
/// <c>(Category!=Integration)&amp;(Category=Integration)</c> and matched
/// nothing at all. The integration tests were unrunnable.</para>
///
/// <para>Skipping is honest by comparison: the run stays green, the count says
/// how many were skipped, and each one states why. The
/// <c>Category=Integration</c> trait is kept alongside, since CI still needs to
/// exclude them up front rather than spend a Windows runner discovering they
/// would skip.</para>
/// </summary>
public sealed class ModelRequiredFactAttribute : FactAttribute
{
    private const string ModelDirectory = "models/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8";

    public ModelRequiredFactAttribute()
    {
        if (ModelLocator.Resolve(ModelDirectory, AppContext.BaseDirectory) is null)
        {
            Skip = "Recognition model not present. Run scripts/get-model.ps1 to download it.";
        }
    }
}
