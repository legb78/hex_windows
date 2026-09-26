using System.Globalization;

namespace HexWin.Ui;

public sealed partial class UiStrings
{
    public static readonly UiStrings French = new()
    {
        Culture = CultureInfo.GetCultureInfo("fr-FR"),

        SettingNames = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["modelPath"] = "Dossier du modèle",
            ["hotkey"] = "Raccourci",
            ["minRecordingMilliseconds"] = "Durée minimale",
            ["maxRecordingSeconds"] = "Durée maximale",
            ["segmentation"] = "Insérer phrase par phrase",
            ["pauseMilliseconds"] = "Pause qui coupe une phrase",
            ["unloadAfterMinutes"] = "Libérer la mémoire après",
            ["provider"] = "Processeur de calcul",
            ["threads"] = "Fils de calcul",
            ["insertion"] = "Insertion du texte",
            ["feedback"] = "Cercle et son",
            ["feedbackColor"] = "Couleur du cercle",
            ["feedbackSize"] = "Taille du cercle",
            ["feedbackOpacity"] = "Opacité du cercle",
            ["feedbackTopMargin"] = "Marge du cercle",
            ["language"] = "Langue",
            ["logEnabled"] = "Journal des dictées",
        },

        KeyNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Ctrl"] = "Ctrl",
            ["LeftCtrl"] = "Ctrl gauche",
            ["RightCtrl"] = "Ctrl droit",
            ["Alt"] = "Alt",
            ["LeftAlt"] = "Alt gauche",
            ["RightAlt"] = "Alt Gr",
            ["Shift"] = "Maj",
            ["LeftShift"] = "Maj gauche",
            ["RightShift"] = "Maj droite",
            ["Win"] = "Windows",
            ["LeftWin"] = "Windows gauche",
            ["RightWin"] = "Windows droite",
            ["CapsLock"] = "Verr. Maj",
            ["Space"] = "Espace",
        },

        // French puts a space before the percent sign.
        Percent = value => $"{value} %",
        ThreadCount = value => value == 1 ? "1 fil" : $"{value} fils",
        Never = "Jamais",
        Off = "Désactivée",

        CaveatAltGr = "Sans doute AltGr : il ne tapera plus @, # ni [ tant que HexWin tourne.",
        CaveatSpace = "Espace seule ne tapera plus d'espaces tant que HexWin tourne.",
        CaveatBothShifts = "Aucune touche Maj ne servira plus aux majuscules.",
        CaveatLeftShift = "Utilisez Maj droite pour les majuscules.",
        CaveatRightShift = "Utilisez Maj gauche pour les majuscules.",
        CaveatCapsLock = "Verr. Maj ne verrouillera plus les majuscules.",

        WindowTitle = "Paramètres de HexWin",
        About = version => $"Version {version}\nDictée locale, rien ne sort de votre machine.",
        Save = "Enregistrer",
        Cancel = "Annuler",
        NavDictation = "Dictée",
        NavCues = "Retour",
        NavEngine = "Moteur",
        NavGeneral = "Général",

        PageDictation = "Dictée",
        SectionHotkey = "Raccourci",
        HotkeyTitle = "Raccourci de dictée",
        HotkeyHint = "Maintenir pour dicter, relâcher pour insérer.",
        Change = "Modifier",
        ChangeHotkeyName = "Modifier le raccourci de dictée",
        CapturePrompt = "Appuyez sur les touches…",
        CaptureHint = "Maintenez-les, puis relâchez. Échap pour annuler.",
        // French puts a space before the ellipsis.
        Holding = keys => $"{keys} …",
        CaptureRefused = "Touche refusée : Maj, Ctrl, Alt, Windows, Verr. Maj, Espace ou F13 à F24.",
        SectionRecording = "Enregistrement",
        MinimumLengthTitle = "Durée minimale",
        MinimumLengthHint = "Plus bref, l'appui est ignoré.",
        MaximumLengthTitle = "Durée maximale",
        MaximumLengthHint = "Au-delà, l'enregistrement s'arrête.",
        SectionInsertion = "Insertion",
        InsertionTitle = "Insertion du texte",
        InsertionHint = "Coller est instantané ; Taper passe partout.",
        InsertPaste = "Coller",
        InsertType = "Taper",
        SegmentationTitle = "Insérer phrase par phrase",
        SegmentationHint = "Sans attendre le relâchement, à chaque pause.",
        PauseTitle = "Pause qui coupe une phrase",
        PauseHint = "Plus courte : plus tôt, au risque de couper.",

        PageCues = "Retour visuel et sonore",
        SectionWhileDictating = "Pendant la dictée",
        CircleTitle = "Cercle à l'écran",
        CircleHint = "Rouge à l'enregistrement, orange à la transcription.",
        ToneTitle = "Son au début et à la fin",
        ToneHint = "Un bip bref à l'ouverture et à la fermeture du micro.",
        SectionCircle = "Apparence du cercle",
        ColorTitle = "Couleur",
        ColorHint = "Selon l'état, ou une seule couleur.",
        ColorByState = "Selon l'état",
        ColorFixed = "Fixe",
        PickColorName = "Choisir la couleur du cercle",
        SizeTitle = "Taille",
        OpacityTitle = "Opacité",
        TopMarginTitle = "Marge en haut de l'écran",

        PageEngine = "Moteur",
        EngineNote = "Ces réglages prennent effet au prochain démarrage de HexWin.",
        SectionModel = "Modèle",
        ModelTitle = "Dossier du modèle",
        Browse = "Parcourir…",
        BrowseModelName = "Choisir le dossier du modèle",
        ModelNotFound = path => $"Introuvable : {path}",
        ModelFolderDialog = "Dossier du modèle Parakeet",
        ModelUnusable = reason => $"Ce dossier ne contient pas de modèle utilisable.\n\n{reason}",
        SectionPerformance = "Performances",
        ThreadsTitle = "Fils de calcul",
        ThreadsHint = "Au-delà de quelques-uns, le gain s'effondre.",
        UnloadTitle = "Libérer la mémoire après",
        UnloadHint = "Le modèle occupe environ 1 Go.",

        PageGeneral = "Général",
        SectionStartup = "Démarrage",
        AutoStartTitle = "Lancer au démarrage de Windows",
        AutoStartHint = "HexWin se place dans la zone de notification.",
        DesktopShortcutTitle = "Raccourci sur le bureau",
        DesktopShortcutHint = "Démarre HexWin, ou rouvre cette fenêtre.",
        SectionDisplay = "Affichage",
        LanguageTitle = "Langue",
        LanguageHint = "Pas celle de la dictée.",
        LanguageAuto = "Auto",
        SectionDiagnostics = "Diagnostic",
        LogTitle = "Journal des dictées",
        LogHint = "Durée, niveau capté, caractères. Au prochain démarrage.",
        OpenTitle = "Ouvrir",
        OpenHint = "Pour les réglages avancés ou un diagnostic.",
        Logs = "Journaux",

        NotPersisted = names =>
            $"Appliqué, mais settings.json n'a pas pu être mis à jour pour : {names}.\n"
            + "Le fichier est peut-être ouvert ailleurs ou en lecture seule ; "
            + "ces réglages seront perdus au prochain démarrage.",
        AwaitingRestart = names => $"Enregistré. Prendra effet au prochain démarrage de HexWin : {names}.",

        MenuSettings = "Paramètres…",
        MenuOpenSettingsFile = "Ouvrir settings.json",
        MenuOpenLogFolder = "Ouvrir le dossier des journaux",
        MenuShowCircle = "Afficher le cercle pendant la dictée",
        MenuPlayTone = "Jouer un son au début et à la fin",
        MenuStartWithWindows = "Lancer au démarrage de Windows",
        MenuQuit = "Quitter",

        TipLoading = "HexWin — chargement du modèle...",
        TipReady = hotkey => $"HexWin — prêt ({hotkey})",
        TipRecording = "HexWin — enregistrement",
        TipTranscribing = "HexWin — transcription...",
        TipModelMissing = "HexWin — modèle introuvable",

        BalloonModelMissing = "Modèle introuvable",
        BalloonModelMissingBody = reason => $"{reason}\n\nLancez scripts/get-model.ps1.",
        BalloonMicrophone = "Micro indisponible",
        MicrophoneMissing = "Aucun microphone détecté. Vérifiez qu'un périphérique d'entrée est "
            + "branché et autorisé dans Paramètres > Confidentialité > Microphone.",
        BalloonCannotOpen = "Ouverture impossible",
        BalloonShortcutFailed = "Raccourci impossible",
        BalloonShortcutFailedBody = "Le raccourci n'a pas pu être créé sur le bureau.",
        DesktopShortcutQuestion = "Ajouter un raccourci HexWin sur le bureau ?\n\n"
            + "Il démarre HexWin, ou ouvre ses paramètres quand HexWin tourne déjà. "
            + "Vous pourrez le retirer dans Paramètres, page Général.",
        DesktopShortcutDescription = "HexWin — dictée vocale locale. Ouvre les paramètres quand HexWin tourne déjà.",

        StartupModelMissing = path => $"Modèle introuvable : {path}\n\nLancez scripts/get-model.ps1 pour le télécharger.",
        ModelFileMissing = path => $"Fichier de modèle manquant : {path}.",
        Crashed = (reason, log) => $"HexWin s'est arrêté sur une erreur :\n\n{reason}\n\nDétails dans {log}",
        CrashLogUnavailable = "(journal inaccessible)",
    };
}
