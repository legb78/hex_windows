using System.Globalization;

namespace HexWin.Ui;

/// <summary>
/// Every text HexWin shows, in one language: the settings window, the tray
/// menu and tooltip, the balloons and dialogs, the names of the keys.
///
/// <para>A class of required properties rather than resource files. A
/// language that forgets a text does not compile, where a missing .resx entry
/// would reach the user as a blank label; the texts stay plain values a test
/// can read; and the single-file executable carries no satellite assemblies.
/// The log is not in here: it is a diagnostic, and stays in French.</para>
/// </summary>
public sealed partial class UiStrings
{
    private static UiStrings? _current;

    /// <summary>
    /// The texts in force. Set once at startup from the settings, then again
    /// when the settings window saves a new language. Read on the interface
    /// thread only, when a window or a menu is built.
    ///
    /// <para>English until set, read through a field rather than initialised
    /// from <see cref="English"/>: the two languages live in other files of
    /// this partial class, and C# does not order static initialisers across
    /// files — an initialiser here could run first and capture null.</para>
    /// </summary>
    public static UiStrings Current
    {
        get => _current ?? English;
        set => _current = value;
    }

    /// <summary>
    /// The texts for a <c>language</c> setting. <c>auto</c> follows the Windows
    /// display language, and anything Windows speaks that HexWin does not
    /// falls back to English — the language most people can read after their
    /// own.
    /// </summary>
    public static UiStrings For(string? language, CultureInfo windowsLanguage)
    {
        ArgumentNullException.ThrowIfNull(windowsLanguage);

        string chosen = string.Equals(language, "auto", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(language)
            ? windowsLanguage.TwoLetterISOLanguageName
            : language.Trim();

        return string.Equals(chosen, "fr", StringComparison.OrdinalIgnoreCase) ? French : English;
    }

    /// <summary>Numbers are written the way this language writes them: 5 000 or 5,000.</summary>
    public required CultureInfo Culture { get; init; }

    // --- Values and names ---------------------------------------------------------

    /// <summary>Keys of settings.json, as the window names them.</summary>
    public required IReadOnlyDictionary<string, string> SettingNames { get; init; }

    /// <summary>Keys of a shortcut, as printed on this language's keyboards, side spelled out.</summary>
    public required IReadOnlyDictionary<string, string> KeyNames { get; init; }

    public required Func<int, string> Percent { get; init; }

    public required Func<int, string> ThreadCount { get; init; }

    public required string Never { get; init; }

    public required string Off { get; init; }

    public required string CaveatAltGr { get; init; }

    public required string CaveatSpace { get; init; }

    public required string CaveatBothShifts { get; init; }

    public required string CaveatLeftShift { get; init; }

    public required string CaveatRightShift { get; init; }

    public required string CaveatCapsLock { get; init; }

    // --- Settings window: frame -----------------------------------------------------

    public required string WindowTitle { get; init; }

    public required Func<string, string> About { get; init; }

    public required string Save { get; init; }

    public required string Cancel { get; init; }

    public required string NavDictation { get; init; }

    public required string NavCues { get; init; }

    public required string NavEngine { get; init; }

    public required string NavGeneral { get; init; }

    // --- Settings window: dictation page ---------------------------------------------

    public required string PageDictation { get; init; }

    public required string SectionHotkey { get; init; }

    public required string HotkeyTitle { get; init; }

    public required string HotkeyHint { get; init; }

    public required string Change { get; init; }

    public required string ChangeHotkeyName { get; init; }

    public required string CapturePrompt { get; init; }

    public required string CaptureHint { get; init; }

    /// <summary>The keys held so far during a capture, with the mark that more may come.</summary>
    public required Func<string, string> Holding { get; init; }

    public required string CaptureRefused { get; init; }

    public required string SectionRecording { get; init; }

    public required string MinimumLengthTitle { get; init; }

    public required string MinimumLengthHint { get; init; }

    public required string MaximumLengthTitle { get; init; }

    public required string MaximumLengthHint { get; init; }

    public required string SectionInsertion { get; init; }

    public required string InsertionTitle { get; init; }

    public required string InsertionHint { get; init; }

    public required string InsertPaste { get; init; }

    public required string InsertType { get; init; }

    public required string SegmentationTitle { get; init; }

    public required string SegmentationHint { get; init; }

    public required string PauseTitle { get; init; }

    public required string PauseHint { get; init; }

    // --- Settings window: cues page --------------------------------------------------

    public required string PageCues { get; init; }

    public required string SectionWhileDictating { get; init; }

    public required string CircleTitle { get; init; }

    public required string CircleHint { get; init; }

    public required string ToneTitle { get; init; }

    public required string ToneHint { get; init; }

    public required string SectionCircle { get; init; }

    public required string ColorTitle { get; init; }

    public required string ColorHint { get; init; }

    public required string ColorByState { get; init; }

    public required string ColorFixed { get; init; }

    public required string PickColorName { get; init; }

    public required string SizeTitle { get; init; }

    public required string OpacityTitle { get; init; }

    public required string TopMarginTitle { get; init; }

    // --- Settings window: engine page -------------------------------------------------

    public required string PageEngine { get; init; }

    public required string EngineNote { get; init; }

    public required string SectionModel { get; init; }

    public required string ModelTitle { get; init; }

    public required string Browse { get; init; }

    public required string BrowseModelName { get; init; }

    public required Func<string, string> ModelNotFound { get; init; }

    public required string ModelFolderDialog { get; init; }

    public required Func<string, string> ModelUnusable { get; init; }

    public required string SectionPerformance { get; init; }

    public required string ThreadsTitle { get; init; }

    public required string ThreadsHint { get; init; }

    public required string UnloadTitle { get; init; }

    public required string UnloadHint { get; init; }

    // --- Settings window: general page ------------------------------------------------

    public required string PageGeneral { get; init; }

    public required string SectionStartup { get; init; }

    public required string AutoStartTitle { get; init; }

    public required string AutoStartHint { get; init; }

    public required string DesktopShortcutTitle { get; init; }

    public required string DesktopShortcutHint { get; init; }

    public required string SectionDisplay { get; init; }

    public required string LanguageTitle { get; init; }

    public required string LanguageHint { get; init; }

    public required string LanguageAuto { get; init; }

    public required string SectionDiagnostics { get; init; }

    public required string LogTitle { get; init; }

    public required string LogHint { get; init; }

    public required string OpenTitle { get; init; }

    public required string OpenHint { get; init; }

    public required string Logs { get; init; }

    // --- Settings window: save report ---------------------------------------------------

    public required Func<string, string> NotPersisted { get; init; }

    public required Func<string, string> AwaitingRestart { get; init; }

    // --- Tray ---------------------------------------------------------------------------------

    public required string MenuSettings { get; init; }

    public required string MenuOpenSettingsFile { get; init; }

    public required string MenuOpenLogFolder { get; init; }

    public required string MenuShowCircle { get; init; }

    public required string MenuPlayTone { get; init; }

    public required string MenuStartWithWindows { get; init; }

    public required string MenuQuit { get; init; }

    public required string TipLoading { get; init; }

    public required Func<string, string> TipReady { get; init; }

    public required string TipRecording { get; init; }

    public required string TipTranscribing { get; init; }

    public required string TipModelMissing { get; init; }

    public required string BalloonModelMissing { get; init; }

    public required Func<string, string> BalloonModelMissingBody { get; init; }

    public required string BalloonMicrophone { get; init; }

    public required string MicrophoneMissing { get; init; }

    public required string BalloonCannotOpen { get; init; }

    public required string BalloonShortcutFailed { get; init; }

    public required string BalloonShortcutFailedBody { get; init; }

    public required string DesktopShortcutQuestion { get; init; }

    public required string DesktopShortcutDescription { get; init; }

    /// <summary>The dialog shown at startup when the model cannot be found.</summary>
    public required Func<string, string> StartupModelMissing { get; init; }

    /// <summary>A file of the model is missing, named by its path.</summary>
    public required Func<string, string> ModelFileMissing { get; init; }

    /// <summary>The dialog shown when HexWin stops on an unexpected error.</summary>
    public required Func<string, string, string> Crashed { get; init; }

    /// <summary>Stands in for the crash log path when the log folder cannot be written.</summary>
    public required string CrashLogUnavailable { get; init; }

    /// <summary>
    /// What went wrong with a model, in this language.
    ///
    /// <para>The engine throws its own message, in French, and that message goes
    /// to the log as it is — the log stays in French. The interface rebuilds it
    /// from the file name when it can, so an English window does not show a
    /// French sentence; anything else — a native library that fails to load —
    /// is passed through unchanged, having no better wording to offer.</para>
    /// </summary>
    public string ModelProblem(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return error is FileNotFoundException { FileName: { Length: > 0 } file }
            ? ModelFileMissing(file)
            : error.Message;
    }
}
