using System.Diagnostics.CodeAnalysis;
using HexWin.Configuration;
using HexWin.Feedback;
using HexWin.Input;
using HexWin.Transcription;

namespace HexWin.Ui;

/// <summary>What the window hands back when the user saves.</summary>
/// <param name="Settings">The edited configuration, already normalised.</param>
/// <param name="AutoStart">
/// Launch at sign-in. Lives in the registry, not in settings.json, hence apart.
/// </param>
/// <param name="DesktopShortcut">
/// The shortcut on the desktop. A file on the desktop, not a setting, hence apart too.
/// </param>
internal sealed record SettingsSubmission(AppSettings Settings, bool AutoStart, bool DesktopShortcut);

/// <summary>What became of a save, for the window to tell the user.</summary>
/// <param name="AwaitingRestart">Keys saved, but only read when the application starts.</param>
/// <param name="NotPersisted">Keys applied, but that settings.json could not take.</param>
internal sealed record SaveOutcome(IReadOnlyList<string> AwaitingRestart, IReadOnlyList<string> NotPersisted);

/// <summary>
/// The settings window: every value of settings.json, grouped in four pages,
/// without the user having to know a single key name.
///
/// <para>It edits a copy. Nothing reaches the running application or the
/// file until Save, and Cancel leaves both exactly as they were. The tray
/// does the applying (see <see cref="SettingsSubmission"/>): it knows which
/// parts of the application can take a new value on the fly.</para>
///
/// <para>Every range offered comes from the bounds
/// <see cref="AppSettings.Normalize"/> enforces, so the window cannot produce
/// a value the file would then correct in silence.</para>
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Windows Forms shell: requires an interactive session. The logic it relies on is tested in HotkeyCapture, SettingsDiff, SettingText and ModelLocator.")]
internal sealed class SettingsWindow : Form
{
    private static readonly int SidebarWidth = Dpi.S(210);
    private static readonly int ContentWidth = Dpi.S(570);
    private static readonly int FooterHeight = Dpi.S(64);
    private static readonly int PageMargin = Dpi.S(24);
    private static readonly int WindowHeight = Dpi.S(640);

    /// <summary>
    /// The shortest pause the window offers. Zero also turns the cutting off in
    /// the file, but the switch does that here; a slider reaching zero would be
    /// a second, unlabelled off switch.
    /// </summary>
    private const int MinimumPause = 100;

    private const string DefaultHotkeyHint = "Maintenir pour dicter, relâcher pour insérer.";

    private readonly Theme _theme = Theme.Current;
    private readonly AppSettings _edited;
    private readonly KeyboardHook _hook;
    private readonly Func<SettingsSubmission, SaveOutcome> _save;
    private readonly Icon _icon;

    private readonly List<(NavigationItem Item, Panel Page)> _pages = [];
    private readonly Panel _content;

    // --- Editors ------------------------------------------------------------

    private readonly Label _hotkeyLabel;
    private readonly RoundButton _hotkeyButton;
    private readonly Panel _hotkeyEditor;
    private readonly SettingRow _hotkeyRow;
    private readonly LabeledSlider _minRecording;
    private readonly LabeledSlider _maxRecording;
    private readonly LabeledSlider _pause;
    private readonly ToggleSwitch _segmentation;
    private readonly SegmentedControl _insertion;

    private readonly ToggleSwitch _showCircle;
    private readonly ToggleSwitch _playTone;
    private readonly SegmentedControl _colorMode;
    private readonly Button _colorSwatch;
    private readonly LabeledSlider _circleSize;
    private readonly LabeledSlider _circleOpacity;
    private readonly LabeledSlider _circleMargin;

    private readonly SettingRow _modelRow;
    private readonly LabeledSlider _threads;
    private readonly LabeledSlider _unloadAfter;

    private readonly ToggleSwitch _autoStart;
    private readonly ToggleSwitch _desktopShortcut;
    private readonly ToggleSwitch _logEnabled;

    /// <summary>Colour of the circle when it is fixed; kept while "auto" is chosen.</summary>
    private Color _fixedColor;

    private HotkeyCapture? _capture;

    public SettingsWindow(
        AppSettings current,
        bool autoStart,
        bool desktopShortcut,
        KeyboardHook hook,
        Func<SettingsSubmission, SaveOutcome> save,
        Action openSettingsFile,
        Action openLogFolder,
        Icon icon)
    {
        _edited = current.Clone();
        _hook = hook;
        _save = save;

        // A copy: the tray keeps using its own icon after this window, and a
        // form may dispose the icon it was given.
        _icon = (Icon)icon.Clone();

        SuspendLayout();

        // Scaled by hand through Dpi: see there for why not by Windows Forms.
        AutoScaleMode = AutoScaleMode.None;
        Text = "Paramètres de HexWin";
        Icon = _icon;
        Font = Theme.Body;
        BackColor = _theme.Window;
        ForeColor = _theme.Text;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(SidebarWidth + ContentWidth, WindowHeight);

        _fixedColor = HexColor.TryParse(_edited.FeedbackColor, out int rgb)
            ? Color.FromArgb(255, Color.FromArgb(rgb))
            : Color.FromArgb(224, 49, 49);

        // --- Dictation --------------------------------------------------------

        _hotkeyLabel = new Label
        {
            Font = Theme.Strong,
            ForeColor = _theme.Text,
            BackColor = _theme.Card,
            AutoSize = false,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleRight,
            Bounds = new Rectangle(0, 0, Dpi.S(170), Dpi.S(32)),
        };

        _hotkeyButton = new RoundButton(_theme, "Modifier") { Location = new Point(Dpi.S(178), 0) };
        _hotkeyButton.Click += (_, _) => ToggleCapture();

        _hotkeyEditor = new Panel { BackColor = _theme.Card, Size = new Size(Dpi.S(178) + _hotkeyButton.Width, Dpi.S(32)) };
        _hotkeyEditor.Controls.Add(_hotkeyLabel);
        _hotkeyEditor.Controls.Add(_hotkeyButton);

        _hotkeyRow = new SettingRow(_theme, "Raccourci de dictée", DefaultHotkeyHint, _hotkeyEditor);
        _hotkeyButton.AccessibleName = "Modifier le raccourci de dictée";

        _minRecording = new LabeledSlider(
            _theme, 0, AppSettings.MinRecordingMillisecondsCeiling, 50, _edited.MinRecordingMilliseconds, SettingText.Milliseconds);

        _maxRecording = new LabeledSlider(
            _theme, AppSettings.MaxRecordingSecondsFloor, AppSettings.MaxRecordingSecondsCeiling, 5, _edited.MaxRecordingSeconds, SettingText.Seconds);

        // A pause of zero in the file is the cutting turned off: it shows as the
        // switch off, with the slider on the default duration, ready for the
        // day the switch goes on.
        _segmentation = new ToggleSwitch(_theme) { Checked = _edited.Segmentation && _edited.PauseMilliseconds > 0 };

        _pause = new LabeledSlider(
            _theme,
            MinimumPause,
            AppSettings.MaxPauseMilliseconds,
            50,
            _edited.PauseMilliseconds > 0 ? _edited.PauseMilliseconds : new AppSettings().PauseMilliseconds,
            SettingText.Pause);

        _pause.SetActive(_segmentation.Checked);
        _segmentation.CheckedChanged += (_, _) => _pause.SetActive(_segmentation.Checked);

        _insertion = new SegmentedControl(_theme, "Coller", "Taper")
        {
            SelectedIndex = _edited.Insertion == InsertionMode.Type ? 1 : 0,
        };

        Panel dictation = NewPage(
            "Dictée",
            Section("Raccourci"),
            Card(_hotkeyRow),
            Section("Enregistrement"),
            Card(
                new SettingRow(_theme, "Durée minimale", "Plus bref, l'appui est ignoré.", _minRecording),
                new SettingRow(_theme, "Durée maximale", "Au-delà, l'enregistrement s'arrête.", _maxRecording)),
            Section("Insertion"),
            Card(
                new SettingRow(_theme, "Insertion du texte", "Coller est instantané ; Taper passe partout.", _insertion),
                new SettingRow(_theme, "Insérer phrase par phrase", "Sans attendre le relâchement, à chaque pause.", _segmentation),
                new SettingRow(_theme, "Pause qui coupe une phrase", "Plus courte : plus tôt, au risque de couper.", _pause)));

        // --- Feedback ---------------------------------------------------------

        var feedback = new FeedbackPolicy(_edited.Feedback);

        _showCircle = new ToggleSwitch(_theme) { Checked = feedback.ShowsCircle };
        _playTone = new ToggleSwitch(_theme) { Checked = feedback.PlaysTone };

        _colorMode = new SegmentedControl(_theme, "Selon l'état", "Fixe")
        {
            Size = new Size(Dpi.S(170), Dpi.S(30)),
            SelectedIndex = HexColor.TryParse(_edited.FeedbackColor, out _) ? 1 : 0,
        };

        _colorSwatch = new Button
        {
            FlatStyle = FlatStyle.Flat,
            Size = new Size(Dpi.S(44), Dpi.S(30)),
            Location = new Point(Dpi.S(178), 0),
            Cursor = Cursors.Hand,
            AccessibleName = "Choisir la couleur du cercle",
        };
        _colorSwatch.FlatAppearance.BorderColor = _theme.ControlBorder;
        _colorSwatch.Click += (_, _) => PickColor();
        _colorMode.SelectedIndexChanged += (_, _) => RefreshSwatch();

        var colorEditor = new Panel { BackColor = _theme.Card, Size = new Size(Dpi.S(222), Dpi.S(30)) };
        colorEditor.Controls.Add(_colorMode);
        colorEditor.Controls.Add(_colorSwatch);
        RefreshSwatch();

        _circleSize = new LabeledSlider(
            _theme, AppSettings.MinFeedbackSize, AppSettings.MaxFeedbackSize, 4, _edited.FeedbackSize, SettingText.Pixels);

        _circleOpacity = new LabeledSlider(
            _theme, AppSettings.MinFeedbackOpacity, 255, 5, _edited.FeedbackOpacity, SettingText.Opacity);

        _circleMargin = new LabeledSlider(
            _theme, 0, AppSettings.MaxFeedbackTopMargin, 10, _edited.FeedbackTopMargin, SettingText.Pixels);

        Panel cues = NewPage(
            "Retour visuel et sonore",
            Section("Pendant la dictée"),
            Card(
                new SettingRow(_theme, "Cercle à l'écran", "Rouge à l'enregistrement, orange à la transcription.", _showCircle),
                new SettingRow(_theme, "Son au début et à la fin", "Un bip bref à l'ouverture et à la fermeture du micro.", _playTone)),
            Section("Apparence du cercle"),
            Card(
                new SettingRow(_theme, "Couleur", "Selon l'état, ou une seule couleur.", colorEditor),
                new SettingRow(_theme, "Taille", null, _circleSize),
                new SettingRow(_theme, "Opacité", null, _circleOpacity),
                new SettingRow(_theme, "Marge en haut de l'écran", null, _circleMargin)));

        // --- Engine -----------------------------------------------------------

        var browse = new RoundButton(_theme, "Parcourir…");
        browse.Click += (_, _) => PickModel();
        _modelRow = new SettingRow(_theme, "Dossier du modèle", DescribeModel(), browse);
        browse.AccessibleName = "Choisir le dossier du modèle";

        _threads = new LabeledSlider(_theme, 1, AppSettings.MaxThreads, 1, _edited.Threads, SettingText.Threads);

        _unloadAfter = new LabeledSlider(
            _theme, 0, AppSettings.MaxUnloadAfterMinutes, 5, _edited.UnloadAfterMinutes, SettingText.IdleMinutes);

        Panel engine = NewPage(
            "Moteur",
            Note("Ces réglages prennent effet au prochain démarrage de HexWin."),
            Section("Modèle"),
            Card(_modelRow),
            Section("Performances"),
            Card(
                new SettingRow(_theme, "Fils de calcul", "Au-delà de quelques-uns, le gain s'effondre.", _threads),
                new SettingRow(_theme, "Libérer la mémoire après", "Le modèle occupe environ 1 Go.", _unloadAfter)));

        // --- General ----------------------------------------------------------

        _autoStart = new ToggleSwitch(_theme) { Checked = autoStart };
        _desktopShortcut = new ToggleSwitch(_theme) { Checked = desktopShortcut };
        _logEnabled = new ToggleSwitch(_theme) { Checked = _edited.LogEnabled };

        var openFile = new RoundButton(_theme, "settings.json");
        openFile.Click += (_, _) => openSettingsFile();

        var openLogs = new RoundButton(_theme, "Journaux") { Location = new Point(openFile.Width + Dpi.S(8), 0) };
        openLogs.Click += (_, _) => openLogFolder();

        var files = new Panel { BackColor = _theme.Card, Size = new Size(openFile.Width + Dpi.S(8) + openLogs.Width, Dpi.S(32)) };
        files.Controls.Add(openFile);
        files.Controls.Add(openLogs);

        Panel general = NewPage(
            "Général",
            Section("Démarrage"),
            Card(
                new SettingRow(_theme, "Lancer au démarrage de Windows", "HexWin se place dans la zone de notification.", _autoStart),
                new SettingRow(_theme, "Raccourci sur le bureau", "Démarre HexWin, ou rouvre cette fenêtre.", _desktopShortcut)),
            Section("Diagnostic"),
            Card(
                new SettingRow(_theme, "Journal des dictées", "Durée, niveau capté, caractères. Au prochain démarrage.", _logEnabled),
                new SettingRow(_theme, "Ouvrir", "Pour les réglages avancés ou un diagnostic.", files)));

        // --- Frame ------------------------------------------------------------

        _content = new Panel
        {
            Bounds = new Rectangle(SidebarWidth, 0, ContentWidth, WindowHeight - FooterHeight),
            BackColor = _theme.Window,
        };

        foreach (Panel page in new[] { dictation, cues, engine, general })
        {
            _content.Controls.Add(page);
        }

        Controls.Add(_content);
        Controls.Add(BuildSidebar(("Dictée", dictation), ("Retour", cues), ("Moteur", engine), ("Général", general)));
        Controls.Add(BuildFooter());

        RefreshHotkey();
        RefreshModel();
        ShowPage(_pages[0].Item);

        ResumeLayout(false);
    }

    // --- Layout -----------------------------------------------------------------

    private Panel NewPage(string title, params Control[] blocks)
    {
        var page = new Panel
        {
            Bounds = new Rectangle(0, 0, ContentWidth, WindowHeight - FooterHeight),
            BackColor = _theme.Window,
            AutoScroll = true,
            Visible = false,
        };

        var heading = new Label
        {
            Text = title,
            Font = Theme.Heading,
            ForeColor = _theme.Text,
            BackColor = _theme.Window,
            AutoSize = true,
            Location = new Point(PageMargin, Dpi.S(20)),
        };
        page.Controls.Add(heading);

        // Laid out once, top to bottom, at the widths the window was designed
        // for. Windows Forms scales the result for the screen's density.
        int top = Dpi.S(20) + heading.PreferredHeight + Dpi.S(12);
        int width = ContentWidth - (PageMargin * 2) - SystemInformation.VerticalScrollBarWidth;

        foreach (Control block in blocks)
        {
            block.Location = new Point(PageMargin, top);
            block.Width = width;

            if (block is Label label)
            {
                label.MaximumSize = new Size(width, 0);
                block.Height = label.PreferredHeight;
            }

            page.Controls.Add(block);
            top = block.Bottom + Dpi.S(block is SettingCard ? 16 : 6);
        }

        return page;
    }

    private Label Section(string text) => new()
    {
        Text = text,
        Font = Theme.Section,
        ForeColor = _theme.Text,
        BackColor = _theme.Window,
        AutoSize = false,
    };

    private Label Note(string text) => new()
    {
        Text = text,
        Font = Theme.Caption,
        ForeColor = _theme.SecondaryText,
        BackColor = _theme.Window,
        AutoSize = false,
    };

    private SettingCard Card(params SettingRow[] rows)
    {
        var card = new SettingCard(_theme) { Width = ContentWidth - (PageMargin * 2) };

        foreach (SettingRow row in rows)
        {
            card.AddRow(row);
        }

        return card;
    }

    private Panel BuildSidebar(params (string Title, Panel Page)[] entries)
    {
        var sidebar = new Panel
        {
            Bounds = new Rectangle(0, 0, SidebarWidth, WindowHeight),
            BackColor = _theme.Window,
        };

        var title = new Label
        {
            Text = "HexWin",
            Font = Theme.Heading,
            ForeColor = _theme.Text,
            BackColor = _theme.Window,
            AutoSize = true,
            Location = new Point(Dpi.S(20), Dpi.S(20)),
        };
        sidebar.Controls.Add(title);

        int top = Dpi.S(72);

        foreach ((string text, Panel page) in entries)
        {
            var item = new NavigationItem(_theme, text) { Bounds = new Rectangle(Dpi.S(12), top, SidebarWidth - Dpi.S(24), Dpi.S(36)) };
            item.Chosen += (_, _) => ShowPage(item);
            _pages.Add((item, page));
            sidebar.Controls.Add(item);
            top += Dpi.S(40);
        }

        string version = typeof(SettingsWindow).Assembly.GetName().Version?.ToString(3) ?? "";

        var about = new Label
        {
            Text = $"Version {version}\nDictée locale, rien ne sort de votre machine.",
            Font = Theme.Caption,
            ForeColor = _theme.SecondaryText,
            BackColor = _theme.Window,
            AutoSize = false,
            Bounds = new Rectangle(Dpi.S(20), WindowHeight - Dpi.S(64), SidebarWidth - Dpi.S(32), Dpi.S(48)),
        };
        sidebar.Controls.Add(about);

        return sidebar;
    }

    private Panel BuildFooter()
    {
        var footer = new Panel
        {
            Bounds = new Rectangle(SidebarWidth, WindowHeight - FooterHeight, ContentWidth, FooterHeight),
            BackColor = _theme.Window,
        };

        var save = new RoundButton(_theme, "Enregistrer", primary: true) { BackColor = _theme.Window };
        var cancel = new RoundButton(_theme, "Annuler") { BackColor = _theme.Window };

        save.Location = new Point(ContentWidth - PageMargin - save.Width, (FooterHeight - save.Height) / 2);
        cancel.Location = new Point(save.Left - Dpi.S(8) - cancel.Width, save.Top);

        save.Click += (_, _) => Save();
        cancel.Click += (_, _) => Close();

        CancelButton = cancel;

        footer.Controls.Add(save);
        footer.Controls.Add(cancel);

        return footer;
    }

    private void ShowPage(NavigationItem chosen)
    {
        foreach ((NavigationItem item, Panel page) in _pages)
        {
            bool selected = item == chosen;
            item.Selected = selected;
            page.Visible = selected;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        WindowChrome.Apply(Handle, _theme.IsDark);
    }

    // --- Shortcut capture -------------------------------------------------------

    private void ToggleCapture()
    {
        if (_capture is null)
        {
            StartCapture();
        }
        else
        {
            StopCapture();
        }
    }

    /// <summary>
    /// Hands the keyboard hook over to the capture. From here on every key is
    /// swallowed, so the capture must end as soon as it has what it needs, and
    /// whenever the window stops being the one the user is looking at.
    /// </summary>
    private void StartCapture()
    {
        _capture = new HotkeyCapture();
        ShowHotkeyText("Appuyez sur les touches…");
        _hotkeyLabel.ForeColor = _theme.Accent;
        _hotkeyButton.Text = "Annuler";
        _hotkeyRow.SetDescription("Maintenez-les, puis relâchez. Échap pour annuler.");

        _hook.BeginCapture(OnCapturedKey);
    }

    private void StopCapture()
    {
        if (_capture is null)
        {
            return;
        }

        _capture = null;
        _hook.EndCapture();
        _hotkeyButton.Text = "Modifier";
        RefreshHotkey();
    }

    /// <summary>
    /// Runs inside the keyboard hook callback: nothing here may block, and
    /// above all no dialog — Windows would uninstall the hook past its deadline.
    /// </summary>
    /// <returns>
    /// False to give the key back to Windows: the capture only takes keys
    /// while this window is the active one. Losing the focus normally ends it
    /// through <see cref="OnDeactivate"/>, but a window brought forward or sent
    /// back by another program does not always get that far — and a capture
    /// left running would swallow what the user types in the other window,
    /// then save it as their shortcut.
    /// </returns>
    private bool OnCapturedKey(int virtualKey, bool keyDown)
    {
        if (_capture is null)
        {
            return false;
        }

        if (ActiveForm != this)
        {
            StopCapture();
            return false;
        }

        CaptureStep step = keyDown ? _capture.OnKeyDown(virtualKey) : _capture.OnKeyUp(virtualKey);

        switch (step.State)
        {
            case CaptureState.Captured:
                _edited.Hotkey = [.. step.Keys];
                StopCapture();
                break;

            case CaptureState.Cancelled:
                StopCapture();
                break;

            case CaptureState.Unsupported:
                ShowHotkeyText("Appuyez sur les touches…");
                _hotkeyRow.SetDescription("Touche refusée : Maj, Ctrl, Alt, Windows, Verr. Maj, Espace ou F13 à F24.");
                break;

            case CaptureState.Listening:
            default:
                ShowHotkeyText(step.Keys.Count == 0
                    ? "Appuyez sur les touches…"
                    : HotkeyText.Describe(step.Keys) + " …");
                break;
        }

        return true;
    }

    /// <summary>
    /// Sizes the shortcut to its text. "F13" and "Ctrl gauche + Windows
    /// gauche" do not fit the same box, and an ellipsis would hide exactly the
    /// side of the keyboard the user needs to read.
    /// </summary>
    private void ShowHotkeyText(string text)
    {
        _hotkeyLabel.Text = text;

        int gap = Dpi.S(8);
        int available = Math.Max(Dpi.S(120), _hotkeyRow.Width / 2);
        int width = Math.Min(TextRenderer.MeasureText(text, _hotkeyLabel.Font).Width + Dpi.S(4), available);

        _hotkeyLabel.Width = width;
        _hotkeyButton.Left = width + gap;
        _hotkeyEditor.Width = width + gap + _hotkeyButton.Width;

        _hotkeyRow.PerformLayout();
    }

    private void RefreshHotkey()
    {
        ShowHotkeyText(HotkeyText.Describe(_edited.Hotkey));
        _hotkeyLabel.ForeColor = _theme.Text;
        _hotkeyRow.SetDescription(HotkeyText.Caveat(_edited.Hotkey) ?? DefaultHotkeyHint);
    }

    protected override void OnDeactivate(EventArgs e)
    {
        // A click elsewhere must hand the keyboard back at once: while the
        // capture runs, nothing the user types reaches any application.
        StopCapture();
        base.OnDeactivate(e);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        StopCapture();
        base.OnFormClosing(e);
    }

    // --- Circle colour ------------------------------------------------------------

    private void RefreshSwatch()
    {
        bool fixedColor = _colorMode.SelectedIndex == 1;

        // Hidden rather than greyed out: an empty square next to "Selon l'état"
        // reads as a colour that failed to load.
        _colorSwatch.Visible = fixedColor;
        _colorSwatch.BackColor = _fixedColor;
        _colorSwatch.FlatAppearance.MouseOverBackColor = _colorSwatch.BackColor;
    }

    private void PickColor()
    {
        using var dialog = new ColorDialog { Color = _fixedColor, FullOpen = true, AnyColor = true };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _fixedColor = dialog.Color;
            RefreshSwatch();
        }
    }

    // --- Model ----------------------------------------------------------------------

    /// <summary>
    /// The folder's own name: a full path does not fit the row, and its tail
    /// is what tells two models apart. The row's tooltip carries it whole.
    /// </summary>
    private string DescribeModel() =>
        ModelLocator.Resolve(_edited.ModelPath, AppContext.BaseDirectory) is { } found
            ? Path.GetFileName(Path.TrimEndingDirectorySeparator(found))
            : $"Introuvable : {_edited.ModelPath}";

    private void RefreshModel() =>
        _modelRow.SetDescription(
            DescribeModel(),
            ModelLocator.Resolve(_edited.ModelPath, AppContext.BaseDirectory) ?? _edited.ModelPath);

    /// <summary>
    /// Checks the folder before accepting it. A wrong folder only fails at the
    /// next start, in a dialog, with dictation unavailable — far too late to
    /// connect it to a choice made here.
    /// </summary>
    private void PickModel()
    {
        string baseDirectory = AppContext.BaseDirectory;
        string? current = ModelLocator.Resolve(_edited.ModelPath, baseDirectory);

        using var dialog = new FolderBrowserDialog
        {
            Description = "Dossier du modèle Parakeet",
            UseDescriptionForTitle = true,
            InitialDirectory = current ?? baseDirectory,
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            ParakeetEngine.Validate(dialog.SelectedPath);
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidOperationException or IOException)
        {
            MessageBox.Show(
                this,
                $"Ce dossier ne contient pas de modèle utilisable.\n\n{ex.Message}",
                "HexWin",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

            return;
        }

        // Picking the folder already in use changes nothing, even when the
        // file reaches it by a relative path the new one would not spell the
        // same way.
        if (current is not null
            && string.Equals(Path.GetFullPath(current), Path.GetFullPath(dialog.SelectedPath), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _edited.ModelPath = ModelLocator.ToConfiguredPath(dialog.SelectedPath, baseDirectory);
        RefreshModel();
    }

    // --- Saving -----------------------------------------------------------------------

    private void Save()
    {
        StopCapture();

        _edited.MinRecordingMilliseconds = _minRecording.Value;
        _edited.MaxRecordingSeconds = _maxRecording.Value;
        _edited.Segmentation = _segmentation.Checked;
        _edited.PauseMilliseconds = _pause.Value;
        _edited.Insertion = _insertion.SelectedIndex == 1 ? InsertionMode.Type : InsertionMode.Paste;

        _edited.Feedback = new FeedbackPolicy(FeedbackMode.None)
        {
            ShowsCircle = _showCircle.Checked,
            PlaysTone = _playTone.Checked,
        }.Mode;

        _edited.FeedbackColor = _colorMode.SelectedIndex == 1
            ? HexColor.ToHex(_fixedColor.ToArgb() & 0xFFFFFF)
            : "auto";

        _edited.FeedbackSize = _circleSize.Value;
        _edited.FeedbackOpacity = _circleOpacity.Value;
        _edited.FeedbackTopMargin = _circleMargin.Value;
        _edited.Threads = _threads.Value;
        _edited.UnloadAfterMinutes = _unloadAfter.Value;
        _edited.LogEnabled = _logEnabled.Checked;
        _edited.Normalize();

        SaveOutcome outcome = _save(new SettingsSubmission(_edited, _autoStart.Checked, _desktopShortcut.Checked));
        string? report = Describe(outcome);

        if (report is not null)
        {
            MessageBox.Show(this, report, "HexWin", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        Close();
    }

    private static string? Describe(SaveOutcome outcome)
    {
        List<string> paragraphs = [];

        if (outcome.NotPersisted.Count > 0)
        {
            paragraphs.Add(
                "Appliqué, mais settings.json n'a pas pu être mis à jour pour : "
                + string.Join(", ", outcome.NotPersisted.Select(SettingText.NameOf))
                + ".\nLe fichier est peut-être ouvert ailleurs ou en lecture seule ; "
                + "ces réglages seront perdus au prochain démarrage.");
        }

        IEnumerable<string> pending = outcome.AwaitingRestart.Except(outcome.NotPersisted);

        if (pending.Any())
        {
            paragraphs.Add(
                "Enregistré. Prendra effet au prochain démarrage de HexWin : "
                + string.Join(", ", pending.Select(SettingText.NameOf))
                + ".");
        }

        return paragraphs.Count == 0 ? null : string.Join("\n\n", paragraphs);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _icon.Dispose();
        }

        base.Dispose(disposing);
    }
}
