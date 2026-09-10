using System.Text.Json;
using System.Text.Json.Serialization;

namespace HexWin.Configuration;

/// <summary>
/// How the transcribed text is handed back to the active application.
/// </summary>
public enum InsertionMode
{
    /// <summary>Clipboard then Ctrl+V. Instant, even on a long text.</summary>
    Paste,

    /// <summary>Simulated typing, character by character. Slower, but it gets
    /// through in the rare applications that ignore pasting.</summary>
    Type,
}

/// <summary>
/// What tells the user that a recording has started and stopped.
///
/// The tray icon already turns red, but nobody watches the tray while
/// dictating: the eye is on the text being written. Hence a cue delivered
/// where the user actually is.
/// </summary>
public enum FeedbackMode
{
    /// <summary>Nothing beyond the tray icon.</summary>
    None,

    /// <summary>A circle at the top of the screen, for the whole recording.</summary>
    Visual,

    /// <summary>A short tone when it starts, another when it stops.</summary>
    Sound,

    /// <summary>Both at once.</summary>
    Both,
}

/// <summary>
/// User configuration, read from settings.json next to the executable.
///
/// All the logic in this class is pure: <see cref="Parse"/> and
/// <see cref="Normalize"/> never touch the disk, which makes them entirely
/// testable. Only <see cref="Load"/> does any I/O.
///
/// The principle is that a damaged file must never stop the application from
/// starting: any invalid value is replaced by its default rather than raising
/// an exception.
/// </summary>
public sealed class AppSettings
{
    public const string FileName = "settings.json";

    /// <summary>
    /// Folder of the Parakeet model, relative to the executable. A folder and
    /// not a file: the model is made of an encoder, a decoder, a joiner and a
    /// vocabulary.
    /// </summary>
    public string ModelPath { get; set; } = DefaultModelPath;

    /// <summary>Keys to hold down to dictate.</summary>
    public string[] Hotkey { get; set; } = ["Ctrl", "Win"];

    /// <summary>Below this, the press counts as accidental and is ignored.</summary>
    public int MinRecordingMilliseconds { get; set; } = 250;

    /// <summary>Cuts the recording off if the key stays held down.</summary>
    public int MaxRecordingSeconds { get; set; } = 120;

    /// <summary>
    /// ONNX Runtime compute provider. Only "cpu" is accepted; see
    /// <see cref="KnownProviders"/> for why the GPU ones were removed.
    /// </summary>
    public string Provider { get; set; } = DefaultProvider;

    /// <summary>
    /// Threads allotted to decoding. Past a handful the gain collapses: the
    /// model is small and synchronisation costs more than the parallelism
    /// brings.
    /// </summary>
    public int Threads { get; set; } = DefaultThreads;

    /// <summary>
    /// Minutes without a dictation after which the model is released. Zero
    /// keeps it resident forever.
    ///
    /// The model takes about a gigabyte. Releasing it hands that memory back
    /// to the system; the next dictation pays for a reload, largely hidden
    /// since it starts on the key press, while the user is speaking.
    /// </summary>
    public int UnloadAfterMinutes { get; set; } = DefaultUnloadAfterMinutes;

    public InsertionMode Insertion { get; set; } = InsertionMode.Paste;

    /// <summary>Cue marking the start and the end of a recording.</summary>
    public FeedbackMode Feedback { get; set; } = FeedbackMode.Both;

    /// <summary>
    /// Colour of the circle: <c>auto</c> to follow the state colours of the
    /// tray icon, or #RRGGBB to force one colour whatever the state.
    /// </summary>
    public string FeedbackColor { get; set; } = DefaultFeedbackColor;

    /// <summary>Diameter of the circle, in pixels.</summary>
    public int FeedbackSize { get; set; } = DefaultFeedbackSize;

    /// <summary>Opacity of the circle, 0 invisible to 255 solid.</summary>
    public int FeedbackOpacity { get; set; } = DefaultFeedbackOpacity;

    /// <summary>Gap between the circle and the top of the usable area, in pixels.</summary>
    public int FeedbackTopMargin { get; set; } = DefaultFeedbackTopMargin;

    /// <summary>Logs the transcriptions and the engine actually loaded.</summary>
    public bool LogEnabled { get; set; } = true;

    // --- Reference values -----------------------------------------------------

    private const string DefaultModelPath = "models/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8";
    private const string DefaultProvider = "cpu";
    private const int DefaultThreads = 4;
    private const int DefaultUnloadAfterMinutes = 5;

    /// <summary>
    /// Follow the tray icon rather than impose a colour: red while recording,
    /// orange while transcribing. A circle that stayed one colour would say
    /// less than the icon it sits next to.
    /// </summary>
    private const string DefaultFeedbackColor = "auto";

    private const int DefaultFeedbackSize = 64;
    private const int DefaultFeedbackOpacity = 235;
    private const int DefaultFeedbackTopMargin = 40;
    private const int MaxUnloadAfterMinutes = 1_440;
    private const int MaxThreads = 32;

    private const int MinFeedbackSize = 16;
    private const int MaxFeedbackSize = 512;
    private const int MinFeedbackOpacity = 20;
    private const int MaxFeedbackTopMargin = 2_000;

    private static readonly string[] DefaultHotkey = ["Ctrl", "Win"];

    /// <summary>
    /// Compute providers that are actually available.
    ///
    /// <para>The processor is the only one, and that is not a choice: the
    /// sherpa-onnx NuGet packages are built for it alone. Asked directly, ONNX
    /// Runtime answers "Available providers: CPUExecutionProvider".</para>
    ///
    /// <para><b>"directml" and "cuda" were removed after testing.</b>
    /// sherpa-onnx accepted them, printed a warning on its native error output
    /// — "DirectML is for Windows only. Fallback to cpu!", a misleading message
    /// given that Windows is exactly where we are — then fell back to the
    /// processor. The setting therefore promised an acceleration that could
    /// never happen, with nothing telling the user.</para>
    ///
    /// <para>Using a GPU would mean rebuilding sherpa-onnx with
    /// -DSHERPA_ONNX_ENABLE_GPU=ON and swapping the native libraries. The gain
    /// would be uncertain anyway: the processor already transcribes a
    /// five-second sentence in 0.17 s.</para>
    /// </summary>
    private static readonly string[] KnownProviders = ["cpu"];

    /// <summary>
    /// Keys allowed in a shortcut. Deliberately narrow: the Fn key is absent
    /// because it is handled by the embedded controller of the keyboard and
    /// emits no code Windows can see.
    /// </summary>
    private static readonly string[] KnownHotkeyNames =
    [
        "Ctrl", "LeftCtrl", "RightCtrl",
        "Alt", "LeftAlt", "RightAlt",
        "Shift", "LeftShift", "RightShift",
        "Win", "LeftWin", "RightWin",
        "CapsLock", "Space",
        "F13", "F14", "F15", "F16", "F17", "F18", "F19", "F20", "F21", "F22", "F23", "F24",
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    // --- Reading --------------------------------------------------------------

    /// <summary>
    /// Reads the file if it exists, otherwise returns the defaults. An
    /// unreadable or malformed file also yields the defaults: dictation must
    /// keep working even when the configuration is broken.
    /// </summary>
    public static AppSettings Load(string path)
    {
        if (!File.Exists(path))
        {
            return new AppSettings();
        }

        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch (IOException)
        {
            return new AppSettings();
        }
        catch (UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    /// <summary>
    /// Builds a valid configuration from JSON. Unknown keys are ignored,
    /// invalid values are replaced by their default, and unreadable JSON
    /// simply yields the default configuration.
    /// </summary>
    public static AppSettings Parse(string json)
    {
        AppSettings? settings;

        try
        {
            settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
        }
        catch (JsonException)
        {
            settings = null;
        }

        settings ??= new AppSettings();
        settings.Normalize();
        return settings;
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    // --- Validation -----------------------------------------------------------

    /// <summary>
    /// Repairs any out-of-range value in place. Called after every read.
    /// </summary>
    public void Normalize()
    {
        if (string.IsNullOrWhiteSpace(ModelPath))
        {
            ModelPath = DefaultModelPath;
        }

        Hotkey = NormalizeHotkey(Hotkey);
        Provider = NormalizeProvider(Provider);

        MinRecordingMilliseconds = Math.Clamp(MinRecordingMilliseconds, 0, 5_000);
        MaxRecordingSeconds = Math.Clamp(MaxRecordingSeconds, 5, 600);
        Threads = Math.Clamp(Threads, 1, MaxThreads);

        // Zero stays allowed: that is how the model is kept resident.
        UnloadAfterMinutes = Math.Clamp(UnloadAfterMinutes, 0, MaxUnloadAfterMinutes);

        if (!Enum.IsDefined(Insertion))
        {
            Insertion = InsertionMode.Paste;
        }

        if (!Enum.IsDefined(Feedback))
        {
            Feedback = FeedbackMode.Both;
        }

        // A colour is rewritten canonically so the file keeps one spelling of
        // it; anything else, "auto" included, means the state colours. A typo
        // therefore lands on the palette rather than on some colour the user
        // never asked for.
        FeedbackColor = HexColor.TryParse(FeedbackColor, out int rgb)
            ? HexColor.ToHex(rgb)
            : DefaultFeedbackColor;

        FeedbackSize = Math.Clamp(FeedbackSize, MinFeedbackSize, MaxFeedbackSize);
        FeedbackTopMargin = Math.Clamp(FeedbackTopMargin, 0, MaxFeedbackTopMargin);

        // The floor is not zero on purpose. A circle asked for and then
        // rendered invisible reads as a broken feature; someone who wants no
        // circle has "feedback" for that.
        FeedbackOpacity = Math.Clamp(FeedbackOpacity, MinFeedbackOpacity, 255);
    }

    private static string NormalizeProvider(string? provider)
    {
        string? match = KnownProviders.FirstOrDefault(
            known => string.Equals(known, provider?.Trim(), StringComparison.OrdinalIgnoreCase));

        // The processor is always available: it is the only fallback that
        // cannot leave the application with no way to transcribe.
        return match ?? DefaultProvider;
    }

    private static string[] NormalizeHotkey(string[]? hotkey)
    {
        if (hotkey is null || hotkey.Length == 0)
        {
            return [.. DefaultHotkey];
        }

        string?[] canonical = [.. hotkey.Select(CanonicalHotkeyName)];

        // An unknown key invalidates the whole shortcut; it is never simply
        // dropped. Dropping a key WIDENS the combination instead of narrowing
        // it: ["Ctrl", "Fn"] would become ["Ctrl"], and dictation would fire on
        // every press of Ctrl. Falling back to the known default beats
        // producing a shortcut more permissive than intended.
        if (canonical.Any(name => name is null))
        {
            return [.. DefaultHotkey];
        }

        return [.. canonical.OfType<string>().Distinct(StringComparer.Ordinal)];
    }

    private static string? CanonicalHotkeyName(string? name) =>
        KnownHotkeyNames.FirstOrDefault(
            known => string.Equals(known, name?.Trim(), StringComparison.OrdinalIgnoreCase));
}
