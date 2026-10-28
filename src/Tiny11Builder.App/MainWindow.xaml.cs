using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Collections.Concurrent;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.Storage.Pickers;
using Windows.ApplicationModel.DataTransfer;
using Tiny11Builder.Core;

namespace Tiny11Builder.App;

public sealed partial class MainWindow : Window
{
    private readonly BuildEngine engine = new(new ProcessRunner(), new MediaSourceFactory(),
        new OfflineRegistry(), new ImageOperations(), new BuildHost());
    private readonly List<OptionChoice> options = CleanupCatalog.Options.Select(o => new OptionChoice(o)).ToList();
    private List<InventoryChoice> inventory = [];
    private ExpertAction[] pendingActions = [];
    private int? pendingImageIndex;
    private string pendingSourcePath = "";
    private WindowsImage? image;
    private int step;
    private int reached;
    private bool initialized;
    private bool navigating;
    private bool applyingMode;
    private bool busy;
    private bool completed;
    private bool coreAcknowledged;
    private bool modalOpen;
    private enum NoticeAction { None, OpenLogs, CopyDetails, OpenTools, OpenOfficialDownload }
    private string officialDownloadPage = OfficialImageDownloader.DownloadPage;
    private OperationRun? active;
    private CancellationFlow? cancelFlow;
    private OperationKind running => active?.Kind ?? OperationKind.Other;
    private bool cancelRequested => active?.CancelRequested == true;
    private OperationKind? stoppedRun;
    private bool stoppedHasCheckpoint;
    private string? resumeJobFile;
    private string servicingTool = ToolLocator.FindDism();
    private string isoTool = ToolLocator.FindOscdimg();
    private readonly BuildStageBoard board = new();
    private BuildResult? result;
    private string? workDirectory;
    private readonly LiveLog liveLog = new();
    private ScrollViewer? logScroller;
    private readonly ConcurrentQueue<BuildProgress> progressUpdates = new();
    private readonly LatestProgress<DownloadProgress> downloadUpdates = new();
    private Func<DownloadProgress, string>? downloadText;
    private readonly DispatcherTimer progressTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly Windows.UI.ViewManagement.AccessibilitySettings accessibility = new();
    private bool? appliedContrast;
    private NoticeAction noticeAction;
    private string noticeDetails = "";
    private int activePhase = -1;
    private readonly string dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tiny11Builder");

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleArea);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1200, 900));
        AppWindow.Closing += (_, args) =>
        {
            if (!busy) return;
            args.Cancel = true;
            ShowNotice("Une opération est toujours en cours.", cancelRequested ? "Elle est en cours d'annulation. Veuillez patienter avant de fermer." :
                $"Choisissez {CancelPrompts.ButtonLabel} et attendez la fin de l'opération d'image avant de fermer.", InfoBarSeverity.Warning);
        };
        WorkspaceBox.Text = @"C:\tiny11";
        SupportPurpose.Text = ProjectSupport.Purpose;
        OutputBox.Text = Path.Combine(WorkspaceBox.Text, $"tiny11-{DateTime.Now:yyyyMMdd-HHmmss}.iso");
        BuildStages.ItemsSource = board.Stages;
        progressTimer.Tick += (_, _) => { FlushBuildProgress(); DrainDownload(); };
        Root.ActualThemeChanged += (_, _) => ApplyContrast();
        Activated += (_, _) => ApplyContrast();
        Closed += (_, _) => progressTimer.Stop();
        LatestDownloadDescription.Text = $"ISO officiel Microsoft · {HostArchitecture.ToUpperInvariant()} · {CultureInfo.CurrentUICulture.NativeName}";
        var defaultDefenderControl = @"C:\Users\madtr\Downloads\Defender-Control-v2.1\Defender Control";
        if (Directory.Exists(defaultDefenderControl)) DefenderControlPathBox.Text = defaultDefenderControl;
        foreach (var choice in options) choice.PropertyChanged += (_, _) => UpdateCounts();
        LocalAccountCheck.Toggled += (_, _) => { LocalAccountSettingsPanel.Visibility = LocalAccountCheck.IsOn ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed; };
        initialized = true;
        ApplyContrast();
        ApplyMode();
        FilterOptions();
        GoStep(0);
        var args = Environment.GetCommandLineArgs();
        var restoreIndex = Array.IndexOf(args, "--restore");
        if (restoreIndex >= 0 && restoreIndex + 1 < args.Length)
        {
            try
            {
                var recipe = JsonSerializer.Deserialize<BuildRecipe>(File.ReadAllText(args[restoreIndex + 1]), JsonDefaults.Options)
                    ?? throw new InvalidDataException("La session enregistrée est vide.");
                RestoreRecipe(recipe);
                ShowNotice("Session restaurée.", "Inspectez à nouveau l'image avant de continuer. Aucune création ne démarre automatiquement.");
            }
            catch (Exception error) { ShowNotice("La session n'a pas pu être restaurée.", ErrorText.Reason(error), InfoBarSeverity.Error); }
        }
        UpdateCounts();
    }

    private FrameworkElement[] Panels => [WelcomePanel, SourcePanel, CleanupPanel, SettingsPanel, ReviewPanel, BuildPanel];
    private BuildMode SelectedMode => (BuildMode)ModeChoice.SelectedIndex;
    private bool UsesCore => SelectedMode == BuildMode.Tiny11Core ||
        (SelectedMode == BuildMode.Custom && CustomCoreCheck.IsOn);
    private static string HostArchitecture => RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";

    private void OnContentSizeChanged(object sender, SizeChangedEventArgs args) => UpdateWelcomeLayout();

    private void OnContentViewChanged(object sender, ScrollViewerViewChangedEventArgs args)
    {
        StepNumber.Opacity = 0.10 * Math.Clamp(1 - ContentScroller.VerticalOffset / 32, 0, 1);
    }

    private void UpdateWelcomeLayout()
    {
        if (WelcomePanel is null || ModeCards is null || RemovalModeCards is null || ReviewCards is null || SourceCards is null) return;
        var padding = ContentScroller.Padding;
        WelcomePanel.MinHeight = Math.Max(0, ContentScroller.ActualHeight - padding.Top - padding.Bottom);
        var stacked = ContentScroller.ActualWidth - padding.Left - padding.Right < 560;
        ArrangeModeCards(ModeCards, [RegularModeCard, CoreModeCard, CustomModeCard], stacked);
        ArrangeModeCards(RemovalModeCards, [Tiny11Selection, CoreSelection, CustomSelection], stacked);
        ArrangeSourceCards(ContentScroller.ActualWidth - padding.Left - padding.Right);
        ReviewCards.ColumnDefinitions[1].Width = stacked ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        FrameworkElement[] review = [ReviewImageCard, ReviewModeCard, ReviewPathsCard, ReviewOptionsCard];
        for (var index = 0; index < review.Length; index++)
        {
            ReviewCards.RowDefinitions[index].Height = stacked || index < 2 ? GridLength.Auto : new GridLength(0);
            Grid.SetColumn(review[index], stacked ? 0 : index % 2);
            Grid.SetRow(review[index], stacked ? index : index / 2);
        }
    }

    private void ArrangeSourceCards(double width)
    {
        var wide = width >= 960;
        SourceCards.ColumnDefinitions[1].Width = wide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        SourceCards.RowDefinitions[1].Height = wide ? new GridLength(0) : GridLength.Auto;
        Grid.SetColumn(LocalMediaCard, wide ? 1 : 0);
        Grid.SetRow(LocalMediaCard, wide ? 0 : 1);
    }

    private static void ArrangeModeCards(Grid container, FrameworkElement[] cards, bool stacked)
    {
        container.ColumnSpacing = stacked ? 0 : 16;
        container.RowSpacing = stacked ? 12 : 0;
        for (var index = 0; index < cards.Length; index++)
        {
            container.ColumnDefinitions[index].Width = index == 0 || !stacked ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            container.RowDefinitions[index].Height = stacked ? GridLength.Auto :
                index == 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            Grid.SetColumn(cards[index], stacked ? 0 : index);
            Grid.SetRow(cards[index], stacked ? index : 0);
        }
    }

    private void GoStep(int value)
    {
        var focused = CurrentFocus();
        step = value;
        reached = Math.Max(reached, value);
        for (var i = 0; i < Panels.Length; i++) Panels[i].Visibility = i == value ? Visibility.Visible : Visibility.Collapsed;
        navigating = true;
        StepRail.SelectedIndex = value;
        navigating = false;
        StepNumber.Text = (value + 1).ToString(CultureInfo.InvariantCulture);
        StepNumber.Opacity = 0.10;
        UpdateWelcomeLayout();
        ContentScroller.ChangeView(null, 0, null, disableAnimation: true);
        if (value == 4) UpdateReview();
        if (value == 5) UpdateBuildDescription(CreateRecipe());
        UpdateNavigation();
        DispatcherQueue.TryEnqueue(() => RestoreFocus(focused));
    }

    private void UpdateNavigation()
    {
        if (!initialized) return;
        BackButton.IsEnabled = !busy && step > 0 && !completed;
        BackButton.Visibility = step > 0 && !completed ? Visibility.Visible : Visibility.Collapsed;
        var customCount = inventory.Count > 0 ? inventory.Count(i => i.Selected) : pendingActions.Length;
        var canAdvance = step switch
        {
            1 => image?.IsSupported == true,
            2 => customCount == 0 || ExpertConsent.IsChecked == true,
            _ => true
        };
        NextButton.IsEnabled = !busy && canAdvance;
        var retry = step == 5 && !completed && stoppedRun is not null;
        ActionLabel.Text = completed ? "En créer un autre" : retry ? (stoppedRun == OperationKind.Resume ? "Reprendre à nouveau" : "Réessayer") : step switch
        {
            0 => "Démarrer", 1 => "Suivant", 2 => "Suivant", 3 => "Vérifier", 4 => "Continuer", _ => "Créer"
        };
        ActionLabel.FontSize = 32;
        Grid.SetColumn(NextButton, 2);
        NextButton.HorizontalAlignment = HorizontalAlignment.Right;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(NextButton, ActionLabel.Text);
        StepRail.IsEnabled = !busy && !completed;
        RecoverButton.IsEnabled = !busy;
        SupportButton.IsEnabled = !busy;
        ResumeButton.IsEnabled = !busy;
        ThemeChoice.IsEnabled = !busy && !HighContrast;
        if (AccentColorChoice is not null) AccentColorChoice.IsEnabled = !busy && !HighContrast;
        ResumeRunButton.Visibility = !busy && step == 5 && !completed && stoppedRun == OperationKind.Build && stoppedHasCheckpoint ?
            Visibility.Visible : Visibility.Collapsed;
        CancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.IsEnabled = busy && !cancelRequested;
        if (busy)
        {
            var copy = CancelPrompts.For(running);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(CancelButton, copy.Hint);
            ToolTipService.SetToolTip(CancelButton, copy.Hint);
        }
        CancelHint.Visibility = busy && step == 5 && (running is OperationKind.Build or OperationKind.Resume) ? Visibility.Visible : Visibility.Collapsed;
        ContentScroller.IsEnabled = !busy || (step == 5 && running is OperationKind.Build or OperationKind.Resume);
    }

    private void OnRailChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!initialized || navigating || StepRail.SelectedIndex < 0) return;
        var next = StepRail.SelectedIndex;
        if (busy || next > reached || next > step)
        {
            navigating = true;
            StepRail.SelectedIndex = step;
            navigating = false;
            return;
        }
        GoStep(next);
    }

    private void OnBack(object sender, RoutedEventArgs args) { if (!busy && !modalOpen && step > 0) GoStep(step - 1); }

    private void OnModeCardClick(object sender, RoutedEventArgs args)
    {
        if (busy || modalOpen) return;
        if (sender is not Button { Tag: string tag } || !Enum.TryParse<BuildMode>(tag, out var mode) || !Enum.IsDefined(mode))
        {
            ShowNotice("Le mode de création n'a pas pu être sélectionné.", "La carte sélectionnée ne correspond pas à un mode valide.", InfoBarSeverity.Error);
            return;
        }
        ModeChoice.SelectedIndex = (int)mode;
        GoStep(1);
    }

    private void OnRemovalModeClick(object sender, RoutedEventArgs args)
    {
        if (!busy && sender is Button { Tag: string tag } && Enum.TryParse<BuildMode>(tag, out var mode))
            ModeChoice.SelectedIndex = (int)mode;
    }

    private async void OnNext(object sender, RoutedEventArgs args)
    {
        if (busy || modalOpen) return;
        if (completed)
        {
            completed = false;
            coreAcknowledged = false;
            UpdateModePresentation();
            OutputBox.Text = Path.Combine(Path.GetDirectoryName(OutputBox.Text)!, $"tiny11-{DateTime.Now:yyyyMMdd-HHmmss}.iso");
            ResetBuildPage();
            GoStep(1);
            return;
        }
        if (step == 5)
        {
            await (stoppedRun == OperationKind.Resume && resumeJobFile is { } job ? ResumeJobAsync(job) : StartBuildAsync());
            return;
        }
        if (step == 2 && UsesCore && !coreAcknowledged)
        {
            coreAcknowledged = await Confirm("Tiny11 Core n'est pas maintenable",
                "Pas de Windows Update\nPas de Microsoft Defender\nPas d'environnement de récupération\nImpossible d'ajouter des langues ou fonctionnalités ultérieurement\n\n" +
                "À utiliser exclusivement dans une VM de test isolée.\nL'image pourrait ne pas démarrer.",
                "J'accepte");
            CoreConsentStatus.Text = coreAcknowledged ? "Limitations de Core acceptées pour cette création." : "Les limitations de Core n'ont pas été acceptées.";
            if (!coreAcknowledged) return;
        }
        await RunOperation(OperationKind.Check, async token =>
        {
            Working("Vérification de vos choix…");
            if (step >= 1 && image is null) throw new InvalidOperationException("Analysez d'abord l'image Windows sélectionnée.");
            image?.Validate();
            if (step >= 2) CreateRecipe().ValidateOptions();
            if (step >= 3)
            {
                await EnsureToolsAsync(token);
                var recipe = CreateRecipe();
                PathSafety.Validate(recipe);
                var selectedImage = image!;
                await Task.Run(() =>
                {
                    new BuildHost().ValidateTools(recipe);
                    new BuildHost().ValidateImage(recipe, selectedImage);
                }, token);
            }
            token.ThrowIfCancellationRequested();
            GoStep(step + 1);
            await Task.CompletedTask;
        });
    }

    private BuildRecipe CreateRecipe() => new()
    {
        SourcePath = SourcePathBox.Text.Trim(), ImageIndex = image?.Index ?? 1,
        WorkspacePath = WorkspaceBox.Text.Trim(), OutputPath = OutputBox.Text.Trim(),
        DismPath = servicingTool, OscdimgPath = isoTool,
        Mode = SelectedMode, Flavor = UsesCore ? BuildFlavor.Core : BuildFlavor.Regular,
        CoreAcknowledged = coreAcknowledged, ExpertAcknowledged = ExpertConsent.IsChecked == true,
        SelectedOptions = options.Where(o => o.Selected).Select(o => o.Option.Id).ToArray(),
        ExpertActions = inventory.Count > 0 ? inventory.Where(i => i.Selected).Select(i => i.Action).ToArray() : pendingActions,
        Compression = (Compression)CompressionChoice.SelectedIndex,
        CompactInstallation = CompactCheck.IsOn, EnableNetFx3 = NetFxCheck.IsOn,
        PreferLocalAccount = LocalAccountCheck.IsOn,
        UnattendUsername = LocalAccountCheck.IsOn ? (LocalUserNameBox?.Text?.Trim() ?? "User") : "",
        UnattendPassword = LocalAccountCheck.IsOn ? (LocalPasswordBox?.Password ?? "") : "",
        UnattendComputerName = LocalAccountCheck.IsOn ? (ComputerNameBox?.Text?.Trim() ?? "") : "",
        AutoLogon = LocalAccountCheck.IsOn && (AutoLogonCheck?.IsOn ?? true),
        KeepWorkingFiles = KeepFilesCheck.IsOn,
        TurboMode = TurboCheck.IsOn, RemoveRedundantData = RedundantDataCheck.IsOn,
        OmitNetFx3SetupPayload = OmitSetupCheck.IsOn,
        SelectedBrowser = BrowserChoice?.SelectedIndex switch
        {
            1 => "firefox",
            2 => "brave",
            3 => "chrome",
            4 => "librewolf",
            _ => "none"
        },
        InstallVlc = VlcCheck?.IsChecked == true,
        InstallSevenZip = SevenZipCheck?.IsChecked == true,
        InstallNotepadPlus = NotepadPlusCheck?.IsChecked == true,
        InstallLibreOffice = LibreOfficeCheck?.IsChecked == true,
        DriverPath = DriversPathBox?.Text.Trim() ?? "",
        GamerMode = GamerModeCheck?.IsOn ?? true,
        DisableDefender = DisableDefenderCheck?.IsOn ?? false,
        DisableUpdatesAtFirstBoot = DisableUpdatesFirstBootCheck?.IsOn ?? true,
        DefenderControlPath = DefenderControlPathBox?.Text.Trim() ?? "",
        InstallMadTweak = InstallMadTweakCheck?.IsOn ?? true,
        MadTweakPath = MadTweakPathBox?.Text.Trim() ?? ""
    };

    private async Task RunOperation(OperationKind kind, Func<CancellationToken, Task> action)
    {
        if (busy) return;
        var operation = new OperationRun(kind);
        busy = true;
        active = operation;
        Control? previousFocus = null;
        try
        {
            try
            {
                previousFocus = CurrentFocus();
                progressTimer.Start();
                Notice.IsOpen = false;
                UpdateNavigation();
                await action(operation.Token);
                operation.Record(OperationEnd.Succeeded(kind));
            }
            catch (Exception error)
            {
                var outcome = Describe(kind, error);
                operation.Record(OperationEnd.From(outcome));
                try
                {
                    ShowStopped(kind, outcome);
                    if (OperationOutcomes.RequiresServicingTools(error))
                        ShowNotice(outcome.Title, outcome.FullMessage,
                            InfoBarSeverity.Error, NoticeAction.OpenTools, outcome.Details);
                    else if (OperationOutcomes.DownloadServiceProblem(error) is { } download)
                    {
                        officialDownloadPage = download.OfficialPage;
                        ShowNotice(outcome.Title, download.Message, InfoBarSeverity.Error, NoticeAction.OpenOfficialDownload, outcome.Details);
                    }
                }
                catch (Exception display)
                {
                    try { ShowNotice("L'opération n'a pas abouti.", ErrorText.Reason(error) + " " + display.Message, InfoBarSeverity.Error); }
                    catch (Exception) { ReportUiFailure("Le résultat n'a pas pu être affiché.", display); }
                }
            }
        }
        finally
        {
            try
            {
                FlushBuildProgress(flushAll: true);
                DrainDownload();
            }
            catch (Exception error) { ReportUiFailure("La dernière progression n'a pas pu être affichée.", error); }
            finally
            {
                progressTimer.Stop();
                busy = false;
                active = null;
                try
                {
                    StopBuildActivity();
                    ClearWorking();
                    UpdateNavigation();
                    if (!modalOpen) RestoreFocus(previousFocus);
                }
                catch (Exception error) { ReportUiFailure("La page n'a pas pu être réinitialisée après l'opération.", error); }
                finally { operation.Settle(); }
            }
        }
    }

    private void ReportUiFailure(string what, Exception error)
    {
        Debug.WriteLine(error);
        try
        {
            var text = what + " " + ErrorText.Reason(error);
            liveLog.Add("[App] " + text);
            if (Notice.IsOpen) Notice.Message = ErrorText.Summary(Notice.Message + " " + text, 400);
            else ShowNotice("Une partie de la page n'a pas pu être mise à jour.", text, InfoBarSeverity.Warning, NoticeAction.CopyDetails, error.ToString());
        }
        catch (Exception) { }
    }

    private OperationOutcome Describe(OperationKind kind, Exception error)
    {
        if (error is BuildFailureException failure) workDirectory = failure.WorkDirectory;
        var buildRun = kind is OperationKind.Build or OperationKind.Resume;
        var canResume = false;
        try { canResume = buildRun && workDirectory is not null && JobCheckpoint.Exists(workDirectory); }
        catch (Exception) { }
        var outcome = OperationOutcomes.Classify(kind, error, canResume);
        var logs = outcome.WorkDirectory ?? (kind == OperationKind.Resume ? workDirectory : null);
        if (logs is not null) workDirectory = logs;
        if (buildRun)
        {
            stoppedRun = kind;
            stoppedHasCheckpoint = canResume;
        }
        return outcome;
    }

    private void ShowStopped(OperationKind kind, OperationOutcome outcome)
    {
        FlushBuildProgress(flushAll: true);
        if (outcome.IsBuildRun)
        {
            StopBuildActivity();
            board.Stop(outcome.Cancelled);
            var noun = kind == OperationKind.Resume ? "Reprise" : "Création";
            BuildHeading.Text = outcome.Cancelled ? $"{noun} annulée" : $"{noun} arrêtée";
            CurrentMessage.Text = outcome.FullMessage;
            HistoryCount.Text = StageSummary.Describe(board);
        }
        var logs = outcome.WorkDirectory ?? (kind == OperationKind.Resume ? workDirectory : null);
        ShowNotice(outcome.Title, outcome.FullMessage, outcome.Level == NoticeLevel.Warning ? InfoBarSeverity.Warning : InfoBarSeverity.Error,
            logs is not null ? NoticeAction.OpenLogs : NoticeAction.CopyDetails, outcome.Details);
    }

    private void StopBuildActivity()
    {
        BuildActivity.IsActive = false;
        BuildBar.IsIndeterminate = false;
    }

    private void ShowNotice(string title, string message, InfoBarSeverity severity = InfoBarSeverity.Informational,
        NoticeAction action = NoticeAction.None, string? details = null)
    {
        Notice.Title = title;
        Notice.Message = ErrorText.Summary(message, 400);
        Notice.Severity = severity;
        noticeAction = action;
        noticeDetails = details ?? "";
        NoticeButton.Visibility = action == NoticeAction.None ? Visibility.Collapsed : Visibility.Visible;
        var label = action switch
        {
            NoticeAction.OpenLogs => "Ouvrir les journaux", NoticeAction.OpenTools => "Obtenir les outils d'image",
            NoticeAction.OpenOfficialDownload => "Ouvrir la page de téléchargement Microsoft", _ => "Copier les détails"
        };
        NoticeButton.Content = label;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(NoticeButton, label);
        Notice.IsOpen = true;
    }

    private void OnNoticeAction(object sender, RoutedEventArgs args)
    {
        if (noticeAction == NoticeAction.OpenLogs) { OnOpenLogs(sender, args); return; }
        if (noticeAction is NoticeAction.OpenTools or NoticeAction.OpenOfficialDownload)
        {
            var page = noticeAction == NoticeAction.OpenTools ? ServicingToolsRequiredException.DownloadPage : officialDownloadPage;
            try
            {
                if (page is not (ServicingToolsRequiredException.DownloadPage or OfficialImageDownloader.DownloadPage or OfficialImageDownloader.ArmDownloadPage))
                    throw new InvalidDataException("La page de téléchargement suggérée n'est pas une page Microsoft approuvée.");
                using var browser = Process.Start(new ProcessStartInfo(page) { UseShellExecute = true });
                if (browser is null) throw new IOException("Le navigateur n'a pas pu ouvrir la page Microsoft.");
            }
            catch (Exception error) when (error is IOException or InvalidDataException or System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                ShowNotice("Impossible d'ouvrir la page de téléchargement.", page + "\n" +
                    ErrorText.Reason(error), InfoBarSeverity.Error, NoticeAction.CopyDetails, error.ToString());
            }
            return;
        }
        if (noticeAction != NoticeAction.CopyDetails || noticeDetails.Length == 0) return;
        NoticeButton.Content = CopyToClipboard(noticeDetails) ? "Copié" : "Échec de copie";
    }

    private static bool CopyToClipboard(string text)
    {
        try
        {
            var package = new DataPackage();
            package.SetText(text);
            Clipboard.SetContent(package);
            return true;
        }
        catch (Exception) { return false; }
    }

    private void Working(string text)
    {
        BusyText.Text = text;
        BusyText.Visibility = Visibility.Visible;
        BusyBar.Visibility = Visibility.Visible;
    }

    private void ClearWorking()
    {
        BusyBar.Visibility = Visibility.Collapsed;
        BusyText.Visibility = Visibility.Collapsed;
    }

    private Control? CurrentFocus() =>
        Root.XamlRoot is { } root ? FocusManager.GetFocusedElement(root) as Control : null;

    private static bool IsShown(DependencyObject element)
    {
        for (var node = element; node is not null; node = VisualTreeHelper.GetParent(node))
            if (node is UIElement { Visibility: Visibility.Collapsed }) return false;
        return true;
    }

    private void RestoreFocus(Control? previous)
    {
        if (previous is null || Root.XamlRoot is null) return;
        if (FocusManager.GetFocusedElement(Root.XamlRoot) is UIElement current && IsShown(current) &&
            current is not Control { IsEnabled: false }) return;
        foreach (var candidate in new Control?[] { previous, NextButton, BackButton })
            if (candidate is { IsEnabled: true } && IsShown(candidate) && candidate.Focus(FocusState.Programmatic)) return;
    }

    private ElementTheme DialogTheme() => HighContrast ? ElementTheme.Default :
        Root.ActualTheme == ElementTheme.Dark ? ElementTheme.Dark : ElementTheme.Light;

    private async Task<bool> Confirm(string title, string content, string action)
    {
        if (modalOpen) return false;
        modalOpen = true;
        var previousFocus = CurrentFocus();
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = Root.XamlRoot, RequestedTheme = DialogTheme(),
                Title = title, Content = content, PrimaryButtonText = action, CloseButtonText = "Annuler",
                DefaultButton = ContentDialogButton.Close
            };
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        finally
        {
            modalOpen = false;
            RestoreFocus(previousFocus);
        }
    }

    private async void OnSupport(object sender, RoutedEventArgs args)
    {
        if (busy || modalOpen || Root.XamlRoot is null) return;
        modalOpen = true;
        var previousFocus = CurrentFocus();
        try
        {
            SupportDialog.XamlRoot = Root.XamlRoot;
            SupportDialog.RequestedTheme = DialogTheme();
            SupportError.IsOpen = false;
            await SupportDialog.ShowAsync();
        }
        catch (Exception error)
        {
            ShowNotice("Impossible d'ouvrir les options de soutien.", ErrorText.Reason(error),
                InfoBarSeverity.Error, NoticeAction.CopyDetails, error.ToString());
        }
        finally
        {
            modalOpen = false;
            RestoreFocus(previousFocus);
        }
    }

    private async void OnSupportDestination(object sender, RoutedEventArgs args)
    {
        try
        {
            if (sender is not FrameworkElement { Tag: string tag } ||
                !Enum.TryParse<SupportDestination>(tag, out var destination))
                throw new InvalidDataException("Ce lien de soutien n'a pas de destination valide.");
            var uri = ProjectSupport.Link(destination);
            if (!await Windows.System.Launcher.LaunchUriAsync(uri))
                throw new IOException($"Votre navigateur n'a pas pu ouvrir {uri}. Aucun paiement n'a été effectué.");
            SupportError.IsOpen = false;
        }
        catch (Exception error)
        {
            SupportError.Title = "Impossible d'ouvrir la page de soutien";
            SupportError.Message = ErrorText.Reason(error);
            SupportError.IsOpen = true;
        }
    }

    private async Task<string?> Pick(Func<Task<string?>> pick)
    {
        if (modalOpen) return null;
        modalOpen = true;
        var previousFocus = CurrentFocus();
        try { return await pick(); }
        finally
        {
            modalOpen = false;
            RestoreFocus(previousFocus);
        }
    }

    private Task<string?> PickFile(params string[] extensions)
    {
        var picker = new FileOpenPicker(AppWindow.Id);
        foreach (var extension in extensions) picker.FileTypeFilter.Add(extension);
        return Pick(async () => (await picker.PickSingleFileAsync())?.Path);
    }
    private Task<string?> PickFolder() => Pick(async () => (await new FolderPicker(AppWindow.Id).PickSingleFolderAsync())?.Path);
    private Task<string?> SaveFile(string filename, string extension)
    {
        var picker = new FileSavePicker(AppWindow.Id) { SuggestedFileName = filename };
        picker.FileTypeChoices.Add(extension == ".iso" ? "Image ISO" : "Recette Tiny11", new List<string> { extension });
        return Pick(async () => (await picker.PickSaveFileAsync())?.Path);
    }

    private async void OnChooseIso(object sender, RoutedEventArgs args) =>
        await RunOperation(OperationKind.Inspect, async token => { var path = await PickFile(".iso"); if (path is not null) { SourcePathBox.Text = path; await InspectSourceAsync(token); } });
    private async void OnChooseSourceFolder(object sender, RoutedEventArgs args) =>
        await RunOperation(OperationKind.Inspect, async token => { var path = await PickFolder(); if (path is not null) { SourcePathBox.Text = path; await InspectSourceAsync(token); } });
    private async void OnChooseWorkspace(object sender, RoutedEventArgs args) =>
        await RunOperation(OperationKind.Choose, async _ => { var path = await PickFolder(); if (path is not null) WorkspaceBox.Text = path; });
    private async void OnChooseOutput(object sender, RoutedEventArgs args) =>
        await RunOperation(OperationKind.Choose, async _ => { var path = await SaveFile("tiny11", ".iso"); if (path is not null) OutputBox.Text = path; });
    private async void OnChooseDrivers(object sender, RoutedEventArgs args) =>
        await RunOperation(OperationKind.Choose, async _ => { var path = await PickFolder(); if (path is not null) DriversPathBox.Text = path; });
    private async void OnChooseDefenderControl(object sender, RoutedEventArgs args) =>
        await RunOperation(OperationKind.Choose, async _ => { var path = await PickFolder(); if (path is not null) DefenderControlPathBox.Text = path; });
    private async void OnChooseMadTweak(object sender, RoutedEventArgs args) =>
        await RunOperation(OperationKind.Choose, async _ => { var path = await PickFile(".exe"); if (path is not null) MadTweakPathBox.Text = path; });

    private void OnLaunchMadTweak(object sender, RoutedEventArgs args)
    {
        try
        {
            var path = MadTweakPathBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                path = @"C:\Users\madtr\Downloads\Devlopement\MadTweak\dist\MadTweak.exe";
            if (!File.Exists(path))
                path = @"C:\Users\madtr\Downloads\Devlopement\MadTweak\MadTweak.exe";

            if (!File.Exists(path))
            {
                ShowNotice("Fichier introuvable", "L'exécutable MadTweak.exe est introuvable au chemin indiqué.", InfoBarSeverity.Warning);
                return;
            }

            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "runas" });
        }
        catch (Exception error)
        {
            ShowNotice("Impossible de lancer MadTweak", ErrorText.Reason(error), InfoBarSeverity.Error);
        }
    }

    private void OnSourceChanged(object sender, TextChangedEventArgs args)
    {
        if (!initialized) return;
        image = null;
        pendingImageIndex = null;
        pendingSourcePath = "";
        EditionChoice.ItemsSource = null;
        ImageBadge.Text = "Aucune image sélectionnée";
        ClearInventory();
        UpdateNavigation();
    }
    private void OnEditionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!initialized) return;
        var selected = EditionChoice.SelectedItem as WindowsImage;
        if (selected is not null && image?.Index != selected.Index &&
            !(pendingImageIndex == selected.Index &&
              SourcePathBox.Text.Trim().Equals(pendingSourcePath, StringComparison.OrdinalIgnoreCase)))
            ClearInventory();
        image = selected;
        ImageBadge.Text = image is null ? "Aucune image sélectionnée" : $"{image.Name}\n{image.Architecture} · version {image.Version.Build}";
        if (image is not null && !image.IsSupported)
            ShowNotice("Cette édition n'est pas prise en charge.", "Choisissez une image Windows 11 client, 22H2 ou supérieure, au format x64 ou ARM64.", InfoBarSeverity.Warning);
        UpdateNavigation();
    }

    private async void OnInspect(object sender, RoutedEventArgs args)
    {
        if (!ToolLocator.IsAdministrator)
        {
            ShowNotice("Privilèges d'administrateur requis.", "Fermez et rouvrez Tiny11Builder.exe en acceptant l'invite d'élévation administrateur Windows.", InfoBarSeverity.Warning);
            return;
        }
        await RunOperation(OperationKind.Inspect, InspectSourceAsync);
    }

    private async Task InspectSourceAsync(CancellationToken token)
    {
        if (!ToolLocator.IsAdministrator)
        {
            ShowNotice("Image sélectionnée.", "Fermez et rouvrez Tiny11Builder.exe avec les droits administrateur pour analyser ses éditions.");
            return;
        }
        Working("Analyse de l'image en cours…");
        var source = SourcePathBox.Text.Trim();
        servicingTool = ToolLocator.FindDism();
        await Task.Run(() => ToolLocator.Validate(servicingTool, "dism.exe"), token);
        var images = await Task.Run(() => engine.InspectAsync(source, servicingTool, token), token);
        var restored = source.Equals(pendingSourcePath, StringComparison.OrdinalIgnoreCase) ? pendingImageIndex : null;
        EditionChoice.ItemsSource = images;
        EditionChoice.SelectedIndex = ImageDefaults.SelectPosition(images, restored);
        ShowNotice("Image analysée.", $"{images.Count} édition(s) trouvée(s). {image?.Name} est sélectionnée.");
    }

    private void BeginDownloadText(Func<DownloadProgress, string> format)
    {
        downloadUpdates.TryTake(out _);
        downloadText = format;
    }

    private void EndDownloadText()
    {
        downloadText = null;
        DownloadBar.IsIndeterminate = false;
    }

    private void DrainDownload()
    {
        if (downloadText is not { } format || !downloadUpdates.TryTake(out var p)) return;
        DownloadBar.IsIndeterminate = p.Percent is null;
        if (p.Percent is double value) DownloadBar.Value = value;
        DownloadStatus.Text = format(p);
    }

    private static string FormatDownload(DownloadProgress p, string prefix) =>
        $"{prefix}{p.Bytes / (1024d * 1024):N0} Mo" +
        (p.Total is long total ? $" / {total / (1024d * 1024):N0} Mo" : "") +
        $" · {p.BytesPerSecond / (1024 * 1024):N1} Mo/s";

    private async void OnDownload(object sender, RoutedEventArgs args) => await RunOperation(OperationKind.Download, async token =>
    {
        var uri = OfficialImageDownloader.ValidateUrl(DownloadUrlBox.Text.Trim());
        var hash = OfficialImageDownloader.ValidateHash(PublishedHashBox.Text);
        var path = await SaveFile(Path.GetFileName(uri.AbsolutePath), ".iso");
        if (path is null) return;
        Working("Téléchargement depuis Microsoft en cours…");
        using var client = OfficialImageDownloader.CreateClient();
        DownloadBar.IsIndeterminate = true;
        BeginDownloadText(p => FormatDownload(p, ""));
        try
        {
            var downloaded = await new OfficialImageDownloader(client).DownloadAsync(uri.AbsoluteUri, path, hash, downloadUpdates, token,
                status: new UiProgress<string>(text => DispatcherQueue.TryEnqueue(() => DownloadStatus.Text = text)));
            DrainDownload();
            SourcePathBox.Text = downloaded.Path;
            DownloadStatus.Text = $"SHA-256 : {downloaded.Sha256}";
            ShowNotice(downloaded.VerifiedAgainstPublishedHash ? "ISO officiel téléchargé et empreinte validée." : "ISO officiel Microsoft téléchargé.",
                downloaded.VerifiedAgainstPublishedHash ? "Le fichier correspond à l'empreinte SHA-256 fournie. Inspectez-le pour sélectionner une édition." :
                "Aucune empreinte officielle fournie. L'empreinte calculée est affichée mais l'authenticité n'a pas pu être vérifiée indépendamment.", InfoBarSeverity.Informational);
        }
        finally { EndDownloadText(); }
    });

    private async void OnDownloadLatest(object sender, RoutedEventArgs args) => await RunOperation(OperationKind.Download, async token =>
    {
        Working("Recherche du dernier Windows 11 officiel…");
        DownloadBar.IsIndeterminate = true;
        try
        {
            var catalogProgress = new UiProgress<string>(text => DispatcherQueue.TryEnqueue(() => DownloadStatus.Text = text));
            using var catalogClient = LatestWindowsMedia.CreateClient();
            var media = await new LatestWindowsMedia(catalogClient).ResolveAsync(HostArchitecture,
                CultureInfo.CurrentUICulture, catalogProgress, token);
            LatestDownloadDescription.Text = media.Description;
            var folder = Path.Combine(PathSafety.Full(WorkspaceBox.Text.Trim()), "downloads");
            PathSafety.RejectReparsePoints(folder);
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, media.FileName);
            var hashFile = path + ".sha256";
            if (File.Exists(path) && File.Exists(hashFile))
            {
                Working("Vérification de l'ISO téléchargé…");
                await using var input = File.OpenRead(path);
                await IsoStructure.ValidateAsync(input, token);
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(input, token));
                if (actual != (await File.ReadAllTextAsync(hashFile, token)).Trim() ||
                    (media.Sha256 is not null && actual != media.Sha256))
                    throw new InvalidDataException("L'ISO mis en cache a été altéré. Il n'a pas été accepté.");
                DownloadStatus.Text = "ISO précédemment téléchargé vérifié.";
            }
            else
            {
                if (File.Exists(path)) path = Path.Combine(folder, Path.GetFileNameWithoutExtension(media.FileName) + "-" + Guid.NewGuid().ToString("N")[..8] + ".iso");
                Working("Téléchargement depuis Microsoft en cours…");
                using var downloadClient = OfficialImageDownloader.CreateClient();
                BeginDownloadText(p => FormatDownload(p, $"{media.Language} · "));
                var download = await new OfficialImageDownloader(downloadClient).DownloadAsync(media.Url.AbsoluteUri,
                    path, media.Sha256, downloadUpdates, token, status: catalogProgress);
                DrainDownload();
                await File.WriteAllTextAsync(path + ".sha256", download.Sha256, token);
                DownloadStatus.Text = $"Téléchargé : {media.Description}. SHA-256 : {download.Sha256}" +
                    (download.VerifiedAgainstPublishedHash ? " (empreinte officielle validée)" : " (empreinte calculée ; Microsoft n'a pas fourni d'empreinte officielle)");
            }
            SourcePathBox.Text = path;
            await InspectSourceAsync(token);
        }
        finally { EndDownloadText(); }
    });

    private async Task EnsureToolsAsync(CancellationToken token)
    {
        servicingTool = ToolLocator.FindDism();
        using var client = OfficialImageDownloader.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(60);
        isoTool = await new OscdimgAcquirer(client).EnsureAsync(Path.Combine(dataDirectory, "tools"),
            new UiProgress<string>(text => DispatcherQueue.TryEnqueue(() => ToolStatus.Text = text)), token);
        ToolStatus.Text = "Outils d'image et de création ISO prêts.";
    }

    private void OnModeChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!initialized || applyingMode) return;
        ApplyMode();
    }
    private void ApplyMode()
    {
        applyingMode = true;
        var selected = CleanupCatalog.ForMode(SelectedMode);
        foreach (var choice in options) choice.Selected = selected.Contains(choice.Option.Id);
        foreach (var item in inventory) item.Selected = false;
        pendingActions = [];
        CustomCoreCheck.IsOn = false;
        coreAcknowledged = false;
        ExpertConsent.IsChecked = false;
        applyingMode = false;
        UpdateModePresentation();
        UpdateCounts();
    }
    private void UpdateModePresentation()
    {
        CustomChoices.Visibility = SelectedMode == BuildMode.Custom ? Visibility.Visible : Visibility.Collapsed;
        CoreWarning.IsOpen = UsesCore;
        CoreConsentStatus.Visibility = UsesCore ? Visibility.Visible : Visibility.Collapsed;
        CoreConsentStatus.Text = coreAcknowledged ? "Limitations de Core acceptées pour cette création." : "Il vous sera demandé d'accepter les limitations de Core avant de continuer.";
        ModeDescription.Text = SelectedMode switch
        {
            BuildMode.Tiny11 => "Suppressions Tiny11 d'origine, incluant les binaires Edge/WebView2 et OneDrive. Windows Update, Defender et la récupération sont conservés. Choisissez Personnalisé pour modifier individuellement.",
            BuildMode.Tiny11Core => "Suppressions Tiny11 Core, incluant l'élagage irréversible des composants. Choisissez Personnalisé pour modifier individuellement.",
            _ => "Démarrez avec aucun élément pré-sélectionné, puis choisissez ce que vous souhaitez retirer ou désactiver. Vous pouvez également lister les composants système."
        };
    }

    private void OnCustomCoreChanged(object sender, RoutedEventArgs args)
    {
        if (!initialized) return;
        UpdateModePresentation();
        UpdateCounts();
    }
    private void OnChoicesChanged(object sender, RoutedEventArgs args) => UpdateNavigation();

    private void OnOptionFilterChanged(object sender, SelectionChangedEventArgs args) { if (initialized) FilterOptions(); }
    private void FilterOptions()
    {
        var category = (OptionFilter.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Tous";
        OptionsList.ItemsSource = options.Where(o => category is "Tous" or "All" || o.Category == category).ToArray();
    }

    private void OnPresetBalanced(object sender, RoutedEventArgs args)
    {
        var ids = CleanupCatalog.ForPreset(CleanupPreset.Balanced).ToHashSet();
        foreach (var choice in options) choice.Selected = ids.Contains(choice.Option.Id);
        UpdateCounts();
    }

    private void OnPresetMinimal(object sender, RoutedEventArgs args)
    {
        var ids = CleanupCatalog.ForPreset(CleanupPreset.Minimal).ToHashSet();
        foreach (var choice in options) choice.Selected = ids.Contains(choice.Option.Id);
        UpdateCounts();
    }

    private void OnPresetTiny11Standard(object sender, RoutedEventArgs args)
    {
        var ids = CleanupCatalog.ForPreset(CleanupPreset.Original).ToHashSet();
        foreach (var choice in options) choice.Selected = ids.Contains(choice.Option.Id);
        UpdateCounts();
    }

    private void OnPresetAppsOnly(object sender, RoutedEventArgs args)
    {
        foreach (var choice in options) choice.Selected = choice.Option.Kind == OptionKind.App;
        UpdateCounts();
    }

    private void OnSelectAllOptions(object sender, RoutedEventArgs args)
    {
        var category = (OptionFilter.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Tous";
        var targets = category is "Tous" or "All" ? options : options.Where(o => o.Category == category);
        foreach (var choice in targets) choice.Selected = true;
        UpdateCounts();
    }

    private void OnDeselectAllOptions(object sender, RoutedEventArgs args)
    {
        var category = (OptionFilter.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Tous";
        var targets = category is "Tous" or "All" ? options : options.Where(o => o.Category == category);
        foreach (var choice in targets) choice.Selected = false;
        UpdateCounts();
    }
    private void UpdateCounts()
    {
        if (!initialized) return;
        var custom = inventory.Count > 0 ? inventory.Count(i => i.Selected) : pendingActions.Length;
        SelectionCount.Text = $"{options.Count(o => o.Selected)} groupes de suppression sélectionnés · {custom} actions avancées";
        ExpertCount.Text = $"{inventory.Count:N0} composants listés · {custom:N0} actions avancées sélectionnées.";
        UpdateNavigation();
    }

    private async void OnOpenExpert(object sender, RoutedEventArgs args)
    {
        if (busy || modalOpen || SelectedMode != BuildMode.Custom) return;
        if (image is null) { ShowNotice("Analysez d'abord une image.", "Sélectionnez une édition avant de lister ses composants.", InfoBarSeverity.Warning); return; }
        if (!await Confirm("Les composants protégés sont inclus",
            "Cet inventaire inclut des pilotes de démarrage, des fichiers de sécurité, licences, applications système et charges utiles de composants. Les supprimer peut rendre Windows inamorçable, vulnérable ou impossible à mettre à jour. La suppression directe contourne le gestionnaire DISM.\n\nNe continuez que pour des tests isolés en VM, avec des sauvegardes.",
            "Inspecter les composants")) return;
        await RunOperation(OperationKind.Inventory, async token =>
        {
            Working("Listage des composants de l'image…");
            var recipe = CreateRecipe() with { CoreAcknowledged = true, ExpertAcknowledged = true };
            var items = await Task.Run(() => engine.InventoryAsync(recipe, BuildProgressReporter(), token), token);
            var previous = inventory.Count > 0 ? inventory.Where(i => i.Selected).Select(i => i.Action).ToArray() : pendingActions;
            inventory = items.Select(item =>
            {
                var choice = new InventoryChoice(item);
                var prior = previous.FirstOrDefault(a => a.Kind == item.Kind && a.Identity == item.Identity);
                if (prior is not null) { choice.Selected = true; choice.ModeIndex = prior.Mode == ExpertActionMode.Disable ? 0 : 1; }
                choice.PropertyChanged += (_, _) => UpdateCounts();
                return choice;
            }).ToList();
            pendingActions = [];
            ExpertPanel.Visibility = Visibility.Visible;
            ExpertConsent.IsChecked = true;
            FilterInventory();
            UpdateCounts();
            if (!ToolLocator.SupportsServicing(recipe.DismPath, image!))
                ShowNotice("Liste des composants chargée en lecture seule.", "Les outils installés peuvent afficher cette image, mais sa création nécessite des outils de déploiement Windows ADK plus récents. Installez-les et rouvrez l'application avant de créer l'image.",
                    InfoBarSeverity.Warning, NoticeAction.OpenTools);
            else
                ShowNotice("Liste des composants chargée.", "Les sélections existantes ont été conservées. Cochez les éléments individuels que vous souhaitez retirer ou désactiver.");
        });
    }

    private void ClearInventory()
    {
        inventory = [];
        pendingActions = [];
        InventoryList.ItemsSource = null;
        ExpertPanel.Visibility = Visibility.Collapsed;
        ExpertConsent.IsChecked = false;
        UpdateCounts();
    }
    private void OnInventoryFilterChanged(object sender, TextChangedEventArgs args) { if (initialized) FilterInventory(); }
    private void OnInventoryFilterSelection(object sender, SelectionChangedEventArgs args) { if (initialized) FilterInventory(); }
    private void FilterInventory()
    {
        var filter = (InventoryFilter.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? (InventoryFilter.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All";
        var query = InventorySearch.Text.Trim();
        InventoryList.ItemsSource = inventory.Where(i => (filter is "All" or "Tous" || i.Item.Kind.ToString() == filter) &&
            (query.Length == 0 || i.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             i.Detail.Contains(query, StringComparison.OrdinalIgnoreCase))).ToArray();
    }
    private void OnClearExpert(object sender, RoutedEventArgs args)
    {
        foreach (var item in inventory) item.Selected = false;
        pendingActions = [];
        UpdateCounts();
    }

    private string CreateReview()
    {
        var recipe = CreateRecipe();
        var text = new StringBuilder();
        text.AppendLine($"IMAGE         {image?.Summary ?? "Non analysée"}");
        text.AppendLine($"MODE          {(ModeChoice.SelectedItem as ComboBoxItem)?.Content}");
        if (SelectedMode == BuildMode.Custom) text.AppendLine($"SUPPRESSIONS CORE {UsesCore}");
        text.AppendLine($"SOURCE        {recipe.SourcePath}");
        text.AppendLine($"SORTIE        {recipe.OutputPath}");
        text.AppendLine($"ESPACE TRAVAIL {recipe.WorkspacePath}");
        text.AppendLine($"OUTIL IMAGE   {recipe.DismPath}");
        text.AppendLine($"OUTIL ISO     {recipe.OscdimgPath}");
        text.AppendLine($"COMPRESSION   {recipe.Compression}");
        text.AppendLine($"MODE TURBO    {recipe.TurboMode}");
        text.AppendLine($"MODE GAMER    {recipe.GamerMode}");
        text.AppendLine($"SANS DEFENDER {recipe.DisableDefender}");
        if (!string.IsNullOrWhiteSpace(recipe.DefenderControlPath)) text.AppendLine($"DEFENDER CONTROL {recipe.DefenderControlPath}");
        if (recipe.InstallMadTweak && !string.IsNullOrWhiteSpace(recipe.MadTweakPath)) text.AppendLine($"MADTWEAK      {recipe.MadTweakPath}");
        text.AppendLine($"BLOQUER MAJ 1ER BOOT {recipe.DisableUpdatesAtFirstBoot}");
        text.AppendLine($"NETTOYAGE CACHE {recipe.RemoveRedundantData}");
        text.AppendLine($"OMISSION SXS  {recipe.OmitNetFx3SetupPayload}");
        text.AppendLine($"COMPACT OS    {recipe.CompactInstallation}");
        text.AppendLine($".NET 3.5      {recipe.EnableNetFx3}");
        text.AppendLine($"COMPTE LOCAL  {recipe.PreferLocalAccount} (au mieux)");
        if (SetupLabels.LegacyReason(recipe) is { } legacyReason) text.AppendLine($"INSTALLEUR HÉRITÉ automatique, requis pour {legacyReason}");
        text.AppendLine("\nSUPPRIMER / DÉSACTIVER");
        foreach (var option in options.Where(o => o.Selected)) text.AppendLine($"  {option.Title}");
        foreach (var action in recipe.ExpertActions) text.AppendLine($"  AVANCÉ {action.Mode} {action.Kind}: {action.Identity}");
        text.AppendLine("\nLES ÉLÉMENTS NON COCHÉS SONT CONSERVÉS.");
        if (recipe.Flavor == BuildFlavor.Core)
            text.AppendLine("\nCORE: élagage irréversible des composants, WinRE supprimé, Windows Update et Defender désactivés. Installez les fonctionnalités facultatives maintenant, pas après création.");
        if (recipe.ExpertActions.Length > 0)
            text.AppendLine("\nAVANCÉ: les dépendances ne sont pas résolues. Les suppressions protégées et critiques au démarrage peuvent produire une image inamorçable.");
        text.AppendLine("\nCertaines stratégies peuvent être ignorées par une version spécifique de Windows ou annulées lors de mises à jour. Un test en machine virtuelle est vivement recommandé.");
        return text.ToString();
    }

    private void UpdateReview()
    {
        var recipe = CreateRecipe();
        ReviewText.Text = CreateReview();
        ReviewImage.Text = image?.Summary ?? "Aucune image sélectionnée";
        ReviewMode.Text = (ModeChoice.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
        ReviewRemovals.Text = $"{recipe.SelectedOptions.Length} groupes de suppression · {recipe.ExpertActions.Length} actions individuelles\n" +
            (UsesCore ? "Sans maintenance, sans récupération ni Defender." : SelectedMode == BuildMode.Custom ?
                "Les suppressions personnalisées peuvent impacter la maintenance ou la sécurité." : "Windows Update, récupération et Defender conservés.");
        ReviewPaths.Text = $"ISO\n{recipe.OutputPath}\n\nEspace de travail\n{recipe.WorkspacePath}";
        var compression = recipe.Compression switch { Compression.Recovery => "ESD de récupération", Compression.Maximum => "WIM Maximum", _ => "WIM Rapide" };
        var browserName = (recipe.SelectedBrowser ?? "").ToLowerInvariant() switch
        {
            "firefox" => "Mozilla Firefox",
            "brave" => "Brave Browser",
            "chrome" => "Google Chrome",
            "librewolf" => "LibreWolf",
            _ => "Aucun"
        };
        var openSourceApps = new List<string>();
        if (recipe.InstallVlc) openSourceApps.Add("VLC");
        if (recipe.InstallSevenZip) openSourceApps.Add("7-Zip");
        if (recipe.InstallNotepadPlus) openSourceApps.Add("Notepad++");
        if (recipe.InstallLibreOffice) openSourceApps.Add("LibreOffice");
        var appsSummary = openSourceApps.Count > 0 ? string.Join(", ", openSourceApps) : "Aucune";

        ReviewOptions.Text = $"Compression {compression}\nMode Turbo : {(recipe.TurboMode ? "Activé" : "Désactivé")}\n" +
            $"Mode Gamer : {(recipe.GamerMode ? "Activé" : "Désactivé")}\n" +
            $"Windows Defender : {(recipe.DisableDefender ? "Désactivé" : "Actif")}\n" +
            (!string.IsNullOrWhiteSpace(recipe.DefenderControlPath) ? $"Defender Control : Embarqué ({recipe.DefenderControlPath})\n" : "") +
            $"Mises à jour au 1er boot : {(recipe.DisableUpdatesAtFirstBoot ? "Bloquées" : "Autorisées")}\n" +
            $"Navigateur web : {browserName}\nApplications Open-Source : {appsSummary}\n" +
            (!string.IsNullOrWhiteSpace(recipe.DriverPath) ? $"Pilotes personnalisés : {recipe.DriverPath}\n" : "") +
            $"Données redondantes : {(recipe.RemoveRedundantData ? "Supprimer" : "Conserver")}\nFichiers .NET 3.5 : {(recipe.OmitNetFx3SetupPayload ? "Omettre" : "Conserver")}\n" +
            $"Installation CompactOS : {(recipe.CompactInstallation ? "Activée" : "Désactivée")}\n.NET 3.5 : {(recipe.EnableNetFx3 ? "Inclus" : "Non inclus")}\n" +
            $"Compte local : {(recipe.PreferLocalAccount ? (!string.IsNullOrWhiteSpace(recipe.UnattendUsername) ? $"{recipe.UnattendUsername} (AutoLogon: {(recipe.AutoLogon ? "Oui" : "Non")})" : "Préféré") : "Désactivé")}\nFichiers temporaires : {(recipe.KeepWorkingFiles ? "Conserver" : "Nettoyer après création")}" +
            (SetupLabels.Legacy(recipe) is { } legacy ? "\n" + legacy : "");
    }

    private const string BuildDescriptionText = "L'édition sélectionnée va être copiée, modifiée, compressée et enregistrée sous forme d'ISO bootable.";

    private void UpdateBuildDescription(BuildRecipe recipe) => BuildDescription.Text = BuildDescriptionText +
        (SetupLabels.LegacyReason(recipe) is { } reason ? $" L'installeur hérité est activé automatiquement pour {reason}." : "");

    private async Task StartBuildAsync()
    {
        BuildRecipe recipe;
        try { recipe = CreateRecipe(); recipe.ValidateOptions(); PathSafety.Validate(recipe); }
        catch (Exception error) { ShowNotice("La recette n'est pas prête.", ErrorText.Reason(error), InfoBarSeverity.Error); return; }
        if (!await Confirm("Créer cette image Windows hors-ligne ?",
            $"Sortie : {recipe.OutputPath}\n\nMode : {(ModeChoice.SelectedItem as ComboBoxItem)?.Content}\nGroupes de suppression : {recipe.SelectedOptions.Length}\nActions individuelles : {recipe.ExpertActions.Length}" +
            (SetupLabels.Legacy(recipe) is { } legacy ? "\n" + legacy : ""),
            "Créer")) return;
        await RunOperation(OperationKind.Build, async token =>
        {
            ResetBuildPage();
            BuildActivity.IsActive = true;
            BuildBar.IsIndeterminate = true;
            CurrentMessage.Text = "Préparation des outils d'image…";
            await EnsureToolsAsync(token);
            recipe = CreateRecipe();
            BeginBuildRun("Création en cours");
            var reporter = BuildProgressReporter();
            var built = await Task.Run(() => engine.BuildAsync(recipe, reporter, token), token);
            ShowBuildResult(built, resumed: false, turbo: recipe.TurboMode);
        });
    }

    private void ResetBuildPage()
    {
        board.Reset();
        activePhase = -1;
        while (progressUpdates.TryDequeue(out _)) { }
        result = null;
        workDirectory = null;
        stoppedRun = null;
        stoppedHasCheckpoint = false;
        ResultPanel.Visibility = Visibility.Collapsed;
        ResultWarnings.IsOpen = false;
        liveLog.Clear();
        RefreshLog(force: true);
        BuildHeading.Text = "Création";
        CurrentStage.Text = "En attente de démarrage";
        CurrentMessage.Text = "Aucune modification n'a été effectuée.";
        BuildGlyph.Glyph = "\uE7C5";
        BuildActivity.IsActive = false;
        BuildBar.IsIndeterminate = false;
        BuildBar.Value = 0;
        OverallBar.Value = 0;
        BuildPercent.Text = "0%";
        OverallStep.Text = "";
        CurrentPercent.Text = "";
        HistoryCount.Text = StageSummary.Describe(board);
    }

    private void BeginBuildRun(string heading)
    {
        BuildHeading.Text = heading;
        BuildActivity.IsActive = true;
        BuildBar.IsIndeterminate = true;
    }

    private void ShowBuildResult(BuildResult built, bool resumed, bool turbo)
    {
        FlushBuildProgress(flushAll: true);
        result = built;
        workDirectory = built.WorkDirectory;
        ResultPath.Text = built.IsoPath;
        ResultHash.Text = built.Sha256;
        var size = File.Exists(built.IsoPath) ? $" · ISO de {new FileInfo(built.IsoPath).Length / (1024d * 1024 * 1024):N2} Go" : "";
        var metrics = new List<string>
        {
            $"{(resumed ? "Création reprise" : turbo ? "Création Turbo" : "Création standard")} · {built.Duration.TotalMinutes:N1} minutes{(resumed ? " au total" : "")}{size}"
        };
        if (!resumed || built.RemovedLogicalBytes > 0 || built.OmittedSetupBytes > 0)
            metrics.Add($"Données redondantes supprimées : {built.RemovedLogicalBytes / (1024d * 1024):N1} Mo logiques · " +
                $"Fichiers d'installation omis : {built.OmittedSetupBytes / (1024d * 1024):N1} Mo");
        metrics.Add("Le minutage détaillé des étapes est enregistré avec le rapport de création.");
        ResultMetrics.Text = string.Join("\n", metrics);
        var warnings = built.Warnings;
        if (warnings.Count > 0)
            active?.NoteWarning("L'ISO a été enregistré avec des avertissements. Consultez les détails sur la page.");
        ResultWarnings.IsOpen = warnings.Count > 0;
        if (warnings.Count > 0)
            ResultWarnings.Message = string.Join("\n", warnings.Take(4).Select(w => "• " + ErrorText.Summary(w, 200))) +
                (warnings.Count > 4 ? $"\n• {warnings.Count - 4} autres dans le journal de création" : "");
        ResultPanel.Visibility = Visibility.Visible;
        BuildHeading.Text = "Image ISO créée";
        CurrentStage.Text = "Terminé";
        BuildActivity.IsActive = false;
        BuildGlyph.Glyph = "\uE73E";
        if (resumed) board.MarkEarlierPhasesDone();
        board.Complete();
        OverallBar.Value = 100;
        BuildPercent.Text = "100%";
        OverallStep.Text = "Toutes les étapes terminées";
        CurrentPercent.Text = "100%";
        HistoryCount.Text = StageSummary.Describe(board);
        BuildBar.IsIndeterminate = false;
        BuildBar.Value = 100;
        CurrentMessage.Text = warnings.Count > 0 ?
            "L'ISO a été enregistré avec des avertissements. Consultez les opérations ignorées et autres détails ci-dessous." :
            "L'image ISO et le rapport de création ont été enregistrés. Testez l'ISO dans une VM avant installation.";
        stoppedRun = null;
        stoppedHasCheckpoint = false;
        completed = true;
    }

    private IProgress<BuildProgress> BuildProgressReporter() => new UiProgress<BuildProgress>(progressUpdates.Enqueue);

    private void FlushBuildProgress(bool flushAll = false)
    {
        var clock = Stopwatch.StartNew();
        var count = 0;
        var applied = false;
        while ((flushAll || count++ < 256 && clock.ElapsedMilliseconds < 20) && progressUpdates.TryDequeue(out var p))
        {
            if (completed) continue;
            cancelFlow?.Observe(p);
            if (p.Stage == CancelCleanup.Stage)
            {
                if (!string.IsNullOrWhiteSpace(p.Message) && !BuildProgressText.IsConsoleProgress(p.Message))
                {
                    if (running is OperationKind.Build or OperationKind.Resume) CurrentMessage.Text = $"Annulation demandée. {p.Message}…";
                    liveLog.Add($"[{p.Stage}] {BuildProgressText.ForDisplay(p.Message)}");
                }
                continue;
            }
            var phase = board.Apply(p);
            applied |= phase >= 0;
            if (p.State is BuildOperationState.Pending or BuildOperationState.Skipped)
            {
                continue;
            }
            if (!string.IsNullOrWhiteSpace(p.Message) && !BuildProgressText.IsConsoleProgress(p.Message))
            {
                CurrentStage.Text = FormatStageName(p.Stage);
                if (!cancelRequested)
                    CurrentMessage.Text = p.State == BuildOperationState.Failed ?
                        p.Message + " a échoué. Développez cette étape pour voir l'erreur." : FormatStageMessage(p.Stage, p.Message);
            }
            else if (p.Stage == "Compress")
            {
                CurrentStage.Text = "Compression de l'image finale (ESD / WIM)";
                if (!cancelRequested && p.Percent is double currentPct)
                    CurrentMessage.Text = $"Exportation et compression haute densité en cours : {currentPct:0.0}%";
            }
            BuildBar.IsIndeterminate = p.Percent is null && p.State is not (BuildOperationState.Completed or BuildOperationState.Failed or BuildOperationState.Cancelled);
            if (p.Percent is double percent) BuildBar.Value = percent;
            else if (p.State == BuildOperationState.Completed) BuildBar.Value = 100;
            CurrentPercent.Text = BuildBar.IsIndeterminate ? "" : $"{BuildBar.Value:0}%";
            if (step == 5 && phase >= 0)
            {
                var stage = board.Stages[phase];
                if (activePhase != phase && p.State is not BuildOperationState.Completed)
                {
                    activePhase = phase;
                    stage.Expanded = true;
                }
                var (start, end) = BuildProgressText.Range(p.Stage);
                var fraction = p.Stage == "Compress" && p.Percent.HasValue ? p.Percent.Value / 100.0 : stage.Fraction(p);
                var overall = start + (end - start) * fraction;
                OverallBar.Value = Math.Max(OverallBar.Value, overall);
                BuildPercent.Text = $"{OverallBar.Value:0}%";
                OverallStep.Text = $"Étape {phase + 1} sur {board.Stages.Length}";
                BuildGlyph.Glyph = stage.Glyph;
            }
            var line = p.Detail ?? (p.State is null ? null : p.Message);
            if (string.IsNullOrWhiteSpace(line) || BuildProgressText.IsConsoleProgress(line)) continue;
            liveLog.Add($"[{p.Stage}] {BuildProgressText.ForDisplay(line)}");
        }
        if (running == OperationKind.Resume) board.MarkEarlierPhasesDone();
        if (applied || running == OperationKind.Resume) HistoryCount.Text = StageSummary.Describe(board);
        RefreshLog();
    }

    private static string FormatStageName(string stage) => stage switch
    {
        "Prepare" => "Préparation",
        "Copy media" => "Copie du support d'installation",
        "Export edition" => "Exportation de l'édition sélectionnée",
        "Mount" => "Montage de l'image Windows",
        "Inventory" => "Analyse des composants",
        "Custom servicing" => "Application des modifications et pilotes",
        "Remove apps" => "Suppression des applications",
        "Optional features" => "Configuration des fonctionnalités",
        ".NET 3.5" => "Intégration de .NET Framework 3.5",
        "Windows settings" => "Application des paramètres Windows",
        "Component cleanup" => "Nettoyage des composants",
        "Advanced payloads" => "Suppression des composants avancés",
        "Core pruning" => "Élagage Core des composants",
        "Redundant data" => "Suppression des données redondantes",
        "Commit" => "Enregistrement de l'image (Commit)",
        "Setup image" => "Configuration de l'installeur Windows",
        "Compress" => "Compression de l'image finale (ESD / WIM)",
        "Create ISO" => "Création du fichier ISO amorçable",
        "Verify output" => "Vérification et publication de l'ISO",
        _ => stage
    };

    private static string FormatStageMessage(string stage, string message) => stage switch
    {
        "Compress" => "Exportation et compression haute densité de l'image...",
        "Create ISO" => "Génération de l'image ISO bootable...",
        "Commit" => "Enregistrement des modifications de l'image...",
        _ => message
    };

    private void RefreshLog(bool force = false)
    {
        if (!liveLog.TryRender(BuildDetails.IsExpanded, BuildLog.SelectionLength > 0, force, out var text)) return;
        var viewer = LogScroller();
        var follow = viewer is null || viewer.VerticalOffset >= viewer.ScrollableHeight - 8;
        var offset = viewer?.VerticalOffset ?? 0;
        BuildLog.Text = text;
        if (viewer is null) return;
        BuildLog.UpdateLayout();
        viewer.ChangeView(null, follow ? viewer.ScrollableHeight : Math.Min(offset, viewer.ScrollableHeight), null, disableAnimation: true);
    }

    private ScrollViewer? LogScroller() => logScroller ??= FindScroller(BuildLog);

    private static ScrollViewer? FindScroller(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is ScrollViewer viewer) return viewer;
            if (FindScroller(child) is { } nested) return nested;
        }
        return null;
    }

    private void OnDetailsExpanding(Expander sender, ExpanderExpandingEventArgs args)
    {
        RefreshLog(force: true);
        DispatcherQueue.TryEnqueue(() =>
        {
            if (LogScroller() is { } viewer) viewer.ChangeView(null, viewer.ScrollableHeight, null, disableAnimation: true);
        });
    }

    private void OnLogSelectionChanged(object sender, RoutedEventArgs args)
    {
        if (BuildLog.SelectionLength == 0) RefreshLog();
    }

    private async void OnCancel(object sender, RoutedEventArgs args)
    {
        if (active is not { } run || modalOpen || Root.XamlRoot is null) return;
        if (CancellationFlow.TryBegin(run) is not { } flow) return;
        modalOpen = true;
        cancelFlow = flow;
        var previousFocus = CurrentFocus();
        try { await ShowCancelPrompt(flow); }
        catch (Exception error)
        {
            ReportUiFailure(flow.Stage == CancelStage.Confirming ? "L'annulation n'a pas pu être confirmée." :
                "Le dialogue d'annulation s'est fermé, mais l'opération est toujours en cours d'arrêt.", error);
        }
        finally
        {
            cancelFlow = null;
            flow.Close();
            modalOpen = false;
            RestoreFocus(previousFocus);
        }
    }

    private async Task ShowCancelPrompt(CancellationFlow flow)
    {
        var prompt = new CancelPrompt(flow, Root.XamlRoot, DialogTheme(), AfterCancelRequested, TryOpenFolder, CopyToClipboard, ReportUiFailure);
        _ = WatchSettled(flow);
        try
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                await prompt.ShowAsync();
                if (flow.Stage != CancelStage.Cancelling) return;
            }
            await flow.Run.Settled;
        }
        finally { prompt.Detach(); }
    }

    private async Task WatchSettled(CancellationFlow flow)
    {
        try
        {
            await flow.Run.Settled;
            flow.RunSettled();
        }
        catch (Exception error) { ReportUiFailure("Le dialogue d'annulation n'a pas pu afficher l'état final de l'opération.", error); }
    }

    private void AfterCancelRequested()
    {
        if (running is OperationKind.Build or OperationKind.Resume)
            CurrentMessage.Text = "Annulation demandée. Attente de la fin de l'opération en cours…";
        Working("Annulation en cours…");
        UpdateNavigation();
    }

    private static bool TryOpenFolder(string path)
    {
        if (!Directory.Exists(path)) return false;
        try
        {
            using var process = Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true, Arguments = $"\"{path}\"" });
            return true;
        }
        catch (Exception) { return false; }
    }

    private sealed class CancelPrompt
    {
        private readonly CancellationFlow flow;
        private readonly Action cancelSent;
        private readonly Func<string, bool> openLogs;
        private readonly Func<string, bool> copyDetails;
        private readonly Action<string, Exception> report;
        private readonly ContentDialog dialog;
        private readonly TextBlock body = new() { TextWrapping = TextWrapping.Wrap };
        private readonly InfoBar notice = new() { IsClosable = false, IsOpen = false };
        private readonly StackPanel progress = new() { Spacing = 10 };
        private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        private readonly ProgressBar bar = new() { Minimum = 0, Maximum = 100 };
        private readonly TextBlock detail = new() { TextWrapping = TextWrapping.Wrap };
        private readonly TextBlock footnote = new() { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };

        public CancelPrompt(CancellationFlow flow, XamlRoot root, ElementTheme theme, Action cancelSent,
            Func<string, bool> openLogs, Func<string, bool> copyDetails, Action<string, Exception> report)
        {
            this.flow = flow;
            this.cancelSent = cancelSent;
            this.openLogs = openLogs;
            this.copyDetails = copyDetails;
            this.report = report;
            var hint = (Style)Application.Current.Resources["Hint"];
            detail.Style = hint;
            footnote.Style = hint;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetLiveSetting(status, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(bar, "Progression de l'annulation");
            progress.Children.Add(status);
            progress.Children.Add(bar);
            progress.Children.Add(detail);
            var content = new StackPanel { Spacing = 16, MinWidth = 380 };
            content.Children.Add(body);
            content.Children.Add(notice);
            content.Children.Add(progress);
            content.Children.Add(footnote);
            dialog = new ContentDialog
            {
                XamlRoot = root, RequestedTheme = theme, Content = content,
                DefaultButton = ContentDialogButton.Close,
                PrimaryButtonStyle = (Style)Application.Current.Resources["CriticalButtonStyle"]
            };
            dialog.PrimaryButtonClick += OnConfirm;
            dialog.SecondaryButtonClick += OnLink;
            dialog.Closing += OnClosing;
            flow.Changed += Render;
            Render();
        }

        public async Task ShowAsync() => await dialog.ShowAsync();

        public void Detach() => flow.Changed -= Render;

        private void Render()
        {
            CancelView view;
            try { view = flow.View; }
            catch (Exception error)
            {
                report("Le dialogue d'annulation n'a pas pu lire son état.", error);
                return;
            }
            Safely("La progression de l'annulation n'a pas pu être dessinée.", () =>
            {
                bar.IsIndeterminate = view.Indeterminate;
                bar.Value = view.Percent;
                progress.Visibility = view.ShowProgress ? Visibility.Visible : Visibility.Collapsed;
            });
            Safely("Le dialogue d'annulation n'a pas pu afficher son message.", () =>
            {
                dialog.Title = view.Title;
                var errored = view.Tone == CancelTone.Error;
                body.Text = view.Body;
                body.Visibility = view.Body.Length > 0 && !errored ? Visibility.Visible : Visibility.Collapsed;
                var message = errored ? string.Join(" ", new[] { view.Body, view.Warning }.Where(text => text.Length > 0)) : view.Warning;
                notice.Severity = errored ? InfoBarSeverity.Error : InfoBarSeverity.Warning;
                notice.Message = message;
                notice.IsOpen = message.Length > 0;
                Show(status, view.Status);
                Show(detail, view.Detail);
                Show(footnote, view.Footnote);
            });
            Safely("Le dialogue d'annulation n'a pas pu mettre à jour ses boutons.", () =>
            {
                dialog.PrimaryButtonText = view.ConfirmText ?? "";
                dialog.SecondaryButtonText = view.LinkText;
                dialog.CloseButtonText = view.CloseText ?? "";
            });
        }

        private static void Show(TextBlock block, string text)
        {
            block.Text = text;
            block.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnConfirm(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            args.Cancel = true;
            Safely("La page n'a pas pu être mise à jour après l'envoi de l'annulation.", () => { if (flow.Confirm()) cancelSent(); });
        }

        private void OnLink(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            args.Cancel = true;
            var end = flow.Run.End;
            var copy = flow.View.Link == CancelLink.CopyDetails;
            var done = copy ? end is { Details.Length: > 0 } && copyDetails(end.Details) : end?.WorkDirectory is { } directory && openLogs(directory);
            Safely("Le dialogue d'annulation n'a pas pu mettre à jour son lien.",
                () => dialog.SecondaryButtonText = copy ? done ? "Copié" : "Échec de copie" : done ? "Ouvrir les journaux" : "Impossible d'ouvrir les journaux");
        }

        private void OnClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
        {
            if (!flow.CanClose) args.Cancel = true;
        }

        private void Safely(string what, Action step)
        {
            try { step(); }
            catch (Exception error) { report(what, error); }
        }
    }

    private async void OnSaveRecipe(object sender, RoutedEventArgs args) => await RunOperation(OperationKind.Recipe, async _ =>
    {
        var recipe = CreateRecipe();
        recipe.ValidateOptions();
        var path = await SaveFile("tiny11-recipe", ".json");
        if (path is not null) { recipe.Write(path); ShowNotice("Recette enregistrée.", "Elle contient vos sélections et chemins locaux, sans médias téléchargés ni identifiants."); }
    });
    private async void OnLoadRecipe(object sender, RoutedEventArgs args) => await RunOperation(OperationKind.Recipe, async _ =>
    {
        var path = await PickFile(".json");
        if (path is null) return;
        RestoreRecipe(BuildRecipe.Read(path));
        ShowNotice("Recette chargée.", "Inspectez à nouveau l'image source. L'acceptation des risques Core/avancés doit être renouvelée.");
    });

    private void RestoreRecipe(BuildRecipe recipe)
    {
        SourcePathBox.Text = recipe.SourcePath;
        WorkspaceBox.Text = recipe.WorkspacePath;
        OutputBox.Text = recipe.OutputPath;
        servicingTool = ToolLocator.FindDism();
        applyingMode = true;
        ModeChoice.SelectedIndex = (int)CleanupCatalog.ModeForRecipe(recipe);
        CustomCoreCheck.IsOn = recipe.Flavor == BuildFlavor.Core && SelectedMode == BuildMode.Custom;
        foreach (var choice in options) choice.Selected = recipe.SelectedOptions.Contains(choice.Option.Id);
        applyingMode = false;
        CompressionChoice.SelectedIndex = (int)recipe.Compression;
        CompactCheck.IsOn = recipe.CompactInstallation;
        NetFxCheck.IsOn = recipe.EnableNetFx3;
        LocalAccountCheck.IsOn = recipe.PreferLocalAccount;
        if (!string.IsNullOrWhiteSpace(recipe.UnattendUsername)) LocalUserNameBox.Text = recipe.UnattendUsername;
        LocalPasswordBox.Password = recipe.UnattendPassword ?? "";
        if (!string.IsNullOrWhiteSpace(recipe.UnattendComputerName)) ComputerNameBox.Text = recipe.UnattendComputerName;
        AutoLogonCheck.IsOn = recipe.AutoLogon;
        LocalAccountSettingsPanel.Visibility = recipe.PreferLocalAccount ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        KeepFilesCheck.IsOn = recipe.KeepWorkingFiles;
        TurboCheck.IsOn = recipe.TurboMode;
        RedundantDataCheck.IsOn = recipe.RemoveRedundantData;
        OmitSetupCheck.IsOn = recipe.OmitNetFx3SetupPayload;
        BrowserChoice.SelectedIndex = (recipe.SelectedBrowser ?? "").ToLowerInvariant() switch
        {
            "firefox" => 1,
            "brave" => 2,
            "chrome" => 3,
            "librewolf" => 4,
            _ => 0
        };
        VlcCheck.IsChecked = recipe.InstallVlc;
        SevenZipCheck.IsChecked = recipe.InstallSevenZip;
        NotepadPlusCheck.IsChecked = recipe.InstallNotepadPlus;
        LibreOfficeCheck.IsChecked = recipe.InstallLibreOffice;
        DriversPathBox.Text = recipe.DriverPath;
        DefenderControlPathBox.Text = recipe.DefenderControlPath;
        InstallMadTweakCheck.IsOn = recipe.InstallMadTweak;
        if (!string.IsNullOrWhiteSpace(recipe.MadTweakPath))
            MadTweakPathBox.Text = recipe.MadTweakPath;
        GamerModeCheck.IsOn = recipe.GamerMode;
        DisableDefenderCheck.IsOn = recipe.DisableDefender;
        DisableUpdatesFirstBootCheck.IsOn = recipe.DisableUpdatesAtFirstBoot;
        pendingActions = recipe.ExpertActions;
        pendingImageIndex = recipe.ImageIndex;
        pendingSourcePath = recipe.SourcePath;
        if (pendingActions.Length > 0) ExpertPanel.Visibility = Visibility.Visible;
        coreAcknowledged = false;
        ExpertConsent.IsChecked = false;
        UpdateModePresentation();
        reached = 1;
        GoStep(1);
        UpdateCounts();
    }

    private async void OnRecover(object sender, RoutedEventArgs args)
    {
        if (busy || modalOpen) return;
        if (!ToolLocator.IsAdministrator)
        {
            ShowNotice("Privilèges d'administrateur requis.", "Fermez et rouvrez Tiny11Builder.exe avec élévation administrateur pour nettoyer un espace de travail.", InfoBarSeverity.Warning);
            return;
        }
        var path = await PickFile(".json");
        if (path is null) return;
        if (JobCheckpoint.Exists(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ""))
        {
            if (await Confirm("Cette tâche a une progression enregistrée",
                "Le nettoyage supprime la copie de travail d'une tâche, ce qui n'est pas fait pour une tâche avec progression enregistrée. " +
                "La reprise permet de continuer la création à partir de cette étape.\n\nRien n'a été modifié.", "Reprendre plutôt"))
                await ResumeJobAsync(path);
            return;
        }
        if (!await Confirm("Nettoyer cet espace de travail interrompu ?",
            "Sélectionnez uniquement le fichier job.json de la tâche tiny11 interrompue. Cela libère la copie montée et supprime les fichiers temporaires. " +
            "Les journaux, recettes, rapports et votre ISO source sont conservés. Une tâche dont l'image a déjà été enregistrée a une progression et n'est jamais supprimée ici ; " +
            "utilisez 'Reprendre la création' pour cela.",
            "Nettoyer la copie de travail")) return;
        await RunOperation(OperationKind.Recover, async token =>
        {
            Working("Nettoyage de l'espace de travail en cours…");
            var dism = ToolLocator.FindDism();
            ToolLocator.Validate(dism, "dism.exe");
            await Task.Run(() => engine.RecoverAsync(path, dism, token), token);
            ShowNotice("Espace de travail nettoyé.", "La copie de travail interrompue a été libérée et nettoyée. Votre source et les journaux ont été conservés.");
        });
    }

    private async void OnResume(object sender, RoutedEventArgs args)
    {
        if (busy || modalOpen) return;
        var path = await PickFile(".json");
        if (path is not null) await ResumeJobAsync(path);
    }

    private async void OnResumeStopped(object sender, RoutedEventArgs args)
    {
        if (busy || modalOpen || workDirectory is null) return;
        await ResumeJobAsync(Path.Combine(workDirectory, ResumeSupport.JobFileName));
    }

    private async Task ResumeJobAsync(string jobFile)
    {
        var candidate = ResumeSupport.Inspect(jobFile, JobCheckpoint.Exists, JobCheckpoint.RecipeFileName);
        if (candidate.Recipe is not { } original)
        {
            ShowNotice("Cette création ne peut pas être reprise.", candidate.Problem ?? ResumeSupport.NoCheckpoint, InfoBarSeverity.Warning);
            return;
        }
        if (!ToolLocator.IsAdministrator)
        {
            ShowNotice("Privilèges d'administrateur requis.", "Fermez et rouvrez Tiny11Builder.exe avec élévation administrateur, puis choisissez 'Reprendre la création'. Rien n'a été modifié.", InfoBarSeverity.Warning);
            return;
        }
        if (!await Confirm("Reprendre cette création ?", ResumeSupport.Describe(original), "Reprendre")) return;
        var recipe = original;
        if (File.Exists(recipe.OutputPath))
        {
            var picked = await SaveFile(Path.GetFileNameWithoutExtension(recipe.OutputPath), ".iso");
            if (picked is null)
            {
                ShowNotice("Reprise non démarrée.", "Le fichier de sortie existe déjà. Choisissez un nouveau nom de fichier pour continuer.", InfoBarSeverity.Warning);
                return;
            }
            recipe = recipe with { OutputPath = picked };
        }
        resumeJobFile = candidate.JobFile;
        await RunOperation(OperationKind.Resume, async token =>
        {
            completed = false;
            GoStep(5);
            ResetBuildPage();
            UpdateBuildDescription(recipe);
            workDirectory = candidate.JobDirectory;
            BuildActivity.IsActive = true;
            BuildBar.IsIndeterminate = true;
            CurrentMessage.Text = "Préparation des outils d'image…";
            await EnsureToolsAsync(token);
            recipe = recipe with { DismPath = servicingTool, OscdimgPath = isoTool };
            new BuildHost().ValidateTools(recipe);
            BeginBuildRun("Reprise en cours");
            var reporter = BuildProgressReporter();
            var built = await Task.Run(() => engine.ResumeAsync(candidate.JobFile, recipe, reporter, token), token);
            ShowBuildResult(built, resumed: true, turbo: recipe.TurboMode);
        });
    }

    private void OnOpenOutputFolder(object sender, RoutedEventArgs args)
    {
        if (result is not null) OpenFolder(Path.GetDirectoryName(result.IsoPath)!);
    }
    private void OnOpenLogs(object sender, RoutedEventArgs args)
    {
        if (workDirectory is null) return;
        if (!Directory.Exists(workDirectory))
        {
            ShowNotice("Le dossier de la tâche est introuvable.", $"{workDirectory} n'existe plus.", InfoBarSeverity.Warning);
            return;
        }
        OpenFolder(workDirectory);
    }
    private void OpenFolder(string path)
    {
        try { using var process = Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true, Arguments = $"\"{path}\"" }); }
        catch (Exception error) { ShowNotice("Impossible d'ouvrir le dossier.", ErrorText.Reason(error), InfoBarSeverity.Error); }
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs args) => ApplyTheme();

    private bool HighContrast
    {
        get
        {
            try { return accessibility.HighContrast; }
            catch (COMException) { return false; }
        }
    }

    private void ApplyTheme()
    {
        if (Root is null) return;
        Root.RequestedTheme = HighContrast ? ElementTheme.Default : ThemeChoice.SelectedIndex switch
        {
            1 => ElementTheme.Light, 2 => ElementTheme.Dark, _ => ElementTheme.Default
        };
    }

    private void OnAccentColorChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!initialized || AccentColorChoice?.SelectedItem is not ComboBoxItem item) return;
        if (item.Tag is string hex && !string.IsNullOrWhiteSpace(hex))
        {
            ApplyAccentColor(hex);
        }
    }

    private void ApplyAccentColor(string hex)
    {
        try
        {
            hex = hex.TrimStart('#');
            byte a = 255;
            byte r = Convert.ToByte(hex[..2], 16);
            byte g = Convert.ToByte(hex.Substring(2, 2), 16);
            byte b = Convert.ToByte(hex.Substring(4, 2), 16);
            var color = Windows.UI.Color.FromArgb(a, r, g, b);
            var brush = new SolidColorBrush(color);

            Root.Resources["SystemColorAccentColor"] = color;
            Root.Resources["AccentTextFillColorPrimaryBrush"] = brush;
            Root.Resources["AccentFillColorDefaultBrush"] = brush;

            var secondaryColor = Windows.UI.Color.FromArgb(200, r, g, b);
            Root.Resources["AccentFillColorSecondaryBrush"] = new SolidColorBrush(secondaryColor);
        }
        catch (Exception) { }
    }

    private void ApplyContrast()
    {
        var contrast = HighContrast;
        if (contrast == appliedContrast) return;
        appliedContrast = contrast;
        StepNumber.Visibility = contrast ? Visibility.Collapsed : Visibility.Visible;
        ToolTipService.SetToolTip(ThemeChoice, contrast ? "Le contraste élevé de Windows est actif, les couleurs suivent vos paramètres système." : null);
        ApplyTheme();
        UpdateNavigation();
    }
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

public sealed class StateVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is BuildOperationState state && parameter is string names && names.Split('|').Contains(state.ToString()) ?
            Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

internal sealed class UiProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
