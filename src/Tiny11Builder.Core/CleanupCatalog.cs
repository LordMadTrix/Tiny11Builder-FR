namespace Tiny11Builder.Core;

public static class CleanupCatalog
{
    public static IReadOnlyList<CleanupOption> Options { get; } =
    [
        App("copilot-app", "Applications Copilot", "Supprime les applications Copilot préinstallées (Windows 11 23H2 / 24H2). Conserve les composants partagés et le shell.", true, true,
            "Microsoft.Copilot", "Microsoft.Windows.Copilot", "Microsoft.Windows.Ai.Copilot.Provider", "Microsoft.549981C3F5F10"),
        App("outlook", "Nouvel Outlook et Courrier", "Supprime Outlook ainsi que les anciennes applications Courrier et Calendrier.", true, true,
            "Microsoft.OutlookForWindows", "microsoft.windowscommunicationsapps"),
        App("teams", "Microsoft Teams", "Supprime les clients Teams personnels.", true, true, "MSTeams", "MicrosoftTeams", "Microsoft.Windows.Teams"),
        App("xbox", "Xbox et barre de jeu", "Supprime les applications Xbox et la barre de jeu. La connexion Xbox et l'enregistrement peuvent être indisponibles.", true, true,
            "Microsoft.GamingApp", "Microsoft.XboxApp", "Microsoft.Xbox.TCUI", "Microsoft.XboxGameOverlay",
            "Microsoft.XboxGamingOverlay", "Microsoft.XboxIdentityProvider", "Microsoft.XboxSpeechToTextOverlay"),
        App("news-weather", "Actualités, Météo et Bing", "Supprime les flux d'actualités Bing et l'application d'accueil Widgets StartExperiences.", true, true,
            "Microsoft.BingNews", "Microsoft.BingWeather", "Microsoft.BingSearch", "Microsoft.StartExperiencesApp"),
        App("office-promos", "Promotions Microsoft 365", "Supprime le Hub Office et les notifications promotionnelles Office.", true, true,
            "Microsoft.MicrosoftOfficeHub", "Microsoft.OfficePushNotificationUtility"),
        App("clipchamp", "Clipchamp", "Supprime l'éditeur vidéo préinstallé Clipchamp.", true, true, "Clipchamp.Clipchamp"),
        App("solitaire", "Collection Solitaire", "Supprime les jeux préinstallés Microsoft Solitaire.", true, true, "Microsoft.MicrosoftSolitaireCollection"),
        App("phone", "Mobile connecté et multi-appareils", "Supprime les applications de synchronisation téléphonique. Les fonctions multi-appareils peuvent cesser de fonctionner.", true, true,
            "Microsoft.YourPhone", "Microsoft.Windows.CrossDevice"),
        App("feedback", "Commentaires, Astuces et Aide", "Supprime le Hub de commentaires, Obtenir de l'aide et les applications de découverte.", true, true,
            "Microsoft.WindowsFeedbackHub", "Microsoft.GetHelp", "Microsoft.Getstarted"),
        App("devhome", "Dev Home et Power Automate", "Supprime Dev Home et Power Automate Desktop.", true, true,
            "Microsoft.Windows.DevHome", "Microsoft.Windows.DevHomeGitHubExtension", "Microsoft.Windows.DevHomeAzureExtension", "Microsoft.PowerAutomateDesktop"),
        App("social", "Contacts, To Do, Famille et Skype", "Supprime les applications facultatives d'organisation et de communication.", true, true,
            "Microsoft.People", "Microsoft.Todos", "Microsoft.SkypeApp", "MicrosoftCorporationII.MicrosoftFamily"),
        App("maps", "Cartes et Réalité mixte", "Supprime Cartes et Portail de réalité mixte (Réalité mixte déjà retirée nativement sur 24H2+).", true, true,
            "Microsoft.WindowsMaps", "Microsoft.MixedReality.Portal"),
        App("media", "Lecteur multimédia et Films & TV", "Supprime les lecteurs multimédias préinstallés ; les codecs et bibliothèques partagées sont conservés.", false, true,
            "Microsoft.ZuneMusic", "Microsoft.ZuneVideo"),
        App("utilities", "Horloge, Enregistreur vocal et Pense-bêtes", "Supprime les utilitaires personnels facultatifs.", false, true,
            "Microsoft.WindowsAlarms", "Microsoft.WindowsSoundRecorder", "Microsoft.MicrosoftStickyNotes"),
        App("quickassist", "Assistance rapide", "Supprime l'utilitaire d'assistance à distance Microsoft.", false, true, "MicrosoftCorporationII.QuickAssist"),
        App("paint", "Paint, Visionneuse 3D et OneNote", "Supprime Paint, Visionneuse 3D et OneNote. Décochez pour les conserver.", false, false,
            "Microsoft.Paint", "Microsoft.MSPaint", "Microsoft.Microsoft3DViewer", "Microsoft.Office.OneNote"),
        App("camera", "Caméra", "Supprime l'application Caméra (les pilotes de caméra sont conservés).", false, false, "Microsoft.WindowsCamera"),
        App("terminal", "Windows Terminal", "Supprime Terminal. PowerShell et l'Invite de commandes restent disponibles.", false, false, "Microsoft.WindowsTerminal"),
        App("oem", "Applications promotionnelles OEM", "Supprime les applications promotionnelles Intel et Dolby (présentes uniquement sur images constructeurs).", false, false,
            "AppUp.IntelManagementandSecurityStatus", "DolbyLaboratories.DolbyAccess", "DolbyLaboratories.DolbyDigitalPlusDecoderOEM"),
        new("edge-files", "Supprimer Edge et WebView2", "Supprime les fichiers Edge et WebView2. Les apps dépendant de WebView2 peuvent ne plus fonctionner. Les mises à jour peuvent réinstaller Edge.",
            "Avancé / Outil d'origine", OptionKind.File, false, false, []),
        new("onedrive-files", "Supprimer les fichiers d'installation OneDrive", "Supprime les installeurs OneDrive 32 et 64 bits de l'image hors-ligne.",
            "Avancé / Outil d'origine", OptionKind.File, false, false, []),
        new("language-extras", "Reconnaissance vocale, OCR et écriture manuscrite", "Supprime les composants linguistiques facultatifs dans chaque langue ; conserve le support linguistique de base.",
            "Fonctionnalités facultatives", OptionKind.Capability, false, false,
            ["Language.Handwriting", "Language.OCR", "Language.Speech", "Language.TextToSpeech"]),
        new("legacy-capabilities", "Accessoires hérités et fonds d'écran étendus", "Supprime WordPad, Enregistreur d'actions, Math Recognizer, Lecteur Windows Media classique et fonds d'écran étendus.",
            "Fonctionnalités facultatives", OptionKind.Capability, false, true,
            ["Microsoft.Windows.WordPad", "App.StepsRecorder", "MathRecognizer", "Media.WindowsMediaPlayer", "Microsoft.Windows.Wallpaper.Extended"]),
        Feature("xps", "Impression XPS", "Désactive l'impression XPS facultative et supprime ses fichiers.", false, true, "Printing-XPSServices-Features"),
        Feature("workfolders", "Dossiers de travail (Work Folders)", "Désactive la synchronisation Work Folders d'entreprise et supprime ses fichiers.", false, true, "WorkFolders-Client"),
        Feature("legacy", "Internet Explorer hérité", "Désactive la fonctionnalité optionnelle Internet Explorer (retirée nativement sur Windows 11 24H2+).", true, true,
            "Internet-Explorer-Optional-amd64", "Internet-Explorer-Optional-arm64"),
        Feature("recall-feature", "Fonctionnalité Recall (IA)", "Désactive et supprime la charge utile de Recall si l'image l'expose à DISM.", true, true, "Recall", "Microsoft.Windows.Recall"),
        Policy("ads", "Publicités, recommandations et applications sponsorisées", "Désactive le contenu commercial, les suggestions des Paramètres et les promotions de l'Explorateur.", true, true),
        Policy("privacy", "Diagnostics optionnels et personnalisation", "Conserve les diagnostics minimaux. Désactive l'identifiant de pub, les suggestions personnalisées et la télémétrie de saisie.", true, true),
        Policy("copilot", "Désactiver Copilot et Recall", "Applique les stratégies pour désactiver Copilot et Recall.", true, true),
        Policy("widgets", "Widgets et flux de la barre des tâches", "Désactive les Widgets par stratégie de groupe et masque leur icône.", true, true),
        Policy("search", "Recherche web et suggestions Bing", "Désactive les suggestions web, la recherche cloud et les contenus dynamiques ; conserve la recherche locale.", true, true),
        Policy("onedrive", "Synchronisation et invites OneDrive", "Désactive la synchronisation OneDrive par stratégie. Les exécutables sont conservés si non supprimés.", true, true),
        Policy("edge", "Écrans d'accueil et bannières promotionnelles Edge", "Désactive le premier lancement, la barre latérale et les recommandations Edge tout en préservant WebView2.", true, true),
        Policy("oobe", "Écrans de confidentialité et suggestions OOBE", "Supprime les étapes invasives de fin de configuration post-installation sans accepter le CLUF à votre place.", true, true),
        Policy("cleanup", "Nettoyage des composants de l'image", "Le mode standard préserve la maintenance. Le mode Core réinitialise la base de composants avant l'élagage.", true, true),
        Policy("hardware-bypass", "Contourner les prérequis matériels (TPM, CPU, RAM)", "Injecte les clés de contournement CPU, RAM, TPM, stockage et Secure Boot dans l'installeur et Windows.", false, false),
        Policy("reserved-storage", "Désactiver le stockage réservé", "Désactive la réservation d'espace disque pour les mises à jour.", false, false),
        Policy("device-encryption", "Désactiver le chiffrement automatique BitLocker", "Empêche l'activation automatique du chiffrement de disque BitLocker sur les périphériques compatibles.", false, false)
    ];

    private static CleanupOption App(string id, string title, string description, bool balanced, bool minimal, params string[] targets)
        => new(id, title, description, "Applications intégrées", OptionKind.App, balanced, minimal, targets);
    private static CleanupOption Feature(string id, string title, string description, bool balanced, bool minimal, params string[] targets)
        => new(id, title, description, "Fonctionnalités facultatives", OptionKind.Feature, balanced, minimal, targets);
    private static CleanupOption Policy(string id, string title, string description, bool balanced, bool minimal)
        => new(id, title, description, "Paramètres Windows", OptionKind.Policy, balanced, minimal, []);

    public static string[] ForPreset(CleanupPreset preset) => Options
        .Where(o => preset switch
        {
            CleanupPreset.Balanced => o.Balanced,
            CleanupPreset.Minimal => o.Minimal,
            CleanupPreset.Original => o.Kind == OptionKind.App || o.Id is not ("language-extras" or "legacy-capabilities"),
            _ => false
        })
        .Select(o => o.Id).ToArray();

    public static string[] ForMode(BuildMode mode) => mode switch
    {
        BuildMode.Tiny11 => ForPreset(CleanupPreset.Original),
        BuildMode.Tiny11Core => Options.Where(o => o.Minimal || o.Id is
            "language-extras" or "edge-files" or "onedrive-files" or
            "hardware-bypass" or "reserved-storage" or "device-encryption")
            .Select(o => o.Id).ToArray(),
        BuildMode.Custom => [],
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };

    public static BuildMode ModeForRecipe(BuildRecipe recipe)
    {
        if (recipe.Mode is { } mode) return mode;
        var preset = recipe.Flavor == BuildFlavor.Core ? BuildMode.Tiny11Core : BuildMode.Tiny11;
        return recipe.ExpertActions.Length == 0 &&
            recipe.SelectedOptions.ToHashSet(StringComparer.Ordinal).SetEquals(ForMode(preset))
            ? preset : BuildMode.Custom;
    }

    public static bool MatchesApp(CleanupOption option, ProvisionedApp app) =>
        option.Kind == OptionKind.App && option.Targets.Any(target =>
            (app.PackageName.StartsWith(target + "_", StringComparison.OrdinalIgnoreCase) ||
             app.PackageName.StartsWith(target + "~", StringComparison.OrdinalIgnoreCase) ||
             app.PackageName.Equals(target, StringComparison.OrdinalIgnoreCase)) &&
            (app.DisplayName.Equals(target, StringComparison.OrdinalIgnoreCase) ||
             app.DisplayName.StartsWith(target, StringComparison.OrdinalIgnoreCase) ||
             target.StartsWith(app.DisplayName, StringComparison.OrdinalIgnoreCase) ||
             string.IsNullOrWhiteSpace(app.DisplayName) ||
             app.DisplayName.Equals("Copilot", StringComparison.OrdinalIgnoreCase) ||
             app.DisplayName.Equals("Teams", StringComparison.OrdinalIgnoreCase) ||
             app.DisplayName.Equals("Microsoft Teams", StringComparison.OrdinalIgnoreCase)));
}
