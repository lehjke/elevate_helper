using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Numerics;
using ElevateHelperWinUI.Models;
using ElevateHelperWinUI.Services;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace ElevateHelperWinUI.Views;

public sealed partial class MainPage : Page
{
    private const float LeftColumnClipRadius = 24f;
    private static readonly TimeSpan ProjectPathAnalysisDebounce = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan MetricsReadThrottle = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan DismissUndoLifetime = TimeSpan.FromSeconds(10);
    private readonly AppLocalizationService localizationService = AppLocalizationService.Instance;
    private readonly AppUpdateService updateService = new();
    private readonly IElevateProjectEditorService projectEditorService = new ElevateProjectEditorService();
    private readonly IElevateIntegrationService integrationService = new ElevateIntegrationService();
    private readonly IElevateProcessingService processingService = new ElevateProcessingService();
    private readonly IElevateReportService reportService = new ElevateReportService();
    private readonly ElevateProjectBatchDiscoveryService projectBatchDiscoveryService = new();
    private readonly ElevateResultMetricsService resultMetricsService = new();
    private readonly JobQueuePersistenceService jobQueuePersistenceService = new();
    private readonly SemaphoreSlim reportExecutionLock = new(1, 1);
    private readonly SemaphoreSlim dialogCoordinator = new(1, 1);
    private readonly ProcessingFolderLeaseRegistry processingFolderLeases = new();
    private readonly CancellationTokenSource applicationLifetimeSource = new();
    private readonly object activeJobTasksSync = new();
    private readonly HashSet<Task> activeJobTasks = [];
    private readonly ObservableCollection<JobProgressViewModel> jobs = [];
    private readonly ObservableCollection<FloorEditorRowViewModel> editorFloors = [];
    private readonly ObservableCollection<CarEditorRowViewModel> editorCars = [];
    private ElevateProjectEditorWindow? editorWindow;
    private string? editorWindowPath;
    private BuildingType? editorWindowBuildingType;
    private ElevateProjectEditorDocument? loadedEditorDocument;
    private bool suppressBuildingTypeStatus;
    private bool updateCheckStarted;
    private ProjectInputMode projectInputMode = ProjectInputMode.Standard;
    private int nextJobId = 1;
    private int shutdownStarted;
    private bool restoringInterruptedJobs;
    private CancellationTokenSource? projectPathAnalysisSource;
    private int projectPathAnalysisGeneration;
    private ProjectPathAnalysis? lastProjectPathAnalysis;
    private readonly Dictionary<string, MetricsReadState> metricsReadStates =
        new(StringComparer.OrdinalIgnoreCase);
    private int batchLaunchFlowActive;
    private int statusAnnouncementGeneration;
    private AppLanguage statusMessageLanguage;
    private JobProgressViewModel? dismissedJob;
    private int dismissedJobIndex = -1;
    private CancellationTokenSource? dismissUndoSource;

    public MainPage()
    {
        this.InitializeComponent();
        StatusInfoBar.Message = Text.Ready;
        statusMessageLanguage = localizationService.CurrentLanguage;
        localizationService.LanguageChanged += OnLanguageChanged;
        processingService.SetElevateWindowsHidden(HideElevateWindowsToggle.IsOn);

        UpdateLanguageSelector();
        OfficeRadioButton.IsChecked = true;
        UpdateProjectInputModeSelection();
        UpdateProjectModeControlsText();

        if (App.MainWindow is not null)
        {
            App.MainWindow.Title = Text.WindowTitle;
        }

        ResetEditorStatus();
        UpdateModeButtons(BuildingType.Office);
        UpdateProjectInputModeVisibility();
        RefreshIntegrationStatus(showStatusMessage: true);
        RestoreInterruptedJobs();
        RefreshJobsSummary();

        Loaded += OnMainPageLoaded;
        LeftColumnClipBorder.SizeChanged += OnLeftColumnClipBorderSizeChanged;
        ApplyLeftColumnClip();
    }

    public ObservableCollection<JobProgressViewModel> Jobs => jobs;

    public ObservableCollection<FloorEditorRowViewModel> EditorFloors => editorFloors;

    public ObservableCollection<CarEditorRowViewModel> EditorCars => editorCars;

    public IReadOnlyList<LanguageOption> LanguageOptions { get; } =
    [
        new(AppLanguage.English, "English"),
        new(AppLanguage.Russian, "Русский"),
    ];

    public AppLocalizationService.AppTextCatalog Text => localizationService.CurrentText;

    public string AppVersionLabel => $"v{updateService.CurrentVersion}";

    public async Task<bool> ConfirmShutdownAsync()
    {
        int activeJobCount = Jobs.Count(job => !job.IsFinished);
        bool hasUnsavedEditorChanges = editorWindow?.HasUnsavedChanges == true;
        bool editorIsBusy = editorWindow?.IsBusy == true;
        if (activeJobCount == 0 && !hasUnsavedEditorChanges && !editorIsBusy)
        {
            return true;
        }

        string title = localizationService.CurrentLanguage == AppLanguage.Russian
            ? "Завершить работу?"
            : "Exit Elevate Helper?";
        string activeJobsText = activeJobCount > 0
            ? localizationService.CurrentLanguage == AppLanguage.Russian
                ? $"Будет остановлено задач: {activeJobCount}."
                : $"Active or queued jobs that will be stopped: {activeJobCount}."
            : string.Empty;
        string unsavedText = hasUnsavedEditorChanges
            ? localizationService.CurrentLanguage == AppLanguage.Russian
                ? "Несохранённые изменения редактора будут потеряны."
                : "Unsaved editor changes will be discarded."
            : string.Empty;
        string editorBusyText = editorIsBusy
            ? localizationService.CurrentLanguage == AppLanguage.Russian
                ? "Текущая операция редактора будет прервана."
                : "The current editor operation will be interrupted."
            : string.Empty;
        string message = string.Join(
            Environment.NewLine,
            new[] { activeJobsText, unsavedText, editorBusyText }.Where(value => !string.IsNullOrWhiteSpace(value)));

        ContentDialog dialog = new()
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = message,
            PrimaryButtonText = localizationService.CurrentLanguage == AppLanguage.Russian
                ? "Остановить и выйти"
                : "Stop and exit",
            CloseButtonText = localizationService.CurrentLanguage == AppLanguage.Russian
                ? "Продолжить работу"
                : "Keep working",
            DefaultButton = ContentDialogButton.Close,
        };

        ContentDialogResult result = await ShowCoordinatedDialogAsync(dialog);
        if (result != ContentDialogResult.Primary)
        {
            return false;
        }

        editorWindow?.AllowCloseForShutdown();
        return true;
    }

    public void BeginShutdownFeedback()
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            _ = DispatcherQueue.TryEnqueue(BeginShutdownFeedback);
            return;
        }

        string message = Text.ShutdownStoppingStatus;
        BusyRing.IsActive = true;
        AutomationProperties.SetLiveSetting(BusyTextBlock, AutomationLiveSetting.Assertive);
        UpdateLiveRegionText(BusyTextBlock, message);
        SetStatus(message, InfoBarSeverity.Informational);
        RootPage.IsEnabled = false;
    }

    public async Task ShutdownAsync()
    {
        if (Interlocked.Exchange(ref shutdownStarted, 1) != 0)
        {
            return;
        }

        applicationLifetimeSource.Cancel();
        projectPathAnalysisSource?.Cancel();
        dismissUndoSource?.Cancel();
        foreach (MetricsReadState metricsReadState in metricsReadStates.Values)
        {
            metricsReadState.Source?.Cancel();
        }

        foreach (JobProgressViewModel job in Jobs.Where(job => job.CanStop).ToList())
        {
            job.RequestStop(localizationService);
        }

        using CancellationTokenSource shutdownTimeout = new(TimeSpan.FromSeconds(20));
        if (editorWindow is ElevateProjectEditorWindow currentEditorWindow)
        {
            try
            {
                await currentEditorWindow.PrepareForShutdownAsync(shutdownTimeout.Token);
            }
            catch (Exception)
            {
            }

            currentEditorWindow.Close();
        }

        try
        {
            await processingService.ShutdownAsync(shutdownTimeout.Token);
        }
        catch (Exception)
        {
        }

        try
        {
            await reportService.ShutdownAsync(shutdownTimeout.Token);
        }
        catch (Exception)
        {
        }

        Task[] runningTasks;
        lock (activeJobTasksSync)
        {
            runningTasks = activeJobTasks.ToArray();
        }

        if (runningTasks.Length > 0)
        {
            try
            {
                await Task.WhenAll(runningTasks).WaitAsync(shutdownTimeout.Token);
            }
            catch (Exception)
            {
            }
        }

        try
        {
            if (await reportExecutionLock.WaitAsync(TimeSpan.FromSeconds(5), shutdownTimeout.Token))
            {
                _ = reportExecutionLock.Release();
            }
        }
        catch (OperationCanceledException)
        {
        }

        try
        {
            await reportService.ShutdownAsync(CancellationToken.None);
        }
        catch (Exception)
        {
        }

        localizationService.LanguageChanged -= OnLanguageChanged;
    }

    private async void OnMainPageLoaded(object sender, RoutedEventArgs e)
    {
        if (updateCheckStarted)
        {
            return;
        }

        updateCheckStarted = true;
        await CheckForApplicationUpdateAsync();
    }

    private async Task CheckForApplicationUpdateAsync()
    {
        AppUpdateInfo? updateInfo;
        try
        {
            updateInfo = await updateService.CheckForUpdateAsync(applicationLifetimeSource.Token);
        }
        catch
        {
            return;
        }

        if (updateInfo is null)
        {
            return;
        }

        ContentDialog dialog = new()
        {
            XamlRoot = XamlRoot,
            Title = Text.UpdateAvailableTitle,
            Content = string.Format(
                localizationService.CurrentCulture,
                Text.UpdateAvailableMessageFormat,
                updateInfo.CurrentVersion,
                updateInfo.LatestVersion),
            PrimaryButtonText = Text.UpdateInstallButton,
            CloseButtonText = Text.UpdateLaterButton,
            DefaultButton = ContentDialogButton.Primary,
        };

        ContentDialogResult result = await ShowCoordinatedDialogAsync(dialog);
        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            await DownloadAndInstallUpdateWithProgressAsync(updateInfo);
        }
        catch (Exception ex)
        {
            SetStatus(BuildExceptionMessage(ex), InfoBarSeverity.Error);
        }
    }

    private async Task DownloadAndInstallUpdateWithProgressAsync(AppUpdateInfo updateInfo)
    {
        TextBlock progressTextBlock = new()
        {
            Text = Text.UpdatePreparingStatus,
            TextWrapping = TextWrapping.Wrap,
        };
        ProgressBar progressBar = new()
        {
            IsIndeterminate = true,
            Minimum = 0,
            Maximum = 100,
        };
        StackPanel progressContent = new()
        {
            Spacing = 12,
            Children =
            {
                progressTextBlock,
                progressBar,
            },
        };
        ContentDialog progressDialog = new()
        {
            XamlRoot = XamlRoot,
            Title = Text.UpdateProgressTitle,
            Content = progressContent,
            DefaultButton = ContentDialogButton.None,
        };
        TaskCompletionSource<string> updateTaskCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Progress<AppUpdateProgress> progress = new(updateProgress =>
            ApplyUpdateProgress(updateProgress, progressTextBlock, progressBar));

        progressDialog.Opened += async (_, _) =>
        {
            try
            {
                SetStatus(Text.UpdatePreparingStatus, InfoBarSeverity.Informational);
                string installerPath = await updateService.DownloadAndStartUpdateAsync(
                    updateInfo,
                    progress,
                    applicationLifetimeSource.Token);
                updateTaskCompletion.TrySetResult(installerPath);
            }
            catch (Exception ex)
            {
                updateTaskCompletion.TrySetException(ex);
            }
            finally
            {
                progressDialog.Hide();
            }
        };

        await ShowCoordinatedDialogAsync(progressDialog);
        _ = await updateTaskCompletion.Task;
        SetStatus(Text.UpdateStartedStatus, InfoBarSeverity.Success);
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        App.MainWindow?.Close();
    }

    private async Task<ContentDialogResult> ShowCoordinatedDialogAsync(
        ContentDialog dialog,
        CancellationToken cancellationToken = default)
    {
        await dialogCoordinator.WaitAsync(cancellationToken);
        try
        {
            return await dialog.ShowAsync();
        }
        finally
        {
            _ = dialogCoordinator.Release();
        }
    }

    private void ApplyUpdateProgress(
        AppUpdateProgress updateProgress,
        TextBlock progressTextBlock,
        ProgressBar progressBar)
    {
        switch (updateProgress.Stage)
        {
            case AppUpdateProgressStage.Downloading:
                if (updateProgress.Percentage is double percentage)
                {
                    progressBar.IsIndeterminate = false;
                    progressBar.Value = percentage;
                    progressTextBlock.Text = string.Format(
                        localizationService.CurrentCulture,
                        Text.UpdateDownloadingProgressFormat,
                        percentage);
                    SetStatus(progressTextBlock.Text, InfoBarSeverity.Informational);
                }
                else
                {
                    progressBar.IsIndeterminate = true;
                    progressTextBlock.Text = Text.UpdateDownloadingStatus;
                    SetStatus(Text.UpdateDownloadingStatus, InfoBarSeverity.Informational);
                }

                break;
            case AppUpdateProgressStage.Verifying:
                progressBar.IsIndeterminate = true;
                progressTextBlock.Text = Text.UpdateVerifyingStatus;
                SetStatus(Text.UpdateVerifyingStatus, InfoBarSeverity.Informational);
                break;
            case AppUpdateProgressStage.StartingInstaller:
                progressBar.IsIndeterminate = true;
                progressTextBlock.Text = Text.UpdateStartingInstallerStatus;
                SetStatus(Text.UpdateStartingInstallerStatus, InfoBarSeverity.Informational);
                break;
            default:
                progressBar.IsIndeterminate = true;
                progressTextBlock.Text = Text.UpdatePreparingStatus;
                SetStatus(Text.UpdatePreparingStatus, InfoBarSeverity.Informational);
                break;
        }
    }

    private void OnLeftColumnClipBorderSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyLeftColumnClip(e.NewSize.Width, e.NewSize.Height);
    }

    private void ApplyLeftColumnClip()
    {
        ApplyLeftColumnClip(LeftColumnClipBorder.ActualWidth, LeftColumnClipBorder.ActualHeight);
    }

    private void ApplyLeftColumnClip(double width, double height)
    {
        var visual = ElementCompositionPreview.GetElementVisual(LeftColumnClipBorder);
        if (width <= 0 || height <= 0)
        {
            visual.Clip = null;
            return;
        }

        float radiusValue = Math.Min(LeftColumnClipRadius, (float)Math.Min(width, height) / 2f);
        Vector2 radius = new(radiusValue, radiusValue);
        Vector2 size = new((float)width, (float)height);
        var geometry = visual.Compositor.CreateRoundedRectangleGeometry();

        geometry.Size = size;
        geometry.CornerRadius = radius;
        visual.Clip = visual.Compositor.CreateGeometricClip(geometry);
    }

    private void OnRunButtonClick(object sender, RoutedEventArgs e)
    {
        if (projectInputMode == ProjectInputMode.ProjectBatch)
        {
            OnRunProjectBatchButtonClick(sender, e);
            return;
        }

        if (!TryGetInputs(out string path, out BuildingType buildingType))
        {
            return;
        }

        if (!TryEnsureEditorReadyForRun())
        {
            return;
        }

        if (!TryEnsureIntegrationForLaunch())
        {
            return;
        }

        StartProcessingJob(path, buildingType, includeLunchPeak: true);
    }

    private void OnRunMorningOnlyButtonClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetInputs(out string path, out BuildingType buildingType))
        {
            return;
        }

        if (!TryEnsureEditorReadyForRun())
        {
            return;
        }

        if (!TryEnsureIntegrationForLaunch())
        {
            return;
        }

        if (buildingType != BuildingType.Office)
        {
            SetStatus(Text.OfficeMorningOnlyMessage, InfoBarSeverity.Warning);
            return;
        }

        StartProcessingJob(path, buildingType, includeLunchPeak: false);
    }

    private async void OnRunProjectBatchButtonClick(object sender, RoutedEventArgs e)
    {
        if (Interlocked.Exchange(ref batchLaunchFlowActive, 1) != 0)
        {
            SetStatus(Text.ProjectBatchLaunchAlreadyPreparingMessage, InfoBarSeverity.Informational);
            return;
        }

        RunProjectBatchButton.IsEnabled = false;
        try
        {
            if (!TryEnsureEditorReadyForRun())
            {
                return;
            }

            if (!TryGetProjectBatchInputs(out string projectRoot, out int parallelRuns))
            {
                return;
            }

            if (!TryEnsureIntegrationForLaunch())
            {
                return;
            }

            SetStatus(Text.ProjectBatchAnalyzingStatus, InfoBarSeverity.Informational);

            ProjectBatchDiscoveryResult discoveryResult = await Task.Run(
                () => projectBatchDiscoveryService.Discover(projectRoot),
                applicationLifetimeSource.Token);

            IReadOnlyList<ProjectBatchJob>? manualJobs = await ResolveUnknownProjectBatchJobsAsync(
                projectRoot,
                discoveryResult.UnknownElvxFiles);
            if (manualJobs is null)
            {
                return;
            }

            List<ProjectBatchJob> batchJobs = discoveryResult.Jobs
                .Concat(manualJobs)
                .OrderBy(job => job.BuildingTypeFolderName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(job => job.GroupName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            bool includeOfficeLunchPeak = ShouldIncludeProjectBatchOfficeLunchPeak();

            if (batchJobs.Count == 0)
            {
                SetStatus(Text.ProjectBatchNoJobsMessage, InfoBarSeverity.Warning);
                return;
            }

            if (TryFindOverlappingBatchJobs(
                    batchJobs,
                    out ProjectBatchJob? firstOverlappingJob,
                    out ProjectBatchJob? secondOverlappingJob))
            {
                string message = string.Format(
                    localizationService.CurrentCulture,
                    Text.ProjectBatchOverlapFormat,
                    firstOverlappingJob!.WorkingFolder,
                    secondOverlappingJob!.WorkingFolder);
                SetStatus(message, InfoBarSeverity.Error);
                return;
            }

            if (!await ConfirmProjectBatchJobsAsync(batchJobs, discoveryResult.Warnings, includeOfficeLunchPeak))
            {
                return;
            }

            StartProjectBatchJobs(batchJobs, parallelRuns, discoveryResult.Warnings.Count, includeOfficeLunchPeak);
        }
        catch (OperationCanceledException) when (applicationLifetimeSource.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            SetStatus(BuildExceptionMessage(ex), InfoBarSeverity.Error);
        }
        finally
        {
            RunProjectBatchButton.IsEnabled = true;
            Interlocked.Exchange(ref batchLaunchFlowActive, 0);
        }
    }

    private void OnRunKeyboardAcceleratorInvoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        OnRunButtonClick(RunButton, new RoutedEventArgs());
        args.Handled = true;
    }

    private void OnStopAllKeyboardAcceleratorInvoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        OnStopAllJobsButtonClick(StopAllJobsButton, new RoutedEventArgs());
        args.Handled = true;
    }

    private static bool TryFindOverlappingBatchJobs(
        IReadOnlyList<ProjectBatchJob> jobs,
        out ProjectBatchJob? first,
        out ProjectBatchJob? second)
    {
        for (int firstIndex = 0; firstIndex < jobs.Count; firstIndex++)
        {
            for (int secondIndex = firstIndex + 1; secondIndex < jobs.Count; secondIndex++)
            {
                if (!ProcessingFolderLeaseRegistry.PathsOverlap(
                        jobs[firstIndex].WorkingFolder,
                        jobs[secondIndex].WorkingFolder))
                {
                    continue;
                }

                first = jobs[firstIndex];
                second = jobs[secondIndex];
                return true;
            }
        }

        first = null;
        second = null;
        return false;
    }

    private async void OnBrowseFolderButtonClick(object sender, RoutedEventArgs e)
    {
        string? folderPath = await PickFolderPathAsync();
        if (folderPath is null)
        {
            return;
        }

        PathTextBox.Text = folderPath;
        loadedEditorDocument = null;
        ResetEditorStatus();
    }

    private async void OnBrowseProjectBatchFolderButtonClick(object sender, RoutedEventArgs e)
    {
        string? folderPath = await PickFolderPathAsync();
        if (folderPath is not null)
        {
            ProjectBatchPathTextBox.Text = folderPath;
        }
    }

    private void OnProjectBatchUnlimitedRunsOptionTapped(object sender, TappedRoutedEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source &&
            IsDescendantOf(source, ProjectBatchUnlimitedRunsCheckBox))
        {
            return;
        }

        ProjectBatchUnlimitedRunsCheckBox.IsChecked = ProjectBatchUnlimitedRunsCheckBox.IsChecked != true;
    }

    private void OnProjectBatchMorningOnlyOptionTapped(object sender, TappedRoutedEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source &&
            IsDescendantOf(source, ProjectBatchMorningOnlyCheckBox))
        {
            return;
        }

        ProjectBatchMorningOnlyCheckBox.IsChecked = ProjectBatchMorningOnlyCheckBox.IsChecked != true;
    }

    private void OnSingleProjectModeButtonClick(object sender, RoutedEventArgs e)
    {
        SetProjectInputMode(ProjectInputMode.Standard);
    }

    private void OnProjectBatchModeButtonClick(object sender, RoutedEventArgs e)
    {
        SetProjectInputMode(ProjectInputMode.ProjectBatch);
    }

    private void SetProjectInputMode(ProjectInputMode mode)
    {
        projectInputMode = mode;
        UpdateProjectInputModeSelection();
        SyncProjectBatchPathFromMainPath();
        UpdateProjectInputModeVisibility();

        SetStatus(
            mode == ProjectInputMode.ProjectBatch
                ? Text.ProcessingModeBatchStatus
                : Text.ProcessingModeSingleStatus,
            InfoBarSeverity.Informational);
    }

    private void UpdateProjectInputModeSelection()
    {
        bool isBatchMode = projectInputMode == ProjectInputMode.ProjectBatch;
        SingleProjectModeButton.IsChecked = !isBatchMode;
        ProjectBatchModeButton.IsChecked = isBatchMode;
    }

    private static bool IsDescendantOf(DependencyObject source, DependencyObject ancestor)
    {
        for (DependencyObject? current = source; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<string?> PickFolderPathAsync()
    {
        if (App.MainWindow is null)
        {
            return null;
        }

        FolderPicker picker = new();
        picker.FileTypeFilter.Add("*");

        IntPtr windowHandle = WindowNative.GetWindowHandle(App.MainWindow);
        InitializeWithWindow.Initialize(picker, windowHandle);

        Windows.Storage.StorageFolder? folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    private async void OnLoadEditorButtonClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetInputs(out string path, out BuildingType buildingType))
        {
            return;
        }

        try
        {
            ElevateProjectEditorDocument document = await LoadExistingProjectEditorDocumentAsync(path);
            ApplyEditorDocument(document);
            loadedEditorDocument = document;
            SaveEditorButton.IsEnabled = true;
            SetStatus(
                string.Format(
                    localizationService.CurrentCulture,
                    Text.EditorLoadSuccessFormat,
                    Path.GetFileName(document.SourcePath ?? document.TemplatePath ?? string.Empty)),
                InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            SetStatus(BuildExceptionMessage(ex), InfoBarSeverity.Error);
        }
    }

    private async void OnLoadEditorTemplateButtonClick(object sender, RoutedEventArgs e)
    {
        BuildingType? selectedType = GetSelectedBuildingType();
        if (!selectedType.HasValue)
        {
            SetStatus(Text.BuildingTypeRequiredMessage, InfoBarSeverity.Warning);
            return;
        }

        try
        {
            ElevateProjectEditorDocument document = await projectEditorService.LoadTemplate(selectedType.Value);
            ApplyEditorDocument(document);
            loadedEditorDocument = document;
            SaveEditorButton.IsEnabled = true;
            SetStatus(
                string.Format(
                    localizationService.CurrentCulture,
                    Text.EditorLoadSuccessFormat,
                    Path.GetFileName(document.TemplatePath ?? string.Empty)),
                InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            SetStatus(BuildExceptionMessage(ex), InfoBarSeverity.Error);
        }
    }

    private void OnEditorPathTextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateEditorOutputPreview();
        SyncProjectBatchPathFromMainPath();
        ScheduleProjectPathAnalysis();
    }

    private void OnEditorOutputFieldsChanged(object sender, TextChangedEventArgs e)
    {
        UpdateEditorOutputPreview();
    }

    private async void OnSaveEditorButtonClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetInputs(out string path, out BuildingType buildingType))
        {
            return;
        }

        if (loadedEditorDocument is null)
        {
            SetStatus(Text.EditorNotLoadedMessage, InfoBarSeverity.Warning);
            return;
        }

        if (!TryBuildEditorDocument(buildingType, out ElevateProjectEditorDocument? document))
        {
            return;
        }

        if (document is null)
        {
            SetStatus(Text.EditorNotLoadedMessage, InfoBarSeverity.Warning);
            return;
        }

        try
        {
            string outputPath = ResolveEditorOutputPath(path, document);
            ProcessingResult result = await projectEditorService.SaveAsync(document, outputPath);
            if (!result.Success)
            {
                SetStatus(FormatResultMessage(result), InfoBarSeverity.Error);
                return;
            }

            ElevateProjectEditorDocument refreshedDocument = await projectEditorService.LoadFile(outputPath);
            ApplyEditorDocument(refreshedDocument);
            loadedEditorDocument = refreshedDocument;
            SetStatus(
                string.Format(
                    localizationService.CurrentCulture,
                    Text.EditorSaveSuccessFormat,
                    Path.GetFileName(outputPath)),
                InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            SetStatus(BuildExceptionMessage(ex), InfoBarSeverity.Error);
        }
    }

    private async void OnReportButtonClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetInputs(out string path, out BuildingType buildingType))
        {
            return;
        }

        await ExecuteReportActionAsync(
            Text.GeneratingReport,
            async () =>
            {
                ProcessingResult result = await reportService.PrintReportAsync(
                    path,
                    buildingType,
                    applicationLifetimeSource.Token);
                HandleReportResult(result, Text.ReportGenerated);
            });
    }

    private async void OnMorningReportButtonClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetInputs(out string path, out BuildingType buildingType))
        {
            return;
        }

        await ExecuteReportActionAsync(
            Text.GeneratingMorningReport,
            async () =>
            {
                string morningPath = Path.Combine(path, "morning");
                ProcessingResult result = await reportService.PrintReportAsync(
                    morningPath,
                    buildingType,
                    applicationLifetimeSource.Token);
                HandleReportResult(result, Text.MorningReportGenerated);
            });
    }

    private async void OnLunchReportButtonClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetInputs(out string path, out BuildingType buildingType))
        {
            return;
        }

        await ExecuteReportActionAsync(
            Text.GeneratingLunchReport,
            async () =>
            {
                string lunchPath = Path.Combine(path, "lunch");
                ProcessingResult result = await reportService.PrintReportAsync(
                    lunchPath,
                    buildingType,
                    applicationLifetimeSource.Token);
                HandleReportResult(result, Text.LunchReportGenerated);
            });
    }

    private async void OnJobReportButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: JobProgressViewModel job })
        {
            return;
        }

        await ExecuteReportActionAsync(
            GetJobReportBusyText(job),
            async () =>
            {
                job.BeginManualReport(localizationService);
                RefreshJobsSummary();
                try
                {
                    ProcessingResult result = await PrintReportsForJobAsync(
                        job,
                        outputFolder: null,
                        applicationLifetimeSource.Token);
                    processingService.RecordReportOutcome(
                        job.JobPath,
                        result.Success,
                        result.Success ? null : FormatResultMessage(result));
                    if (result.Success)
                    {
                        job.CompleteManualReport(localizationService);
                    }
                    else
                    {
                        job.MarkReportFailed(result, localizationService);
                    }

                    RefreshJobsSummary();
                    HandleReportResult(result, GetJobReportSuccessText(job));
                }
                catch (OperationCanceledException) when (applicationLifetimeSource.IsCancellationRequested)
                {
                    job.CancelManualReport(localizationService);
                    RefreshJobsSummary();
                    throw;
                }
                catch (Exception ex)
                {
                    job.MarkReportFailed(ex, localizationService);
                    RefreshJobsSummary();
                    throw;
                }
            });
    }

    private void OnRetryJobButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: JobProgressViewModel job })
        {
            return;
        }

        if (!TryEnsureIntegrationForLaunch())
        {
            return;
        }

        string normalizedPath = NormalizeProcessingFolder(job.JobPath);
        if (!processingFolderLeases.TryAcquire(
                normalizedPath,
                job.LeaseOwnerId,
                out IDisposable? folderLease))
        {
            SetStatus(
                string.Format(
                    localizationService.CurrentCulture,
                    Text.RunFolderBusyMessage,
                    normalizedPath),
                InfoBarSeverity.Warning);
            return;
        }

        TrackJobTask(RunProcessingJobAsync(
            job,
            normalizedPath,
            job.BuildingType,
            job.IncludeLunchPeak,
            folderLease!,
            job.AutoGenerateReport,
            job.ReportOutputRoot,
            rerunExistingBatch: true));
    }

    private void OnRetryScenarioButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ScenarioProgressViewModel scenario } ||
            !scenario.CanRetry)
        {
            return;
        }

        if (!TryEnsureIntegrationForLaunch())
        {
            return;
        }

        JobProgressViewModel job = scenario.Job;
        string scenarioFolderName = scenario.ScenarioKind == JobScenarioKind.Lunch
            ? "lunch"
            : "morning";
        string scenarioPath = NormalizeProcessingFolder(
            Path.Combine(job.JobPath, scenarioFolderName));

        if (!processingFolderLeases.TryAcquire(
                scenarioPath,
                job.LeaseOwnerId,
                out IDisposable? folderLease))
        {
            SetStatus(
                string.Format(
                    localizationService.CurrentCulture,
                    Text.RunFolderBusyMessage,
                    scenarioPath),
                InfoBarSeverity.Warning);
            return;
        }

        TrackJobTask(RetryScenarioAsync(job, scenario, scenarioPath, folderLease!));
    }

    private async Task RetryScenarioAsync(
        JobProgressViewModel job,
        ScenarioProgressViewModel scenario,
        string scenarioPath,
        IDisposable folderLease)
    {
        using CancellationTokenSource stopSource = CancellationTokenSource.CreateLinkedTokenSource(
            applicationLifetimeSource.Token);
        job.AttachStopSource(stopSource);
        job.MarkScenarioRetryRunning(scenario, localizationService);
        RefreshJobsSummary();
        SetStatus(
            string.Format(
                localizationService.CurrentCulture,
                Text.ScenarioRunStartedFormat,
                job.Title,
                scenario.Label),
            InfoBarSeverity.Informational);

        try
        {
            Progress<ElevateProgressInfo> progress = new(update => HandleProgressUpdate(job, update));
            ProcessingResult result = await processingService.RunExistingScenarioAsync(
                scenarioPath,
                progress,
                stopSource.Token);

            if (!result.Success)
            {
                string message = FormatResultMessage(result);
                job.MarkScenarioFailed(scenario, result, localizationService);
                if (job.PrimaryRunFinished)
                {
                    job.MarkFailed(result, localizationService);
                }

                SetStatus(message, InfoBarSeverity.Error);
                return;
            }

            job.MarkScenarioCompleted(scenario, localizationService);
            if (job.PrimaryRunFinished && job.AllScenariosCompleted)
            {
                await FinalizeRecoveredJobAsync(job);
            }
        }
        catch (OperationCanceledException) when (stopSource.IsCancellationRequested)
        {
            job.MarkStopped(localizationService);
        }
        catch (Exception ex)
        {
            string message = BuildExceptionMessage(ex);
            job.MarkScenarioFailed(scenario, ex, localizationService);
            if (job.PrimaryRunFinished)
            {
                job.MarkFailed(ex, localizationService);
            }

            SetStatus(message, InfoBarSeverity.Error);
        }
        finally
        {
            job.DetachStopSource(stopSource);
            folderLease.Dispose();
            RefreshJobsSummary();
        }
    }

    private void OnStopJobButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: JobProgressViewModel job } || !job.CanStop)
        {
            return;
        }

        job.RequestStop(localizationService);
        RefreshJobsSummary();
        SetStatus(
            string.Format(localizationService.CurrentCulture, Text.JobStoppingFormat, job.Title),
            InfoBarSeverity.Informational);
    }

    private async void OnStopQueuedJobsButtonClick(object sender, RoutedEventArgs e)
    {
        await ConfirmAndStopJobsAsync(
            Jobs.Where(job => job.CanStop && job.IsQueued).ToList(),
            queuedOnly: true);
    }

    private async void OnStopAllJobsButtonClick(object sender, RoutedEventArgs e)
    {
        await ConfirmAndStopJobsAsync(
            Jobs.Where(job => job.CanStop).ToList(),
            queuedOnly: false);
    }

    private async Task ConfirmAndStopJobsAsync(
        IReadOnlyList<JobProgressViewModel> jobsToStop,
        bool queuedOnly)
    {
        if (jobsToStop.Count == 0)
        {
            SetStatus(Text.NoStoppableJobsMessage, InfoBarSeverity.Informational);
            return;
        }

        bool isRussian = localizationService.CurrentLanguage == AppLanguage.Russian;
        ContentDialog dialog = new()
        {
            XamlRoot = XamlRoot,
            Title = queuedOnly
                ? isRussian ? "Остановить ожидающие задачи?" : "Stop queued jobs?"
                : isRussian ? "Остановить все задачи?" : "Stop all jobs?",
            Content = isRussian
                ? $"Будет остановлено задач: {jobsToStop.Count}. Уже созданные результаты останутся в рабочих папках."
                : $"Jobs that will be stopped: {jobsToStop.Count}. Existing results will remain in their working folders.",
            PrimaryButtonText = isRussian ? "Остановить" : "Stop",
            CloseButtonText = isRussian ? "Продолжить" : "Keep running",
            DefaultButton = ContentDialogButton.Close,
        };

        if (await ShowCoordinatedDialogAsync(dialog) != ContentDialogResult.Primary)
        {
            return;
        }

        List<JobProgressViewModel> confirmedJobs = jobsToStop
            .Where(job => job.CanStop && (!queuedOnly || job.IsQueued))
            .ToList();
        foreach (JobProgressViewModel job in confirmedJobs)
        {
            job.RequestStop(localizationService);
        }

        RefreshJobsSummary();
        SetStatus(
            string.Format(localizationService.CurrentCulture, Text.StopRequestedFormat, confirmedJobs.Count),
            InfoBarSeverity.Informational);
    }

    private void OnDismissJobButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: JobProgressViewModel job } || !job.CanDismiss)
        {
            return;
        }

        int removedIndex = Jobs.IndexOf(job);
        if (removedIndex < 0 || !Jobs.Remove(job))
        {
            return;
        }

        dismissUndoSource?.Cancel();
        dismissUndoSource?.Dispose();
        dismissUndoSource = new CancellationTokenSource();
        dismissedJob = job;
        dismissedJobIndex = removedIndex;
        RefreshJobsSummary();

        bool isRussian = localizationService.CurrentLanguage == AppLanguage.Russian;
        StatusActionButton.Content = isRussian ? "Отменить" : "Undo";
        StatusActionButton.Visibility = Visibility.Visible;
        SetStatus(
            string.Format(localizationService.CurrentCulture, Text.JobDismissedFormat, job.Title),
            InfoBarSeverity.Informational,
            preserveActionButton: true);
        FocusJobAtIndex(Math.Min(removedIndex, Jobs.Count - 1));
        _ = ExpireDismissUndoAsync(dismissUndoSource);
    }

    private void OnStatusActionButtonClick(object sender, RoutedEventArgs e)
    {
        JobProgressViewModel? job = dismissedJob;
        if (job is null)
        {
            return;
        }

        dismissUndoSource?.Cancel();
        dismissUndoSource?.Dispose();
        dismissUndoSource = null;
        int restoredIndex = Math.Clamp(dismissedJobIndex, 0, Jobs.Count);
        dismissedJob = null;
        dismissedJobIndex = -1;
        StatusActionButton.Visibility = Visibility.Collapsed;
        Jobs.Insert(restoredIndex, job);
        RefreshJobsSummary();
        SetStatus(
            string.Format(localizationService.CurrentCulture, Text.JobRestoredFormat, job.Title),
            InfoBarSeverity.Success);
        FocusJobAtIndex(restoredIndex);
    }

    private async Task ExpireDismissUndoAsync(CancellationTokenSource source)
    {
        try
        {
            await Task.Delay(DismissUndoLifetime, source.Token);
            if (!ReferenceEquals(dismissUndoSource, source))
            {
                return;
            }

            dismissUndoSource = null;
            dismissedJob = null;
            dismissedJobIndex = -1;
            StatusActionButton.Visibility = Visibility.Collapsed;
            source.Dispose();
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested)
        {
        }
    }

    private void FocusJobAtIndex(int index)
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (index >= 0 &&
                JobsItemsControl.ContainerFromIndex(index) is DependencyObject container &&
                FindFirstFocusableControl(container) is Control focusTarget &&
                focusTarget.Focus(FocusState.Programmatic))
            {
                return;
            }

            _ = PathTextBox.Focus(FocusState.Programmatic);
        });
    }

    private static Control? FindFirstFocusableControl(DependencyObject root)
    {
        if (root is Control control &&
            control.IsEnabled &&
            control.IsTabStop &&
            control.Visibility == Visibility.Visible)
        {
            return control;
        }

        int childCount = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < childCount; index++)
        {
            Control? candidate = FindFirstFocusableControl(VisualTreeHelper.GetChild(root, index));
            if (candidate is not null)
            {
                return candidate;
            }
        }

        return null;
    }

    private void OnExitButtonClick(object sender, RoutedEventArgs e)
    {
        App.MainWindow?.Close();
    }

    private async void OnOpenEditorWindowClick(object sender, RoutedEventArgs e)
    {
        if (projectInputMode == ProjectInputMode.ProjectBatch)
        {
            SetStatus(Text.CalculationFileBatchModeStatus, InfoBarSeverity.Informational);
            return;
        }

        if (!TryGetInputs(out string path, out BuildingType buildingType))
        {
            return;
        }

        if (editorWindow is not null)
        {
            string normalizedPath = Path.GetFullPath(path);
            if (string.Equals(editorWindowPath, normalizedPath, StringComparison.OrdinalIgnoreCase) &&
                editorWindowBuildingType == buildingType)
            {
                editorWindow.Activate();
                return;
            }

            ElevateProjectEditorWindow currentEditorWindow = editorWindow;
            if (!await currentEditorWindow.TryCloseAsync())
            {
                return;
            }

            if (ReferenceEquals(editorWindow, currentEditorWindow))
            {
                currentEditorWindow.DocumentSaved -= OnEditorWindowDocumentSaved;
                currentEditorWindow.Closed -= OnEditorWindowClosed;
                editorWindow = null;
                editorWindowPath = null;
                editorWindowBuildingType = null;
            }
        }

        editorWindow = new ElevateProjectEditorWindow(path, buildingType);
        editorWindowPath = Path.GetFullPath(path);
        editorWindowBuildingType = buildingType;
        editorWindow.DocumentSaved += OnEditorWindowDocumentSaved;
        editorWindow.Closed += OnEditorWindowClosed;
        editorWindow.Activate();
    }

    private void OnEditorWindowClosed(object sender, WindowEventArgs args)
    {
        if (sender is ElevateProjectEditorWindow closedWindow &&
            ReferenceEquals(editorWindow, closedWindow))
        {
            closedWindow.DocumentSaved -= OnEditorWindowDocumentSaved;
            closedWindow.Closed -= OnEditorWindowClosed;
            editorWindow = null;
            editorWindowPath = null;
            editorWindowBuildingType = null;
        }
    }

    private void OnEditorWindowDocumentSaved(object? sender, EventArgs e)
    {
        lastProjectPathAnalysis = null;
        RefreshCalculationFileStatus();
    }

    private bool TryEnsureEditorReadyForRun()
    {
        if (editorWindow is null)
        {
            return true;
        }

        if (editorWindow.IsBusy)
        {
            editorWindow.Activate();
            SetStatus(Text.EditorBusyRunMessage, InfoBarSeverity.Warning);
            return false;
        }

        if (editorWindow.HasUnsavedChanges)
        {
            editorWindow.Activate();
            SetStatus(Text.EditorUnsavedRunMessage, InfoBarSeverity.Warning);
            return false;
        }

        return true;
    }

    private void OnLanguageFlyoutOptionClick(object sender, RoutedEventArgs e)
    {
        AppLanguage language = ReferenceEquals(sender, EnglishLanguageButton)
            ? AppLanguage.English
            : AppLanguage.Russian;

        if (localizationService.CurrentLanguage != language)
        {
            localizationService.SetLanguage(language);
        }

        LanguageFlyout.Hide();
    }

    private void OnHideElevateWindowsToggleChanged(object sender, RoutedEventArgs e)
    {
        processingService.SetElevateWindowsHidden(HideElevateWindowsToggle.IsOn);
    }

    private void UpdateLanguageSelector()
    {
        LanguageOption selectedOption = LanguageOptions.First(option => option.Language == localizationService.CurrentLanguage);
        LanguageButtonText.Text = selectedOption.DisplayName;

        bool isEnglish = selectedOption.Language == AppLanguage.English;
        EnglishLanguageSelectionBackground.Opacity = isEnglish ? 1 : 0;
        EnglishLanguageSelectionPill.Opacity = isEnglish ? 1 : 0;
        RussianLanguageSelectionBackground.Opacity = isEnglish ? 0 : 1;
        RussianLanguageSelectionPill.Opacity = isEnglish ? 0 : 1;

        bool isRussian = localizationService.CurrentLanguage == AppLanguage.Russian;
        string selectedStatus = isRussian ? "Выбран" : "Selected";
        string availableStatus = isRussian ? "Доступен для выбора" : "Available";
        AutomationProperties.SetName(EnglishLanguageButton, "English");
        AutomationProperties.SetName(RussianLanguageButton, "Русский");
        AutomationProperties.SetItemStatus(
            EnglishLanguageButton,
            isEnglish ? selectedStatus : availableStatus);
        AutomationProperties.SetItemStatus(
            RussianLanguageButton,
            isEnglish ? availableStatus : selectedStatus);
        AutomationProperties.SetHelpText(
            EnglishLanguageButton,
            isEnglish ? selectedStatus : (isRussian ? "Переключить на английский" : "Switch to English"));
        AutomationProperties.SetHelpText(
            RussianLanguageButton,
            !isEnglish ? selectedStatus : (isRussian ? "Переключить на русский" : "Switch to Russian"));
    }

    private void UpdateProjectModeControlsText()
    {
        bool isRussian = localizationService.CurrentLanguage == AppLanguage.Russian;
        ProjectModeTitleTextBlock.Text = isRussian ? "Режим обработки" : "Processing mode";
        SingleProjectModeButton.Content = isRussian ? "Один проект" : "Single project";
        ProjectBatchModeButton.Content = isRussian ? "Пакет" : "Batch";
        StopQueuedJobsButton.Content = isRussian ? "Стоп очереди" : "Stop queued";
        StopAllJobsButton.Content = isRussian ? "Остановить все" : "Stop all";
        StatusActionButton.Content = isRussian ? "Отменить" : "Undo";

        AutomationProperties.SetName(SingleProjectModeButton, SingleProjectModeButton.Content.ToString()!);
        AutomationProperties.SetName(ProjectBatchModeButton, ProjectBatchModeButton.Content.ToString()!);
        AutomationProperties.SetName(StopQueuedJobsButton, StopQueuedJobsButton.Content.ToString()!);
        AutomationProperties.SetName(StopAllJobsButton, StopAllJobsButton.Content.ToString()!);
        AutomationProperties.SetHelpText(
            StopQueuedJobsButton,
            isRussian
                ? "Отменяет задачи, которые ещё не начали расчёт."
                : "Stops jobs that have not started processing yet.");
        AutomationProperties.SetHelpText(
            StopAllJobsButton,
            isRussian
                ? "Останавливает все ожидающие и выполняющиеся задачи."
                : "Stops every queued and running job.");
    }

    private void OnBuildingTypeRadioButtonChecked(object sender, RoutedEventArgs e)
    {
        BuildingType? selectedType = GetSelectedBuildingType();
        if (!selectedType.HasValue)
        {
            return;
        }

        UpdateModeButtons(selectedType.Value);
        UpdateEditorOutputPreview();
        RefreshCalculationFileStatus();

        if (suppressBuildingTypeStatus)
        {
            return;
        }

        SetStatus(localizationService.FormatSelectedBuildingType(selectedType.Value), InfoBarSeverity.Informational);
    }

    private bool TryGetInputs(out string path, out BuildingType buildingType)
    {
        path = PathTextBox.Text?.Trim() ?? string.Empty;
        buildingType = BuildingType.Office;

        if (string.IsNullOrWhiteSpace(path))
        {
            SetStatus(Text.PathRequiredMessage, InfoBarSeverity.Warning);
            return false;
        }

        if (!Directory.Exists(path))
        {
            SetStatus(Text.FolderMissingMessage, InfoBarSeverity.Error);
            return false;
        }

        BuildingType? selectedType = GetSelectedBuildingType();
        if (!selectedType.HasValue)
        {
            SetStatus(Text.BuildingTypeRequiredMessage, InfoBarSeverity.Warning);
            return false;
        }

        buildingType = selectedType.Value;
        return true;
    }

    private bool TryGetProjectBatchInputs(out string projectRoot, out int parallelRuns)
    {
        projectRoot = ProjectBatchPathTextBox.Text?.Trim() ?? string.Empty;
        parallelRuns = 4;

        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            SetStatus(Text.PathRequiredMessage, InfoBarSeverity.Warning);
            return false;
        }

        if (!Directory.Exists(projectRoot))
        {
            SetStatus(Text.FolderMissingMessage, InfoBarSeverity.Error);
            return false;
        }

        if (ProjectBatchUnlimitedRunsCheckBox.IsChecked == true)
        {
            parallelRuns = int.MaxValue;
            return true;
        }

        double rawParallelRuns = ProjectBatchParallelRunsNumberBox.Value;
        if (double.IsNaN(rawParallelRuns) || rawParallelRuns < 1)
        {
            SetStatus(Text.ProjectBatchParallelRunsMinimumMessage, InfoBarSeverity.Warning);
            return false;
        }

        parallelRuns = Math.Max(1, (int)Math.Round(rawParallelRuns));
        return true;
    }

    private bool ShouldIncludeProjectBatchOfficeLunchPeak()
    {
        return ProjectBatchMorningOnlyCheckBox.IsChecked != true;
    }

    private async Task<IReadOnlyList<ProjectBatchJob>?> ResolveUnknownProjectBatchJobsAsync(
        string projectRoot,
        IReadOnlyList<string> unknownElvxFiles)
    {
        if (unknownElvxFiles.Count == 0)
        {
            return [];
        }

        StackPanel rowsPanel = new() { Spacing = 12 };
        rowsPanel.Children.Add(new TextBlock
        {
            Text = Text.ProjectBatchUnknownHint,
            TextWrapping = TextWrapping.Wrap,
        });

        List<(string FilePath, ComboBox ComboBox)> selections = [];
        foreach (string filePath in unknownElvxFiles)
        {
            ComboBox comboBox = new()
            {
                MinWidth = 180,
            };
            comboBox.Items.Add(new ComboBoxItem { Content = Text.BuildingTypeOffice, Tag = BuildingType.Office });
            comboBox.Items.Add(new ComboBoxItem { Content = Text.BuildingTypeResidence, Tag = BuildingType.Residence });
            comboBox.Items.Add(new ComboBoxItem { Content = Text.BuildingTypeHotel, Tag = BuildingType.Hotel });
            comboBox.SelectedIndex = 0;
            UpdateUnknownProjectComboBoxAutomationName(comboBox, filePath);
            comboBox.SelectionChanged += (_, _) =>
                UpdateUnknownProjectComboBoxAutomationName(comboBox, filePath);

            StackPanel row = new() { Spacing = 6 };
            row.Children.Add(new TextBlock
            {
                Text = filePath,
                TextWrapping = TextWrapping.Wrap,
                Foreground = ResolveSubtleTextBrush(),
            });
            row.Children.Add(comboBox);
            rowsPanel.Children.Add(row);
            selections.Add((filePath, comboBox));
        }

        ContentDialog dialog = new()
        {
            XamlRoot = XamlRoot,
            Title = Text.ProjectBatchUnknownTitle,
            Content = new ScrollViewer
            {
                MaxHeight = 460,
                Content = rowsPanel,
            },
            PrimaryButtonText = Text.ProjectBatchUnknownPrimaryButton,
            SecondaryButtonText = Text.ProjectBatchUnknownSecondaryButton,
            CloseButtonText = Text.ProjectBatchUnknownCloseButton,
            DefaultButton = ContentDialogButton.Primary,
        };

        ContentDialogResult result = await ShowCoordinatedDialogAsync(dialog);
        if (result == ContentDialogResult.None)
        {
            return null;
        }

        if (result == ContentDialogResult.Secondary)
        {
            return [];
        }

        return selections
            .Select(selection =>
            {
                BuildingType buildingType = selection.ComboBox.SelectedItem is ComboBoxItem { Tag: BuildingType selectedType }
                    ? selectedType
                    : BuildingType.Office;
                return ElevateProjectBatchDiscoveryService.CreateManualJob(projectRoot, selection.FilePath, buildingType);
            })
            .ToList();
    }

    private void UpdateUnknownProjectComboBoxAutomationName(ComboBox comboBox, string filePath)
    {
        string buildingTypeText = comboBox.SelectedItem is ComboBoxItem { Content: object content }
            ? content.ToString() ?? string.Empty
            : string.Empty;
        string name = localizationService.CurrentLanguage == AppLanguage.Russian
            ? $"Тип здания для {Path.GetFileName(filePath)}: {buildingTypeText}"
            : $"Building type for {Path.GetFileName(filePath)}: {buildingTypeText}";
        AutomationProperties.SetName(comboBox, name);
        AutomationProperties.SetHelpText(comboBox, filePath);
    }

    private async Task<bool> ConfirmProjectBatchJobsAsync(
        IReadOnlyList<ProjectBatchJob> batchJobs,
        IReadOnlyList<ProjectBatchWarning> warnings,
        bool includeOfficeLunchPeak)
    {
        Grid table = new()
        {
            ColumnSpacing = 10,
            RowSpacing = 8,
        };
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        AddProjectBatchPreviewHeader(table, Text.ProjectBatchPreviewBuildingTypeHeader, column: 0);
        AddProjectBatchPreviewHeader(table, Text.ProjectBatchPreviewPathHeader, column: 1);
        AddProjectBatchPreviewHeader(table, Text.ProjectBatchPreviewFileCountHeader, column: 2);
        AddProjectBatchPreviewHeader(table, Text.ProjectBatchPreviewScenariosHeader, column: 3);

        for (int index = 0; index < batchJobs.Count; index++)
        {
            ProjectBatchJob job = batchJobs[index];
            int row = index + 1;
            string scenarioText = GetProjectBatchScenarioText(job.BuildingType, includeOfficeLunchPeak);

            AddProjectBatchPreviewCell(table, localizationService.FormatBuildingType(job.BuildingType), row, column: 0);
            AddProjectBatchPreviewCell(table, job.WorkingFolder, row, column: 1, wrap: true);
            AddProjectBatchPreviewCell(table, CountProjectBatchSourceFiles(job).ToString(localizationService.CurrentCulture), row, column: 2);
            AddProjectBatchPreviewCell(table, scenarioText, row, column: 3);
        }

        StackPanel contentStack = new()
        {
            Spacing = 14,
        };

        if (warnings.Count > 0)
        {
            contentStack.Children.Add(new TextBlock
            {
                Text = Text.ProjectBatchPreviewWarningsTitle,
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            });

            contentStack.Children.Add(new TextBlock
            {
                Text = string.Format(
                    localizationService.CurrentCulture,
                    Text.ProjectBatchPreviewWarningsFormat,
                    warnings.Count),
                FontSize = 13,
                Foreground = ResolveSubtleTextBrush(),
                TextWrapping = TextWrapping.Wrap,
            });

            foreach (ProjectBatchWarning warning in warnings)
            {
                contentStack.Children.Add(new TextBlock
                {
                    Text = $"{localizationService.FormatProjectBatchWarning(warning)}\n{warning.Path}",
                    FontSize = 12,
                    Foreground = ResolveSubtleTextBrush(),
                    IsTextSelectionEnabled = true,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 720,
                });
            }
        }

        contentStack.Children.Add(table);

        ContentDialog dialog = new()
        {
            XamlRoot = XamlRoot,
            Title = Text.ProjectBatchPreviewTitle,
            Content = new ScrollViewer
            {
                MaxHeight = 460,
                HorizontalScrollMode = ScrollMode.Disabled,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollMode = ScrollMode.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = contentStack,
            },
            PrimaryButtonText = Text.ProjectBatchPreviewStartButton,
            CloseButtonText = Text.ProjectBatchPreviewCancelButton,
            DefaultButton = ContentDialogButton.Primary,
        };

        ContentDialogResult result = await ShowCoordinatedDialogAsync(dialog);
        return result == ContentDialogResult.Primary;
    }

    private string GetProjectBatchScenarioText(BuildingType buildingType, bool includeOfficeLunchPeak)
    {
        if (buildingType != BuildingType.Office)
        {
            return Text.ProjectBatchPreviewSingleScenario;
        }

        return includeOfficeLunchPeak
            ? Text.ProjectBatchPreviewMorningLunch
            : Text.ProjectBatchPreviewMorningOnly;
    }

    private static void AddProjectBatchPreviewHeader(Grid table, string text, int column)
    {
        AddProjectBatchPreviewText(
            table,
            text,
            row: 0,
            column,
            fontWeight: Microsoft.UI.Text.FontWeights.SemiBold);
    }

    private static void AddProjectBatchPreviewCell(
        Grid table,
        string text,
        int row,
        int column,
        bool wrap = false)
    {
        AddProjectBatchPreviewText(
            table,
            text,
            row,
            column,
            fontWeight: Microsoft.UI.Text.FontWeights.Normal,
            wrap);
    }

    private static void AddProjectBatchPreviewText(
        Grid table,
        string text,
        int row,
        int column,
        Windows.UI.Text.FontWeight fontWeight,
        bool wrap = false)
    {
        while (table.RowDefinitions.Count <= row)
        {
            table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        TextBlock textBlock = new()
        {
            Text = text,
            FontSize = 13,
            FontWeight = fontWeight,
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            MaxWidth = wrap ? 520 : double.PositiveInfinity,
        };

        Grid.SetRow(textBlock, row);
        Grid.SetColumn(textBlock, column);
        table.Children.Add(textBlock);
    }

    private static Brush? ResolveSubtleTextBrush()
    {
        return Application.Current.Resources.TryGetValue("IosSubtleTextBrush", out object resource) && resource is Brush brush
            ? brush
            : null;
    }

    private static int CountProjectBatchSourceFiles(ProjectBatchJob job)
    {
        return File.Exists(job.ElvxPath) ? 1 : 0;
    }

    private void ResetEditorStatus()
    {
        EditorSourceTextBlock.Text = "-";
        EditorOutputTextBlock.Text = "-";
        SaveEditorButton.IsEnabled = false;
        ClearEditorControls();
    }

    private void ClearEditorControls()
    {
        EditorJobTitleTextBox.Text = string.Empty;
        EditorJobNoTextBox.Text = string.Empty;
        EditorCalculationTitleTextBox.Text = string.Empty;
        EditorMadeByTextBox.Text = string.Empty;
        EditorCheckedByTextBox.Text = string.Empty;
        EditorCompanyTextBox.Text = string.Empty;
        EditorDispatcherTextBox.Text = string.Empty;
        EditorTrafficModeTextBox.Text = string.Empty;
        EditorSimulationsTextBox.Text = string.Empty;
        EditorLearningRunsTextBox.Text = string.Empty;
        EditorRandomSeedTextBox.Text = string.Empty;
        EditorAbsenteeismTextBox.Text = string.Empty;
        EditorIncomingTextBox.Text = string.Empty;
        EditorOutgoingTextBox.Text = string.Empty;
        EditorInterfloorTextBox.Text = string.Empty;
        EditorHandlingCapacityTextBox.Text = string.Empty;
        EditorLoadingTimeTextBox.Text = string.Empty;
        EditorUnloadingTimeTextBox.Text = string.Empty;
        editorFloors.Clear();
        editorCars.Clear();
    }

    private void ApplyEditorDocument(ElevateProjectEditorDocument document)
    {
        EditorSourceTextBlock.Text = document.SourcePath ?? document.TemplatePath ?? "-";

        EditorJobTitleTextBox.Text = document.Job.Title;
        EditorJobNoTextBox.Text = document.Job.Number;
        EditorCalculationTitleTextBox.Text = document.Job.CalculationTitle;
        EditorMadeByTextBox.Text = document.Job.MadeBy;
        EditorCheckedByTextBox.Text = document.Job.CheckedBy;
        EditorCompanyTextBox.Text = document.Job.Company;
        EditorDispatcherTextBox.Text = document.Analysis.DispatcherAlgorithmName;
        EditorTrafficModeTextBox.Text = document.Analysis.TrafficMode;
        EditorSimulationsTextBox.Text = document.Analysis.SimulationsPerConfiguration.ToString(System.Globalization.CultureInfo.InvariantCulture);
        EditorLearningRunsTextBox.Text = document.Analysis.LearningRuns.ToString(System.Globalization.CultureInfo.InvariantCulture);
        EditorRandomSeedTextBox.Text = document.Analysis.RandomSeed.ToString(System.Globalization.CultureInfo.InvariantCulture);
        EditorAbsenteeismTextBox.Text = FormatEditorNumber(document.Building.AbsenteeismPercent);
        EditorIncomingTextBox.Text = FormatEditorNumber(document.Traffic.IncomingPercent);
        EditorOutgoingTextBox.Text = FormatEditorNumber(document.Traffic.OutgoingPercent);
        EditorInterfloorTextBox.Text = FormatEditorNumber(document.Traffic.InterfloorPercent);
        EditorHandlingCapacityTextBox.Text = FormatEditorNumber(document.Traffic.HandlingCapacity);
        EditorLoadingTimeTextBox.Text = FormatEditorNumber(document.Traffic.LoadingTimeSeconds);
        EditorUnloadingTimeTextBox.Text = FormatEditorNumber(document.Traffic.UnloadingTimeSeconds);

        ApplyEditorBuildingType(document.BuildingType);

        editorFloors.Clear();
        foreach (ElevateProjectEditorFloor floor in document.Floors)
        {
            FloorEditorRowViewModel row = new(floor, localizationService);
            editorFloors.Add(row);
        }

        editorCars.Clear();
        foreach (ElevateProjectEditorCar car in document.Cars)
        {
            CarEditorRowViewModel row = new(car, localizationService);
            editorCars.Add(row);
        }

        UpdateEditorOutputPreview();
    }

    private void ApplyEditorBuildingType(BuildingType buildingType)
    {
        suppressBuildingTypeStatus = true;
        try
        {
            switch (buildingType)
            {
                case BuildingType.Office:
                    OfficeRadioButton.IsChecked = true;
                    break;
                case BuildingType.Residence:
                    ResidenceRadioButton.IsChecked = true;
                    break;
                case BuildingType.Hotel:
                    HotelRadioButton.IsChecked = true;
                    break;
            }
        }
        finally
        {
            suppressBuildingTypeStatus = false;
        }

        UpdateModeButtons(buildingType);
    }

    private bool TryBuildEditorDocument(BuildingType buildingType, out ElevateProjectEditorDocument? document)
    {
        document = null;
        if (loadedEditorDocument is null)
        {
            SetStatus(Text.EditorNotLoadedMessage, InfoBarSeverity.Warning);
            return false;
        }

        if (!TryParseEditorDouble(EditorAbsenteeismTextBox.Text, Text.EditorAbsenteeismHeader, out double absenteeism) ||
            !TryParseEditorDouble(EditorIncomingTextBox.Text, Text.EditorIncomingHeader, out double incoming) ||
            !TryParseEditorDouble(EditorOutgoingTextBox.Text, Text.EditorOutgoingHeader, out double outgoing) ||
            !TryParseEditorDouble(EditorInterfloorTextBox.Text, Text.EditorInterfloorHeader, out double interfloor) ||
            !TryParseEditorDouble(EditorHandlingCapacityTextBox.Text, Text.EditorHandlingCapacityHeader, out double handlingCapacity) ||
            !TryParseEditorDouble(EditorLoadingTimeTextBox.Text, Text.EditorLoadingTimeHeader, out double loadingTime) ||
            !TryParseEditorDouble(EditorUnloadingTimeTextBox.Text, Text.EditorUnloadingTimeHeader, out double unloadingTime) ||
            !TryParseEditorInt(EditorSimulationsTextBox.Text, Text.EditorSimulationsHeader, out int simulationsPerConfiguration) ||
            !TryParseEditorInt(EditorLearningRunsTextBox.Text, Text.EditorLearningRunsHeader, out int learningRuns) ||
            !TryParseEditorInt(EditorRandomSeedTextBox.Text, Text.EditorRandomSeedHeader, out int randomSeed))
        {
            return false;
        }

        if (Math.Abs((incoming + outgoing + interfloor) - 100d) > 0.01d)
        {
            SetStatus(Text.EditorTrafficSplitTotalMessage, InfoBarSeverity.Warning);
            return false;
        }

        List<ElevateProjectEditorFloor> floors = [];
        foreach (FloorEditorRowViewModel row in editorFloors)
        {
            if (!TryParseEditorDouble(row.FloorLevelText, row.LevelLabel, out double floorLevel) ||
                !TryParseEditorDouble(row.PopulationText, row.PopulationLabel, out double population))
            {
                return false;
            }

            floors.Add(new ElevateProjectEditorFloor
            {
                FloorName = row.FloorName,
                FloorLevel = floorLevel,
                Population = population,
                EntranceFloor = row.EntranceFloor,
            });
        }

        List<ElevateProjectEditorCar> cars = [];
        foreach (CarEditorRowViewModel row in editorCars)
        {
            if (!TryNormalizeEditorDoubleText(row.CapacityText, row.CapacityLabel, out string capacityKg) ||
                !TryNormalizeEditorDoubleText(row.AreaText, row.AreaLabel, out string floorAreaM2) ||
                !TryNormalizeEditorDoubleText(row.SpeedText, row.SpeedLabel, out string speed) ||
                !TryNormalizeEditorDoubleText(row.AccelerationText, row.AccelerationLabel, out string acceleration) ||
                !TryNormalizeEditorDoubleText(row.JerkText, row.JerkLabel, out string jerk) ||
                !TryNormalizeEditorDoubleText(row.PreOpeningText, row.PreOpeningLabel, out string doorPreOpening) ||
                !TryNormalizeEditorDoubleText(row.OpenTimeText, row.OpenTimeLabel, out string doorOpenTime) ||
                !TryNormalizeEditorDoubleText(row.CloseTimeText, row.CloseTimeLabel, out string doorCloseTime) ||
                !TryParseEditorInt(row.HomeFloorText, row.HomeFloorLabel, out int homeFloor))
            {
                return false;
            }

            cars.Add(new ElevateProjectEditorCar
            {
                Id = row.Id,
                CapacityKg = capacityKg,
                FloorAreaM2 = floorAreaM2,
                Speed = speed,
                Acceleration = acceleration,
                Jerk = jerk,
                DoorPreOpening = doorPreOpening,
                DoorOpenTime = doorOpenTime,
                DoorCloseTime = doorCloseTime,
                HomeFloor = homeFloor.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });
        }

        document = new ElevateProjectEditorDocument
        {
            SourcePath = loadedEditorDocument.SourcePath,
            TemplatePath = loadedEditorDocument.TemplatePath,
            BuildingType = buildingType,
            Job = new ElevateProjectEditorJobSection
            {
                Title = EditorJobTitleTextBox.Text?.Trim() ?? string.Empty,
                Number = EditorJobNoTextBox.Text?.Trim() ?? string.Empty,
                CalculationTitle = EditorCalculationTitleTextBox.Text?.Trim() ?? string.Empty,
                MadeBy = EditorMadeByTextBox.Text?.Trim() ?? string.Empty,
                CheckedBy = EditorCheckedByTextBox.Text?.Trim() ?? string.Empty,
                Company = EditorCompanyTextBox.Text?.Trim() ?? string.Empty,
                LogoFile = loadedEditorDocument.Job.LogoFile,
            },
            Analysis = new ElevateProjectEditorAnalysisSection
            {
                DispatcherAlgorithmName = EditorDispatcherTextBox.Text?.Trim() ?? string.Empty,
                TrafficMode = EditorTrafficModeTextBox.Text?.Trim() ?? string.Empty,
                SimulationsPerConfiguration = simulationsPerConfiguration,
                LearningRuns = learningRuns,
                RandomSeed = randomSeed,
            },
            Building = new ElevateProjectEditorBuildingSection
            {
                BuildingType = buildingType,
                AbsenteeismPercent = absenteeism,
                NumberOfFloors = floors.Count,
            },
            Traffic = new ElevateProjectEditorTrafficSection
            {
                IncomingPercent = incoming,
                OutgoingPercent = outgoing,
                InterfloorPercent = interfloor,
                HandlingCapacity = handlingCapacity,
                LoadingTimeSeconds = loadingTime,
                UnloadingTimeSeconds = unloadingTime,
            },
            Floors = floors,
            Cars = cars,
        };

        return true;
    }

    private async Task<ElevateProjectEditorDocument> LoadExistingProjectEditorDocumentAsync(string workingFolder)
    {
        string[] files = Directory.GetFiles(workingFolder, "*.elvx", SearchOption.TopDirectoryOnly);
        string? existingElvxPath = files
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(path => path.EndsWith("01.elvx", StringComparison.OrdinalIgnoreCase))
            ?? files.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).FirstOrDefault();

        if (existingElvxPath is null)
        {
            throw new InvalidOperationException(Text.EditorExistingFileMissingMessage);
        }

        return await projectEditorService.LoadFile(existingElvxPath);
    }

    private string ResolveEditorOutputPath(string workingFolder, ElevateProjectEditorDocument document)
    {
        string suggestedFileName = projectEditorService.SuggestFileName(document);
        string suggestedOutputPath = Path.Combine(Path.GetFullPath(workingFolder), suggestedFileName);

        if (!string.IsNullOrWhiteSpace(document.SourcePath) && File.Exists(document.SourcePath))
        {
            string currentFolder = Path.GetFullPath(workingFolder);
            string? loadedFolder = Path.GetDirectoryName(document.SourcePath);
            if (!string.IsNullOrWhiteSpace(loadedFolder) &&
                string.Equals(Path.GetFullPath(loadedFolder), currentFolder, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(Path.GetFileName(document.SourcePath), suggestedFileName, StringComparison.OrdinalIgnoreCase))
            {
                return document.SourcePath;
            }
        }

        return suggestedOutputPath;
    }

    private void UpdateEditorOutputPreview()
    {
        if (loadedEditorDocument is null)
        {
            EditorOutputTextBlock.Text = "-";
            return;
        }

        ElevateProjectEditorDocument previewDocument = BuildEditorPreviewDocument();
        string workingFolder = PathTextBox.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(workingFolder))
        {
            EditorOutputTextBlock.Text = projectEditorService.SuggestFileName(previewDocument);
            return;
        }

        try
        {
            EditorOutputTextBlock.Text = ResolveEditorOutputPath(workingFolder, previewDocument);
        }
        catch
        {
            EditorOutputTextBlock.Text = projectEditorService.SuggestFileName(previewDocument);
        }
    }

    private ElevateProjectEditorDocument BuildEditorPreviewDocument()
    {
        BuildingType buildingType = GetSelectedBuildingType() ?? loadedEditorDocument?.BuildingType ?? BuildingType.Office;
        ElevateProjectEditorDocument source = loadedEditorDocument ?? new ElevateProjectEditorDocument();

        return new ElevateProjectEditorDocument
        {
            SourcePath = source.SourcePath,
            TemplatePath = source.TemplatePath,
            BuildingType = buildingType,
            Job = new ElevateProjectEditorJobSection
            {
                Title = EditorJobTitleTextBox.Text?.Trim() ?? source.Job.Title,
                Number = EditorJobNoTextBox.Text?.Trim() ?? source.Job.Number,
                CalculationTitle = source.Job.CalculationTitle,
                MadeBy = source.Job.MadeBy,
                CheckedBy = source.Job.CheckedBy,
                Company = source.Job.Company,
                LogoFile = source.Job.LogoFile,
            },
        };
    }

    private bool TryParseEditorDouble(string? rawValue, string fieldName, out double value)
    {
        value = 0;
        string text = rawValue?.Trim() ?? string.Empty;
        if (double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        if (double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.GetCultureInfo("ru-RU"), out value))
        {
            return true;
        }

        SetStatus(
            string.Format(
                localizationService.CurrentCulture,
                Text.EditorInvalidNumberFormat,
                fieldName),
            InfoBarSeverity.Warning);
        return false;
    }

    private bool TryParseEditorInt(string? rawValue, string fieldName, out int value)
    {
        value = 0;
        string text = rawValue?.Trim() ?? string.Empty;
        if (int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        if (int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.GetCultureInfo("ru-RU"), out value))
        {
            return true;
        }

        SetStatus(
            string.Format(
                localizationService.CurrentCulture,
                Text.EditorInvalidNumberFormat,
                fieldName),
            InfoBarSeverity.Warning);
        return false;
    }

    private bool TryNormalizeEditorDoubleText(string? rawValue, string fieldName, out string normalized)
    {
        normalized = string.Empty;
        if (!TryParseEditorDouble(rawValue, fieldName, out double value))
        {
            return false;
        }

        normalized = value.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture);
        return true;
    }

    private string FormatEditorNumber(double value)
    {
        return value.ToString("0.###", localizationService.CurrentCulture);
    }

    private void StartProcessingJob(string path, BuildingType buildingType, bool includeLunchPeak)
    {
        string normalizedPath = NormalizeProcessingFolder(path);
        string leaseOwnerId = Guid.NewGuid().ToString("N");
        if (!processingFolderLeases.TryAcquire(normalizedPath, leaseOwnerId, out IDisposable? folderLease))
        {
            SetStatus(
                string.Format(
                    localizationService.CurrentCulture,
                    Text.RunFolderBusyMessage,
                    normalizedPath),
                InfoBarSeverity.Warning);
            return;
        }

        JobProgressViewModel job;
        try
        {
            job = CreateJob(
                normalizedPath,
                buildingType,
                includeLunchPeak,
                leaseOwnerId: leaseOwnerId);
        }
        catch
        {
            folderLease!.Dispose();
            throw;
        }

        TrackJobTask(RunProcessingJobAsync(
            job,
            normalizedPath,
            buildingType,
            includeLunchPeak,
            folderLease!));
    }

    private void StartProjectBatchJobs(
        IReadOnlyList<ProjectBatchJob> batchJobs,
        int parallelRuns,
        int warningCount,
        bool includeOfficeLunchPeak)
    {
        int effectiveParallelRuns = parallelRuns == int.MaxValue
            ? Math.Max(1, batchJobs.Count)
            : Math.Max(1, Math.Min(parallelRuns, batchJobs.Count));
        SemaphoreSlim parallelism = new(effectiveParallelRuns, effectiveParallelRuns);

        int startedJobs = 0;
        bool startedOfficeJob = false;
        foreach (ProjectBatchJob batchJob in batchJobs)
        {
            string normalizedPath = NormalizeProcessingFolder(batchJob.WorkingFolder);
            string leaseOwnerId = Guid.NewGuid().ToString("N");
            if (!processingFolderLeases.TryAcquire(normalizedPath, leaseOwnerId, out IDisposable? folderLease))
            {
                SetStatus(
                    string.Format(
                        localizationService.CurrentCulture,
                        Text.RunFolderBusyMessage,
                        normalizedPath),
                    InfoBarSeverity.Warning);
                continue;
            }

            bool includeLunchPeak = batchJob.BuildingType == BuildingType.Office && includeOfficeLunchPeak;
            string title = $"{batchJob.BuildingTypeFolderName} / {batchJob.GroupName}";
            JobProgressViewModel job = CreateJob(
                normalizedPath,
                batchJob.BuildingType,
                includeLunchPeak,
                title,
                batchJob.ProjectRoot,
                leaseOwnerId);

            startedJobs++;
            startedOfficeJob |= batchJob.BuildingType == BuildingType.Office;
            TrackJobTask(RunProjectBatchJobAsync(
                job,
                batchJob,
                includeLunchPeak,
                parallelism,
                folderLease!));
        }

        if (startedJobs == 0)
        {
            SetStatus(Text.ProjectBatchNoJobsMessage, InfoBarSeverity.Warning);
            return;
        }

        string officeScenario = GetProjectBatchScenarioText(BuildingType.Office, includeOfficeLunchPeak);
        string startedMessage = (warningCount > 0, startedOfficeJob) switch
        {
            (true, true) => string.Format(
                localizationService.CurrentCulture,
                Text.ProjectBatchStartedWithWarningsAndOfficeScenarioFormat,
                startedJobs,
                warningCount,
                officeScenario),
            (true, false) => string.Format(
                localizationService.CurrentCulture,
                Text.ProjectBatchStartedWithWarningsFormat,
                startedJobs,
                warningCount),
            (false, true) => string.Format(
                localizationService.CurrentCulture,
                Text.ProjectBatchStartedWithOfficeScenarioFormat,
                startedJobs,
                officeScenario),
            _ => string.Format(
                localizationService.CurrentCulture,
                Text.ProjectBatchStartedFormat,
                startedJobs),
        };

        SetStatus(startedMessage, warningCount > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Informational);
    }

    private async Task RunProjectBatchJobAsync(
        JobProgressViewModel job,
        ProjectBatchJob batchJob,
        bool includeLunchPeak,
        SemaphoreSlim parallelism,
        IDisposable folderLease)
    {
        bool acquired = false;
        bool executionStarted = false;
        using CancellationTokenSource stopSource = CancellationTokenSource.CreateLinkedTokenSource(
            applicationLifetimeSource.Token);
        job.AttachStopSource(stopSource);
        job.MarkQueued(localizationService);
        RefreshJobsSummary();
        try
        {
            await parallelism.WaitAsync(stopSource.Token);
            acquired = true;
            executionStarted = true;
            await RunProcessingJobAsync(
                job,
                batchJob.WorkingFolder,
                batchJob.BuildingType,
                includeLunchPeak,
                folderLease,
                autoGenerateReport: true,
                reportOutputRoot: batchJob.ProjectRoot,
                sharedStopSource: stopSource);
        }
        catch (OperationCanceledException) when (stopSource.IsCancellationRequested)
        {
            job.MarkStopped(localizationService);
        }
        catch (Exception ex)
        {
            string message = BuildExceptionMessage(ex);
            job.MarkFailed(ex, localizationService);
            SetStatus(message, InfoBarSeverity.Error);
        }
        finally
        {
            job.DetachStopSource(stopSource);
            if (!executionStarted)
            {
                folderLease.Dispose();
            }

            if (acquired)
            {
                _ = parallelism.Release();
            }
        }
    }

    private async Task RunProcessingJobAsync(
        JobProgressViewModel job,
        string path,
        BuildingType buildingType,
        bool includeLunchPeak,
        IDisposable folderLease,
        bool autoGenerateReport = false,
        string? reportOutputRoot = null,
        bool rerunExistingBatch = false,
        CancellationTokenSource? sharedStopSource = null)
    {
        bool ownsStopSource = sharedStopSource is null;
        CancellationTokenSource stopSource = sharedStopSource ??
            CancellationTokenSource.CreateLinkedTokenSource(applicationLifetimeSource.Token);
        if (ownsStopSource)
        {
            job.AttachStopSource(stopSource);
        }

        bool primaryRunCompleted = false;
        job.MarkPreparing(localizationService);
        RefreshJobsSummary();
        SetStatus(localizationService.FormatRunStarted(job.Title), InfoBarSeverity.Informational);

        try
        {
            job.MarkRunning(localizationService);
            ProcessingResult result = rerunExistingBatch
                ? await InvokeExistingBatchAsync(job, path, buildingType, includeLunchPeak, stopSource.Token)
                : await InvokeProcessingAsync(job, path, buildingType, includeLunchPeak, stopSource.Token);
            primaryRunCompleted = true;
            job.MarkPrimaryRunFinished();
            if (job.StopRequested)
            {
                if (applicationLifetimeSource.IsCancellationRequested)
                {
                    job.MarkStopped(localizationService);
                }
                else
                {
                    await CompleteStoppedJobAsync(job, autoGenerateReport, reportOutputRoot);
                }

                return;
            }

            if (!result.Success)
            {
                if (job.SupportsScenarioRetry && job.AllScenariosCompleted)
                {
                    await FinalizeRecoveredJobAsync(job);
                }
                else if (job.SupportsScenarioRetry && job.HasRunningScenarios)
                {
                    job.MarkScenarioRecoveryPending(localizationService);
                }
                else
                {
                    ApplyProcessingResult(job, result);
                }
            }
            else if (autoGenerateReport)
            {
                await GenerateReportForCompletedJobAsync(
                    job,
                    reportOutputRoot,
                    stopSource.Token);
            }
            else
            {
                ApplyProcessingResult(job, result);
            }
        }
        catch (OperationCanceledException) when (stopSource.IsCancellationRequested)
        {
            if (!primaryRunCompleted &&
                job.StopRequested &&
                !applicationLifetimeSource.IsCancellationRequested)
            {
                job.MarkPrimaryRunFinished();
                await CompleteStoppedJobAsync(job, autoGenerateReport, reportOutputRoot);
            }
            else
            {
                job.MarkStopped(localizationService);
            }
        }
        catch (Exception ex)
        {
            job.MarkPrimaryRunFinished();
            string message = BuildExceptionMessage(ex);
            job.MarkFailed(ex, localizationService);
            SetStatus(message, InfoBarSeverity.Error);
        }
        finally
        {
            if (ownsStopSource)
            {
                job.DetachStopSource(stopSource);
                stopSource.Dispose();
            }

            folderLease.Dispose();
            RefreshJobsSummary();
        }
    }

    private static string NormalizeProcessingFolder(string path)
    {
        string trimmedPath = path.Trim();
        try
        {
            string fullPath = Path.GetFullPath(trimmedPath);
            string root = Path.GetPathRoot(fullPath) ?? string.Empty;
            return fullPath.Length <= root.Length
                ? fullPath
                : fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return trimmedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    private void TrackJobTask(Task task)
    {
        lock (activeJobTasksSync)
        {
            activeJobTasks.Add(task);
        }

        _ = task.ContinueWith(
            completedTask =>
            {
                _ = completedTask.Exception;
                lock (activeJobTasksSync)
                {
                    activeJobTasks.Remove(completedTask);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task ExecuteReportActionAsync(string busyText, Func<Task> action)
    {
        if (!await reportExecutionLock.WaitAsync(0))
        {
            SetStatus(Text.ReportBusyMessage, InfoBarSeverity.Warning);
            return;
        }

        SetReportButtonsEnabled(isEnabled: false);
        try
        {
            SetStatus(busyText, InfoBarSeverity.Informational);
            await action();
        }
        catch (Exception ex)
        {
            SetStatus(BuildExceptionMessage(ex), InfoBarSeverity.Error);
        }
        finally
        {
            SetReportButtonsEnabled(isEnabled: true);
            _ = reportExecutionLock.Release();
        }
    }

    private async Task GenerateReportForCompletedJobAsync(
        JobProgressViewModel job,
        string? outputFolder,
        CancellationToken cancellationToken)
    {
        await GenerateReportForCompletedJobAsync(
            job,
            outputFolder,
            preserveStoppedStatus: false,
            cancellationToken);
    }

    private async Task FinalizeRecoveredJobAsync(JobProgressViewModel job)
    {
        if (!job.TryBeginCompletion())
        {
            return;
        }

        if (job.AutoGenerateReport)
        {
            await GenerateReportForCompletedJobAsync(
                job,
                job.ReportOutputRoot,
                applicationLifetimeSource.Token);
            return;
        }

        job.MarkCompleted(localizationService);
        SetStatus(localizationService.FormatRunCompleted(job.Title), InfoBarSeverity.Success);
    }

    private async Task GenerateReportForCompletedJobAsync(
        JobProgressViewModel job,
        string? outputFolder,
        bool preserveStoppedStatus,
        CancellationToken cancellationToken)
    {
        if (!preserveStoppedStatus)
        {
            job.MarkReporting(localizationService);
            RefreshJobsSummary();
        }

        SetStatus(Text.ProjectBatchGeneratingReports, InfoBarSeverity.Informational);
        ProcessingResult reportResult;
        try
        {
            reportResult = await PrintReportsForJobWithLockAsync(
                job,
                outputFolder,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            processingService.RecordReportOutcome(
                job.JobPath,
                success: false,
                "Report generation was canceled.");
            throw;
        }

        processingService.RecordReportOutcome(
            job.JobPath,
            reportResult.Success,
            reportResult.Success ? null : FormatResultMessage(reportResult));
        if (reportResult.Success)
        {
            if (!preserveStoppedStatus)
            {
                job.MarkCompleted(localizationService);
            }

            SetStatus(localizationService.FormatRunCompleted(job.Title), InfoBarSeverity.Success);
            return;
        }

        string message = FormatResultMessage(reportResult);
        if (!preserveStoppedStatus)
        {
            job.MarkReportFailed(reportResult, localizationService);
        }

        SetStatus(message, InfoBarSeverity.Error);
    }

    private async Task CompleteStoppedJobAsync(
        JobProgressViewModel job,
        bool autoGenerateReport,
        string? reportOutputRoot)
    {
        job.MarkStopped(localizationService);
        SetStatus(localizationService.FormatRunStopped(job.Title), InfoBarSeverity.Informational);

        if (autoGenerateReport)
        {
            await GenerateReportForCompletedJobAsync(
                job,
                reportOutputRoot,
                preserveStoppedStatus: true,
                applicationLifetimeSource.Token);
        }
    }

    private async Task<ProcessingResult> PrintReportsForJobWithLockAsync(
        JobProgressViewModel job,
        string? outputFolder,
        CancellationToken cancellationToken)
    {
        await reportExecutionLock.WaitAsync(cancellationToken);
        SetReportButtonsEnabled(isEnabled: false);
        try
        {
            return await PrintReportsForJobAsync(job, outputFolder, cancellationToken);
        }
        finally
        {
            SetReportButtonsEnabled(isEnabled: true);
            _ = reportExecutionLock.Release();
        }
    }

    private void HandleReportResult(ProcessingResult result, string successMessage)
    {
        if (result.Success)
        {
            SetStatus(successMessage, InfoBarSeverity.Success);
            return;
        }

        SetStatus(FormatResultMessage(result), InfoBarSeverity.Error);
    }

    private void ApplyProcessingResult(JobProgressViewModel job, ProcessingResult result)
    {
        if (result.Success)
        {
            job.MarkCompleted(localizationService);
            SetStatus(localizationService.FormatRunCompleted(job.Title), InfoBarSeverity.Success);
            return;
        }

        string message = FormatResultMessage(result);
        job.MarkFailed(result, localizationService);
        SetStatus(message, InfoBarSeverity.Error);
    }

    private async Task<ProcessingResult> InvokeProcessingAsync(
        JobProgressViewModel job,
        string path,
        BuildingType buildingType,
        bool includeLunchPeak,
        CancellationToken cancellationToken)
    {
        Progress<ElevateProgressInfo> morningProgress = new(update => HandleProgressUpdate(job, update));
        Progress<ElevateProgressInfo>? lunchProgress = buildingType == BuildingType.Office && includeLunchPeak
            ? new Progress<ElevateProgressInfo>(update => HandleProgressUpdate(job, update))
            : null;

        return await processingService.RunAsync(
            path,
            buildingType,
            includeLunchPeak,
            morningProgress,
            lunchProgress,
            cancellationToken);
    }

    private async Task<ProcessingResult> InvokeExistingBatchAsync(
        JobProgressViewModel job,
        string path,
        BuildingType buildingType,
        bool includeLunchPeak,
        CancellationToken cancellationToken)
    {
        Progress<ElevateProgressInfo> morningProgress = new(update => HandleProgressUpdate(job, update));
        Progress<ElevateProgressInfo>? lunchProgress = buildingType == BuildingType.Office && includeLunchPeak
            ? new Progress<ElevateProgressInfo>(update => HandleProgressUpdate(job, update))
            : null;

        return await processingService.RunExistingBatchAsync(
            path,
            buildingType,
            includeLunchPeak,
            morningProgress,
            lunchProgress,
            cancellationToken);
    }

    private void HandleProgressUpdate(JobProgressViewModel job, ElevateProgressInfo update)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            _ = DispatcherQueue.TryEnqueue(() => HandleProgressUpdate(job, update));
            return;
        }

        if (!string.IsNullOrWhiteSpace(update.ErrorMessage))
        {
            string message = localizationService.TranslateRuntimeMessage(update.ErrorMessage);
            job.MarkScenarioFailed(update.Scenario, update.ErrorMessage, localizationService);
            RefreshJobsSummary();
            return;
        }

        job.UpdateProgress(
            update.Scenario,
            update.Completed,
            update.Total,
            update.IsFinal,
            localizationService);
        UpdateJobScenarioMetrics(job, update.Scenario);
    }

    private void UpdateJobScenarioMetrics(JobProgressViewModel job, string? scenario)
    {
        string metricsPath = ResolveMetricsPath(job, scenario);
        if (!metricsReadStates.TryGetValue(metricsPath, out MetricsReadState? state))
        {
            state = new MetricsReadState();
            metricsReadStates.Add(metricsPath, state);
        }

        state.Source?.Cancel();
        CancellationTokenSource source = CancellationTokenSource.CreateLinkedTokenSource(
            applicationLifetimeSource.Token);
        int generation = ++state.Generation;
        state.Source = source;

        DateTimeOffset now = DateTimeOffset.UtcNow;
        TimeSpan delay = state.LastReadStartedUtc == default
            ? TimeSpan.Zero
            : state.LastReadStartedUtc + MetricsReadThrottle - now;
        if (delay < TimeSpan.Zero)
        {
            delay = TimeSpan.Zero;
        }

        _ = ReadJobScenarioMetricsAsync(
            job,
            scenario,
            metricsPath,
            state,
            generation,
            delay,
            source);
    }

    private async Task ReadJobScenarioMetricsAsync(
        JobProgressViewModel job,
        string? scenario,
        string metricsPath,
        MetricsReadState state,
        int generation,
        TimeSpan delay,
        CancellationTokenSource source)
    {
        try
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, source.Token);
            }

            source.Token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(state.Source, source) || state.Generation != generation)
            {
                return;
            }

            state.LastReadStartedUtc = DateTimeOffset.UtcNow;
            ElevateResultMetrics? metrics = await Task.Run(
                () => resultMetricsService.ReadLatestMetrics(metricsPath),
                source.Token);
            source.Token.ThrowIfCancellationRequested();
            if (metrics is null ||
                !ReferenceEquals(state.Source, source) ||
                state.Generation != generation ||
                !Jobs.Contains(job))
            {
                return;
            }

            job.UpdateScenarioMetrics(
                scenario,
                metrics,
                localizationService.CurrentCulture);
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Unable to refresh Elevate result metrics for '{metricsPath}': {ex}");
        }
        finally
        {
            if (ReferenceEquals(state.Source, source))
            {
                state.Source = null;
            }

            source.Dispose();
        }
    }

    private static string ResolveMetricsPath(JobProgressViewModel job, string? scenario)
    {
        if (job.BuildingType != BuildingType.Office)
        {
            return job.JobPath;
        }

        if (!string.IsNullOrWhiteSpace(scenario) &&
            scenario.Contains("lunch", StringComparison.OrdinalIgnoreCase))
        {
            return Path.Combine(job.JobPath, "lunch");
        }

        return Path.Combine(job.JobPath, "morning");
    }

    private void SetStatus(
        string message,
        InfoBarSeverity severity,
        bool preserveActionButton = false)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            _ = DispatcherQueue.TryEnqueue(() => SetStatus(message, severity, preserveActionButton));
            return;
        }

        if (!preserveActionButton)
        {
            dismissUndoSource?.Cancel();
            dismissUndoSource?.Dispose();
            dismissUndoSource = null;
            dismissedJob = null;
            dismissedJobIndex = -1;
            StatusActionButton.Visibility = Visibility.Collapsed;
        }

        int announcementGeneration = Interlocked.Increment(ref statusAnnouncementGeneration);
        bool wasOpen = StatusInfoBar.IsOpen;
        if (wasOpen)
        {
            StatusInfoBar.IsOpen = false;
        }

        AutomationProperties.SetLiveSetting(
            StatusInfoBar,
            severity is InfoBarSeverity.Error or InfoBarSeverity.Warning
                ? AutomationLiveSetting.Assertive
                : AutomationLiveSetting.Polite);
        StatusInfoBar.Severity = severity;
        StatusInfoBar.Message = message;
        statusMessageLanguage = localizationService.CurrentLanguage;
        AutomationProperties.SetName(StatusInfoBar, $"{Text.StatusTitle}: {message}");
        if (!wasOpen)
        {
            StatusInfoBar.IsOpen = true;
            RaiseLiveRegionChanged(StatusInfoBar);
            return;
        }

        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (announcementGeneration == statusAnnouncementGeneration)
            {
                StatusInfoBar.IsOpen = true;
                RaiseLiveRegionChanged(StatusInfoBar);
            }
        });
    }

    private static void UpdateLiveRegionText(TextBlock textBlock, string text)
    {
        if (string.Equals(textBlock.Text, text, StringComparison.Ordinal))
        {
            return;
        }

        textBlock.Text = text;
        RaiseLiveRegionChanged(textBlock);
    }

    private static void RaiseLiveRegionChanged(FrameworkElement element)
    {
        AutomationPeer? peer =
            FrameworkElementAutomationPeer.FromElement(element) ??
            FrameworkElementAutomationPeer.CreatePeerForElement(element);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    private void RefreshJobsSummary()
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            _ = DispatcherQueue.TryEnqueue(RefreshJobsSummary);
            return;
        }

        SortJobs();

        int runningJobs = Jobs.Count(job => job.IsRunning);
        BusyRing.IsActive = runningJobs > 0;
        UpdateLiveRegionText(BusyTextBlock, localizationService.GetQueueSummary(runningJobs));

        int queuedStoppableJobs = Jobs.Count(job => job.CanStop && job.IsQueued);
        int allStoppableJobs = Jobs.Count(job => job.CanStop);
        BatchQueueControlsPanel.Visibility = allStoppableJobs > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        StopQueuedJobsButton.IsEnabled = queuedStoppableJobs > 0;
        StopAllJobsButton.IsEnabled = allStoppableJobs > 0;

        bool hasJobs = Jobs.Count > 0;
        JobsItemsControl.Visibility = hasJobs ? Visibility.Visible : Visibility.Collapsed;
        EmptyQueueBorder.Visibility = hasJobs ? Visibility.Collapsed : Visibility.Visible;
        if (!restoringInterruptedJobs)
        {
            jobQueuePersistenceService.SaveActiveJobs(
                Jobs
                    .Where(job => !job.IsFinished)
                    .Select(job => job.CreatePersistenceSnapshot()));
        }
    }

    private void RestoreInterruptedJobs()
    {
        IReadOnlyList<PersistedJobSnapshot> interruptedJobs =
            jobQueuePersistenceService.LoadInterruptedJobs();
        restoringInterruptedJobs = true;
        try
        {
            foreach (PersistedJobSnapshot snapshot in interruptedJobs)
            {
                if (string.IsNullOrWhiteSpace(snapshot.Path))
                {
                    continue;
                }

                JobProgressViewModel job = CreateJob(
                    snapshot.Path,
                    snapshot.BuildingType,
                    snapshot.IncludeLunchPeak,
                    snapshot.Title,
                    snapshot.ReportOutputRoot);
                bool pathExists = Directory.Exists(snapshot.Path);
                string interruptionMessage = pathExists
                    ? "The previous application session ended before this task completed. The calculation can be retried."
                    : "The previous session ended before this task completed, and its working folder is currently unavailable.";
                string russianInterruptionMessage = pathExists
                    ? "Предыдущий сеанс приложения завершился до окончания задачи. Расчет можно повторить."
                    : "Предыдущий сеанс завершился до окончания задачи, а рабочая папка сейчас недоступна.";
                job.MarkFailed(
                    interruptionMessage,
                    localizationService,
                    russianInterruptionMessage);
            }

            jobQueuePersistenceService.ClearInterruptedJobs();
        }
        finally
        {
            restoringInterruptedJobs = false;
        }
    }

    private void SortJobs()
    {
        if (Jobs.Count < 2)
        {
            return;
        }

        List<JobProgressViewModel> orderedJobs = Jobs
            .Select((job, index) => new { Job = job, Index = index })
            .OrderBy(item => item.Job.QueueSortGroup)
            .ThenBy(item => item.Index)
            .Select(item => item.Job)
            .ToList();

        for (int targetIndex = 0; targetIndex < orderedJobs.Count; targetIndex++)
        {
            JobProgressViewModel job = orderedJobs[targetIndex];
            int currentIndex = Jobs.IndexOf(job);
            if (currentIndex >= 0 && currentIndex != targetIndex)
            {
                Jobs.Move(currentIndex, targetIndex);
            }
        }
    }

    private void SetReportButtonsEnabled(bool isEnabled)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            _ = DispatcherQueue.TryEnqueue(() => SetReportButtonsEnabled(isEnabled));
            return;
        }

        ReportButton.IsEnabled = isEnabled;
        MorningReportButton.IsEnabled = isEnabled;
        LunchReportButton.IsEnabled = isEnabled;

        foreach (JobProgressViewModel job in Jobs)
        {
            job.SetReportActionEnabled(isEnabled);
        }
    }

    private string GetJobReportBusyText(JobProgressViewModel job)
    {
        if (job.PrintsMultipleReports)
        {
            return Text.GeneratingReports;
        }

        return job.BuildingType == BuildingType.Office
            ? Text.GeneratingMorningReport
            : Text.GeneratingReport;
    }

    private string GetJobReportSuccessText(JobProgressViewModel job)
    {
        if (job.PrintsMultipleReports)
        {
            return Text.ReportsGenerated;
        }

        return job.BuildingType == BuildingType.Office
            ? Text.MorningReportGenerated
            : Text.ReportGenerated;
    }

    private async Task<ProcessingResult> PrintReportsForJobAsync(
        JobProgressViewModel job,
        string? outputFolder,
        CancellationToken cancellationToken)
    {
        if (job.BuildingType != BuildingType.Office)
        {
            return await reportService.PrintReportAsync(
                job.JobPath,
                job.BuildingType,
                outputFolder,
                cancellationToken);
        }

        if (job.WasStoppedEarly)
        {
            return await PrintAvailableOfficeReportsForStoppedJobAsync(
                job,
                outputFolder,
                cancellationToken);
        }

        string morningPath = Path.Combine(job.JobPath, "morning");
        ProcessingResult morningResult = await reportService.PrintReportAsync(
            morningPath,
            job.BuildingType,
            outputFolder,
            cancellationToken);
        if (!morningResult.Success || !job.IncludeLunchPeak)
        {
            return morningResult;
        }

        string lunchPath = Path.Combine(job.JobPath, "lunch");
        return await reportService.PrintReportAsync(
            lunchPath,
            job.BuildingType,
            outputFolder,
            cancellationToken);
    }

    private async Task<ProcessingResult> PrintAvailableOfficeReportsForStoppedJobAsync(
        JobProgressViewModel job,
        string? outputFolder,
        CancellationToken cancellationToken)
    {
        List<string> scenarioPaths = new();
        string morningPath = Path.Combine(job.JobPath, "morning");
        if (HasReportInput(morningPath))
        {
            scenarioPaths.Add(morningPath);
        }

        string lunchPath = Path.Combine(job.JobPath, "lunch");
        if (job.IncludeLunchPeak && HasReportInput(lunchPath))
        {
            scenarioPaths.Add(lunchPath);
        }

        if (scenarioPaths.Count == 0)
        {
            return ProcessingResult.Fail(Text.StoppedRunNoResultsMessage);
        }

        ProcessingResult result = ProcessingResult.Ok();
        foreach (string scenarioPath in scenarioPaths)
        {
            result = await reportService.PrintReportAsync(
                scenarioPath,
                job.BuildingType,
                outputFolder,
                cancellationToken);
            if (!result.Success)
            {
                return result;
            }
        }

        return result;
    }

    private static bool HasReportInput(string path)
    {
        return File.Exists(Path.Combine(path, "batch_results.csv"));
    }

    private void UpdateModeButtons(BuildingType buildingType)
    {
        bool isOffice = buildingType == BuildingType.Office;

        RunMorningOnlyButton.Visibility = isOffice ? Visibility.Visible : Visibility.Collapsed;
        MorningReportButton.Visibility = isOffice ? Visibility.Visible : Visibility.Collapsed;
        LunchReportButton.Visibility = isOffice ? Visibility.Visible : Visibility.Collapsed;
        ReportButton.Visibility = isOffice ? Visibility.Collapsed : Visibility.Visible;
    }

    private void RefreshProjectInputMode()
    {
        ScheduleProjectPathAnalysis(immediate: true);
    }

    private void ScheduleProjectPathAnalysis(bool immediate = false)
    {
        projectPathAnalysisSource?.Cancel();
        projectPathAnalysisSource = null;

        int generation = Interlocked.Increment(ref projectPathAnalysisGeneration);
        string path = PathTextBox.Text?.Trim() ?? string.Empty;
        lastProjectPathAnalysis = null;

        if (string.IsNullOrWhiteSpace(path))
        {
            ApplyProjectPathAnalysisState();
            return;
        }

        if (!Directory.Exists(path))
        {
            ApplyProjectPathAnalysisState();
            return;
        }

        CancellationTokenSource source = CancellationTokenSource.CreateLinkedTokenSource(
            applicationLifetimeSource.Token);
        projectPathAnalysisSource = source;
        UpdateLiveRegionText(
            ProjectModeSuggestionTextBlock,
            localizationService.CurrentLanguage == AppLanguage.Russian
                ? "Анализируем структуру папки…"
                : "Analyzing the folder structure…");
        CalculationFileStatusTextBlock.Text = ProjectModeSuggestionTextBlock.Text;
        _ = AnalyzeProjectPathAsync(path, generation, immediate, source);
    }

    private async Task AnalyzeProjectPathAsync(
        string path,
        int generation,
        bool immediate,
        CancellationTokenSource source)
    {
        try
        {
            if (!immediate)
            {
                await Task.Delay(ProjectPathAnalysisDebounce, source.Token);
            }

            ProjectPathAnalysis analysis = await Task.Run(
                () => AnalyzeProjectPath(path, source.Token),
                source.Token);
            source.Token.ThrowIfCancellationRequested();
            if (generation != projectPathAnalysisGeneration)
            {
                return;
            }

            lastProjectPathAnalysis = analysis;
            ApplyProjectPathAnalysisState();
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (generation != projectPathAnalysisGeneration)
            {
                return;
            }

            lastProjectPathAnalysis = new ProjectPathAnalysis(
                path,
                SuggestedBatchMode: false,
                CalculationFileCount: 0,
                PreferredCalculationFileName: null,
                ErrorMessage: ex.Message);
            ApplyProjectPathAnalysisState();
        }
        finally
        {
            if (ReferenceEquals(projectPathAnalysisSource, source))
            {
                projectPathAnalysisSource = null;
            }

            source.Dispose();
        }
    }

    private ProjectPathAnalysis AnalyzeProjectPath(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string normalizedPath = NormalizeProcessingFolder(path);
        ProjectBatchDiscoveryResult discoveryResult = projectBatchDiscoveryService.Discover(normalizedPath);
        cancellationToken.ThrowIfCancellationRequested();

        bool hasNestedProjectFiles = discoveryResult.Jobs.Any(job =>
                !NormalizeProcessingFolder(job.WorkingFolder)
                    .Equals(normalizedPath, StringComparison.OrdinalIgnoreCase)) ||
            discoveryResult.UnknownElvxFiles.Any(file =>
                !NormalizeProcessingFolder(Path.GetDirectoryName(file) ?? normalizedPath)
                    .Equals(normalizedPath, StringComparison.OrdinalIgnoreCase)) ||
            discoveryResult.Warnings.Any(warning =>
                !NormalizeProcessingFolder(
                        Directory.Exists(warning.Path)
                            ? warning.Path
                            : Path.GetDirectoryName(warning.Path) ?? normalizedPath)
                    .Equals(normalizedPath, StringComparison.OrdinalIgnoreCase));

        string[] calculationFiles = GetCalculationElvxCandidates(normalizedPath);
        string? preferredFile = SelectPreferredCalculationElvxPath(calculationFiles);
        return new ProjectPathAnalysis(
            normalizedPath,
            hasNestedProjectFiles,
            calculationFiles.Length,
            preferredFile is null ? null : Path.GetFileName(preferredFile),
            ErrorMessage: null);
    }

    private void SyncProjectBatchPathFromMainPath()
    {
        if (projectInputMode != ProjectInputMode.ProjectBatch)
        {
            return;
        }

        string path = PathTextBox.Text?.Trim() ?? string.Empty;
        if (!string.Equals(ProjectBatchPathTextBox.Text, path, StringComparison.Ordinal))
        {
            ProjectBatchPathTextBox.Text = path;
        }
    }

    private void UpdateProjectInputModeVisibility()
    {
        bool isBatchMode = projectInputMode == ProjectInputMode.ProjectBatch;
        UpdateProjectInputModeSelection();
        BuildingTypeCard.Visibility = isBatchMode ? Visibility.Collapsed : Visibility.Visible;
        EditorWindowCard.Visibility = isBatchMode ? Visibility.Collapsed : Visibility.Visible;
        ActionsCard.Visibility = isBatchMode ? Visibility.Collapsed : Visibility.Visible;
        ProjectBatchCard.Visibility = isBatchMode
            ? Visibility.Visible
            : Visibility.Collapsed;
        RefreshCalculationFileStatus();
    }

    private void RefreshCalculationFileStatus()
    {
        string path = PathTextBox.Text?.Trim() ?? string.Empty;
        if (lastProjectPathAnalysis is not null &&
            string.Equals(
                NormalizeProcessingFolder(path),
                NormalizeProcessingFolder(lastProjectPathAnalysis.Path),
                StringComparison.OrdinalIgnoreCase))
        {
            ApplyProjectPathAnalysisState();
            return;
        }

        ScheduleProjectPathAnalysis(immediate: true);
    }

    private void ApplyProjectPathAnalysisState()
    {
        string path = PathTextBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            CalculationFileStatusTextBlock.Text = Text.CalculationFileNoPathStatus;
            UpdateLiveRegionText(
                ProjectModeSuggestionTextBlock,
                localizationService.CurrentLanguage == AppLanguage.Russian
                    ? "Выберите режим и рабочую папку."
                    : "Choose a mode and working folder.");
            return;
        }

        if (!Directory.Exists(path))
        {
            CalculationFileStatusTextBlock.Text = Text.CalculationFileMissingPathStatus;
            UpdateLiveRegionText(
                ProjectModeSuggestionTextBlock,
                localizationService.CurrentLanguage == AppLanguage.Russian
                    ? "Папка пока недоступна; выбранный режим сохранён."
                    : "The folder is not available yet; the selected mode is preserved.");
            return;
        }

        ProjectPathAnalysis? analysis = lastProjectPathAnalysis;
        if (analysis is null)
        {
            UpdateLiveRegionText(
                ProjectModeSuggestionTextBlock,
                localizationService.CurrentLanguage == AppLanguage.Russian
                    ? "Анализируем структуру папки…"
                    : "Analyzing the folder structure…");
            CalculationFileStatusTextBlock.Text = ProjectModeSuggestionTextBlock.Text;
            return;
        }

        if (!string.IsNullOrWhiteSpace(analysis.ErrorMessage))
        {
            CalculationFileStatusTextBlock.Text = localizationService.TranslateRuntimeMessage(analysis.ErrorMessage);
            UpdateLiveRegionText(
                ProjectModeSuggestionTextBlock,
                localizationService.CurrentLanguage == AppLanguage.Russian
                    ? "Не удалось определить структуру автоматически; выберите режим вручную."
                    : "The structure could not be detected; choose the mode manually.");
            return;
        }

        if (projectInputMode == ProjectInputMode.ProjectBatch)
        {
            CalculationFileStatusTextBlock.Text = Text.CalculationFileBatchModeStatus;
        }
        else if (analysis.CalculationFileCount == 0 ||
                 string.IsNullOrWhiteSpace(analysis.PreferredCalculationFileName))
        {
            CalculationFileStatusTextBlock.Text = Text.CalculationFileTemplateStatus;
        }
        else
        {
            CalculationFileStatusTextBlock.Text = analysis.CalculationFileCount == 1
                ? string.Format(
                    localizationService.CurrentCulture,
                    Text.CalculationFileExistingStatusFormat,
                    analysis.PreferredCalculationFileName)
                : string.Format(
                    localizationService.CurrentCulture,
                    Text.CalculationFileMultipleStatusFormat,
                    analysis.CalculationFileCount,
                    analysis.PreferredCalculationFileName);
        }

        bool selectedModeMatchesSuggestion =
            analysis.SuggestedBatchMode == (projectInputMode == ProjectInputMode.ProjectBatch);
        UpdateLiveRegionText(
            ProjectModeSuggestionTextBlock,
            selectedModeMatchesSuggestion
                ? localizationService.CurrentLanguage == AppLanguage.Russian
                    ? "Выбранный режим соответствует структуре папки."
                    : "The selected mode matches the folder structure."
                : analysis.SuggestedBatchMode
                    ? localizationService.CurrentLanguage == AppLanguage.Russian
                        ? "Обнаружены вложенные проекты — рекомендуется режим «Пакет»."
                        : "Nested projects were detected; Batch mode is recommended."
                    : localizationService.CurrentLanguage == AppLanguage.Russian
                        ? "Вложенные проекты не найдены — рекомендуется режим «Один проект»."
                        : "No nested projects were found; Single project mode is recommended.");
    }

    private static string? SelectPreferredCalculationElvxPath(IEnumerable<string> files)
    {
        return files
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(path => path.EndsWith("01.elvx", StringComparison.OrdinalIgnoreCase))
            ?? files.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
    }

    private static string[] GetCalculationElvxCandidates(string path)
    {
        string[] topLevelFiles = Directory.GetFiles(path, "*.elvx", SearchOption.TopDirectoryOnly);
        if (topLevelFiles.Length > 0)
        {
            return topLevelFiles;
        }

        return Directory
            .GetFiles(path, "*.elvx", SearchOption.AllDirectories)
            .Where(filePath => !IsKnownScenarioFolder(Path.GetDirectoryName(filePath)))
            .ToArray();
    }

    private static bool IsKnownScenarioFolder(string? directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return false;
        }

        string folderName = Path.GetFileName(directoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return folderName.Equals("morning", StringComparison.OrdinalIgnoreCase) ||
               folderName.Equals("lunch", StringComparison.OrdinalIgnoreCase);
    }

    private BuildingType? GetSelectedBuildingType()
    {
        if (OfficeRadioButton.IsChecked == true)
        {
            return BuildingType.Office;
        }

        if (ResidenceRadioButton.IsChecked == true)
        {
            return BuildingType.Residence;
        }

        if (HotelRadioButton.IsChecked == true)
        {
            return BuildingType.Hotel;
        }

        return null;
    }

    private bool TryEnsureIntegrationForLaunch()
    {
        ElevateIntegrationInfo info = integrationService.GetIntegrationInfo();
        if (info.IsDetected)
        {
            return true;
        }

        SetStatus(Text.IntegrationMissingLaunch, InfoBarSeverity.Error);
        return false;
    }

    private void RefreshIntegrationStatus(bool showStatusMessage)
    {
        ElevateIntegrationInfo info = integrationService.GetIntegrationInfo();

        if (!showStatusMessage)
        {
            return;
        }

        if (info.IsDetected)
        {
            string versionPart = string.IsNullOrWhiteSpace(info.ProductVersion)
                ? string.Empty
                : string.Format(
                    localizationService.CurrentCulture,
                    Text.IntegrationVersionFormat,
                    info.ProductVersion);
            SetStatus(
                string.Format(
                    localizationService.CurrentCulture,
                    Text.IntegrationFoundFormat,
                    versionPart,
                    info.ExecutablePath),
                InfoBarSeverity.Success);
            return;
        }

        SetStatus(Text.IntegrationMissingCheck, InfoBarSeverity.Warning);
    }

    private JobProgressViewModel CreateJob(
        string path,
        BuildingType buildingType,
        bool includeLunchPeak,
        string? title = null,
        string? reportOutputRoot = null,
        string? leaseOwnerId = null)
    {
        JobProgressViewModel job = new(
            nextJobId++,
            path,
            buildingType,
            includeLunchPeak,
            localizationService,
            title,
            reportOutputRoot,
            leaseOwnerId);
        if (string.IsNullOrWhiteSpace(title))
        {
            Jobs.Insert(0, job);
        }
        else
        {
            Jobs.Add(job);
        }

        RefreshJobsSummary();
        return job;
    }

    private string FormatResultMessage(ProcessingResult result)
    {
        string message = string.IsNullOrWhiteSpace(result.Message)
            ? Text.OperationFailedMessage
            : localizationService.TranslateRuntimeMessage(result.Message);
        if (!message.Contains(Text.OperationFailedMessage, StringComparison.OrdinalIgnoreCase))
        {
            message = $"{Text.OperationFailedMessage} {message}";
        }

        if (result.Exception is null)
        {
            return message;
        }

        return $"{message} | {localizationService.TranslateRuntimeMessage(result.Exception.Message)}";
    }

    private string BuildExceptionMessage(Exception exception)
    {
        string message = localizationService.TranslateRuntimeMessage(exception.Message);
        if (exception.InnerException is not null)
        {
            message = $"{message} | {localizationService.TranslateRuntimeMessage(exception.InnerException.Message)}";
        }

        return $"{Text.OperationFailedMessage} {message}";
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            _ = DispatcherQueue.TryEnqueue(() => OnLanguageChanged(sender, e));
            return;
        }

        bool runtimeStatusWasOpen = StatusInfoBar.IsOpen;
        string previousStatusMessage = StatusInfoBar.Message;
        InfoBarSeverity previousStatusSeverity = StatusInfoBar.Severity;
        AppLanguage previousStatusLanguage = statusMessageLanguage;

        foreach (JobProgressViewModel job in Jobs)
        {
            job.ApplyLocalization(localizationService);
        }

        dismissedJob?.ApplyLocalization(localizationService);

        foreach (FloorEditorRowViewModel row in editorFloors)
        {
            row.ApplyLocalization(localizationService);
        }

        foreach (CarEditorRowViewModel row in editorCars)
        {
            row.ApplyLocalization(localizationService);
        }

        UpdateLanguageSelector();

        if (App.MainWindow is not null)
        {
            App.MainWindow.Title = Text.WindowTitle;
        }

        Bindings.Update();
        UpdateProjectModeControlsText();
        RefreshCalculationFileStatus();
        RefreshJobsSummary();

        string localizedStatusMessage;
        InfoBarSeverity localizedStatusSeverity;
        if (dismissedJob is not null && StatusActionButton.Visibility == Visibility.Visible)
        {
            localizedStatusMessage = string.Format(
                localizationService.CurrentCulture,
                Text.JobDismissedFormat,
                dismissedJob.Title);
            localizedStatusSeverity = InfoBarSeverity.Informational;
        }
        else
        {
            localizedStatusMessage = localizationService.RelocalizeCatalogMessage(
                previousStatusMessage,
                previousStatusLanguage);
            localizedStatusSeverity = previousStatusSeverity;
        }

        StatusInfoBar.Title = Text.StatusTitle;
        StatusInfoBar.Message = localizedStatusMessage;
        StatusInfoBar.Severity = localizedStatusSeverity;
        statusMessageLanguage = localizationService.CurrentLanguage;
        AutomationProperties.SetLiveSetting(
            StatusInfoBar,
            localizedStatusSeverity is InfoBarSeverity.Error or InfoBarSeverity.Warning
                ? AutomationLiveSetting.Assertive
                : AutomationLiveSetting.Polite);
        StatusInfoBar.IsOpen = runtimeStatusWasOpen;
        AutomationProperties.SetName(StatusInfoBar, $"{Text.StatusTitle}: {localizedStatusMessage}");
        if (runtimeStatusWasOpen)
        {
            RaiseLiveRegionChanged(StatusInfoBar);
        }
    }

    private sealed record ProjectPathAnalysis(
        string Path,
        bool SuggestedBatchMode,
        int CalculationFileCount,
        string? PreferredCalculationFileName,
        string? ErrorMessage);

    private sealed class MetricsReadState
    {
        public DateTimeOffset LastReadStartedUtc { get; set; }

        public int Generation { get; set; }

        public CancellationTokenSource? Source { get; set; }
    }

    private enum ProjectInputMode
    {
        Standard,
        ProjectBatch,
    }

    public sealed class JobProgressViewModel : INotifyPropertyChanged
    {
        private readonly int jobId;
        private readonly string path;
        private readonly BuildingType buildingType;
        private readonly bool includeLunchPeak;
        private readonly string? customTitle;
        private readonly string? reportOutputRoot;
        private readonly string leaseOwnerId;
        private readonly DateTimeOffset createdAtUtc = DateTimeOffset.UtcNow;
        private readonly ScenarioProgressViewModel? primaryScenario;
        private readonly ScenarioProgressViewModel? morningScenario;
        private readonly ScenarioProgressViewModel? lunchScenario;
        private JobScenarioKind activeScenarioKind;
        private JobStateKind stateKind;
        private JobStateKind? manualReportReturnState;
        private string? failureMessage;
        private string? failureSourceMessage;
        private string? failureSourceExceptionMessage;
        private string? failureRussianOverride;
        private bool failureIncludesOperationPrefix;
        private string title;
        private string details;
        private string statusText;
        private string reportButtonText;
        private string stopButtonText;
        private string dismissButtonText;
        private bool isRunning;
        private bool isFinished;
        private bool reportActionEnabled = true;
        private bool stopRequested;
        private bool primaryRunFinished;
        private bool completionStarted;
        private readonly HashSet<CancellationTokenSource> stopSources = [];

        public JobProgressViewModel(
            int jobId,
            string path,
            BuildingType buildingType,
            bool includeLunchPeak,
            AppLocalizationService localizationService,
            string? customTitle = null,
            string? reportOutputRoot = null,
            string? leaseOwnerId = null)
        {
            this.jobId = jobId;
            this.path = path;
            this.buildingType = buildingType;
            this.includeLunchPeak = includeLunchPeak;
            this.customTitle = customTitle;
            this.reportOutputRoot = reportOutputRoot;
            this.leaseOwnerId = string.IsNullOrWhiteSpace(leaseOwnerId)
                ? Guid.NewGuid().ToString("N")
                : leaseOwnerId;

            title = string.Empty;
            details = string.Empty;
            statusText = string.Empty;
            reportButtonText = string.Empty;
            stopButtonText = string.Empty;
            dismissButtonText = string.Empty;
            stateKind = JobStateKind.Queued;

            bool isOffice = buildingType == BuildingType.Office;
            if (isOffice && includeLunchPeak)
            {
                morningScenario = new ScenarioProgressViewModel(this, JobScenarioKind.Morning);
                lunchScenario = new ScenarioProgressViewModel(this, JobScenarioKind.Lunch);
                Scenarios = new ObservableCollection<ScenarioProgressViewModel>
                {
                    morningScenario,
                    lunchScenario,
                };
            }
            else if (isOffice)
            {
                primaryScenario = new ScenarioProgressViewModel(this, JobScenarioKind.Morning);
                Scenarios = new ObservableCollection<ScenarioProgressViewModel>
                {
                    primaryScenario!,
                };
            }
            else
            {
                primaryScenario = new ScenarioProgressViewModel(this, JobScenarioKind.Progress);
                Scenarios = new ObservableCollection<ScenarioProgressViewModel>
                {
                    primaryScenario!,
                };
            }

            activeScenarioKind = Scenarios[0].ScenarioKind;
            ApplyLocalization(localizationService);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Title
        {
            get => title;
            private set
            {
                if (title == value)
                {
                    return;
                }

                title = value;
                OnPropertyChanged(nameof(Title));
            }
        }

        public string Details
        {
            get => details;
            private set
            {
                if (details == value)
                {
                    return;
                }

                details = value;
                OnPropertyChanged(nameof(Details));
            }
        }

        public string StatusText
        {
            get => statusText;
            private set
            {
                if (statusText == value)
                {
                    return;
                }

                statusText = value;
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(HeaderStatusText));
            }
        }

        public string JobPath => path;

        public string LeaseOwnerId => leaseOwnerId;

        public BuildingType BuildingType => buildingType;

        public bool IncludeLunchPeak => includeLunchPeak;

        public bool SupportsScenarioRetry => morningScenario is not null && lunchScenario is not null;

        public bool PrimaryRunFinished => primaryRunFinished;

        public bool AllScenariosCompleted => Scenarios.All(scenario => scenario.IsCompleted);

        public bool HasRunningScenarios => Scenarios.Any(scenario => scenario.IsRunning);

        public bool AutoGenerateReport => !string.IsNullOrWhiteSpace(reportOutputRoot);

        public string? ReportOutputRoot => reportOutputRoot;

        internal PersistedJobSnapshot CreatePersistenceSnapshot()
        {
            return new PersistedJobSnapshot(
                path,
                buildingType,
                includeLunchPeak,
                customTitle,
                reportOutputRoot,
                createdAtUtc);
        }

        public bool PrintsMultipleReports => buildingType == BuildingType.Office && includeLunchPeak;

        public string ReportButtonText
        {
            get => reportButtonText;
            private set
            {
                if (reportButtonText == value)
                {
                    return;
                }

                reportButtonText = value;
                OnPropertyChanged(nameof(ReportButtonText));
            }
        }

        public bool IsRunning
        {
            get => isRunning;
            private set
            {
                if (isRunning == value)
                {
                    return;
                }

                isRunning = value;
                OnPropertyChanged(nameof(IsRunning));
            }
        }

        public ObservableCollection<ScenarioProgressViewModel> Scenarios { get; }

        public bool IsFinished => isFinished;

        public bool IsFailed => !string.IsNullOrWhiteSpace(failureMessage);

        public bool IsReportFailed => stateKind == JobStateKind.ReportFailed;

        public string HeaderStatusText => IsFailed ? string.Empty : StatusText;

        public Visibility HeaderStatusVisibility => IsFailed ? Visibility.Collapsed : Visibility.Visible;

        public string FailureMessage => failureMessage ?? string.Empty;

        public Visibility FailureMessageVisibility => IsFailed ? Visibility.Visible : Visibility.Collapsed;

        public bool CanPrintReport => (stateKind is JobStateKind.Completed
                                          or JobStateKind.Stopped
                                          or JobStateKind.ReportFailed) &&
                                      reportActionEnabled;

        public bool CanRetry => stateKind == JobStateKind.Failed &&
                                isFinished &&
                                !isRunning &&
                                !string.IsNullOrWhiteSpace(failureMessage);

        public bool CanDismiss => isFinished && !isRunning;

        public int QueueSortGroup => !isFinished
            ? 0
            : IsFailed
                ? 2
                : 1;

        public string RetryButtonText { get; private set; } = string.Empty;

        public string StopButtonText
        {
            get => stopButtonText;
            private set
            {
                if (stopButtonText == value)
                {
                    return;
                }

                stopButtonText = value;
                OnPropertyChanged(nameof(StopButtonText));
            }
        }

        public string DismissButtonText
        {
            get => dismissButtonText;
            private set
            {
                if (dismissButtonText == value)
                {
                    return;
                }

                dismissButtonText = value;
                OnPropertyChanged(nameof(DismissButtonText));
            }
        }

        public bool StopRequested => stopRequested;

        public bool IsQueued => stateKind == JobStateKind.Queued;

        public bool WasStoppedEarly => stateKind == JobStateKind.Stopped ||
                                       (stateKind == JobStateKind.Reporting &&
                                        manualReportReturnState == JobStateKind.Stopped) ||
                                       stopRequested;

        public bool CanStop => (stateKind is JobStateKind.Queued
                                    or JobStateKind.Preparing
                                    or JobStateKind.Running
                                    or JobStateKind.Reporting) &&
                               isRunning &&
                               !isFinished &&
                               !stopRequested &&
                               stopSources.Count > 0;

        public Visibility PrintReportVisibility => stateKind is JobStateKind.Completed
                                                       or JobStateKind.Stopped
                                                       or JobStateKind.ReportFailed
            ? Visibility.Visible
            : Visibility.Collapsed;

        public Visibility RetryButtonVisibility => CanRetry
            ? Visibility.Visible
            : Visibility.Collapsed;

        public Visibility StopButtonVisibility => CanStop
            ? Visibility.Visible
            : Visibility.Collapsed;

        public Visibility DismissButtonVisibility => CanDismiss
            ? Visibility.Visible
            : Visibility.Collapsed;

        public void AttachStopSource(CancellationTokenSource cancellationTokenSource)
        {
            stopSources.Add(cancellationTokenSource);
            NotifyStopActionStateChanged();
        }

        public void DetachStopSource(CancellationTokenSource cancellationTokenSource)
        {
            stopSources.Remove(cancellationTokenSource);
            NotifyStopActionStateChanged();
        }

        public void RequestStop(AppLocalizationService localizationService)
        {
            if (!CanStop)
            {
                return;
            }

            stopRequested = true;
            stateKind = JobStateKind.Stopping;
            StatusText = localizationService.GetJobStateLabel(JobStateKind.Stopping);
            NotifyStopActionStateChanged();
            foreach (CancellationTokenSource source in stopSources.ToArray())
            {
                try
                {
                    source.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }

        public void MarkQueued(AppLocalizationService localizationService)
        {
            isFinished = false;
            SetFailureMessage(null);
            manualReportReturnState = null;
            stopRequested = false;
            stateKind = JobStateKind.Queued;
            IsRunning = true;
            StatusText = localizationService.GetJobStateLabel(JobStateKind.Queued);
            NotifyReportActionStateChanged();
        }

        public void MarkPreparing(AppLocalizationService localizationService)
        {
            stateKind = JobStateKind.Preparing;
            IsRunning = true;
            StatusText = localizationService.GetJobStateLabel(JobStateKind.Preparing);
            NotifyStopActionStateChanged();
        }

        public void MarkRunning(AppLocalizationService localizationService)
        {
            isFinished = false;
            primaryRunFinished = false;
            completionStarted = false;
            SetFailureMessage(null);
            manualReportReturnState = null;
            stopRequested = false;
            stateKind = JobStateKind.Running;
            IsRunning = true;
            StatusText = localizationService.GetJobStateLabel(JobStateKind.Running);
            NotifyReportActionStateChanged();
        }

        public void MarkReporting(AppLocalizationService localizationService)
        {
            manualReportReturnState = null;
            stateKind = JobStateKind.Reporting;
            IsRunning = true;
            StatusText = localizationService.GetJobStateLabel(JobStateKind.Reporting);
            NotifyReportActionStateChanged();
        }

        public void BeginManualReport(AppLocalizationService localizationService)
        {
            manualReportReturnState = stateKind == JobStateKind.Stopped
                ? JobStateKind.Stopped
                : JobStateKind.Completed;
            isFinished = false;
            SetFailureMessage(null);
            stateKind = JobStateKind.Reporting;
            IsRunning = true;
            StatusText = localizationService.GetJobStateLabel(JobStateKind.Reporting);
            NotifyReportActionStateChanged();
        }

        public void CompleteManualReport(AppLocalizationService localizationService)
        {
            JobStateKind returnState = manualReportReturnState ?? JobStateKind.Completed;
            manualReportReturnState = null;
            if (returnState == JobStateKind.Stopped)
            {
                MarkStopped(localizationService);
                return;
            }

            MarkCompleted(localizationService);
        }

        public void CancelManualReport(AppLocalizationService localizationService)
        {
            CompleteManualReport(localizationService);
        }

        public void MarkCompleted(AppLocalizationService localizationService)
        {
            isFinished = true;
            SetFailureMessage(null);
            manualReportReturnState = null;
            stopRequested = false;
            stateKind = JobStateKind.Completed;
            IsRunning = false;
            StatusText = localizationService.GetJobStateLabel(JobStateKind.Completed);
            NotifyReportActionStateChanged();
        }

        public void MarkStopped(AppLocalizationService localizationService)
        {
            isFinished = true;
            SetFailureMessage(null);
            manualReportReturnState = null;
            stopRequested = false;
            stateKind = JobStateKind.Stopped;
            IsRunning = false;
            StatusText = localizationService.GetJobStateLabel(JobStateKind.Stopped);
            NotifyReportActionStateChanged();
        }

        public void MarkFailed(ProcessingResult result, AppLocalizationService localizationService)
        {
            MarkFailed(
                result.Message,
                result.Exception?.Message,
                localizationService,
                includeOperationPrefix: true);
        }

        public void MarkFailed(Exception exception, AppLocalizationService localizationService)
        {
            MarkFailed(
                exception.Message,
                exception.InnerException?.Message,
                localizationService,
                includeOperationPrefix: true);
        }

        public void MarkFailed(
            string sourceMessage,
            AppLocalizationService localizationService,
            string? russianOverride = null,
            bool includeOperationPrefix = false)
        {
            isFinished = true;
            manualReportReturnState = null;
            stopRequested = false;
            stateKind = JobStateKind.Failed;
            SetFailureSource(
                sourceMessage,
                sourceExceptionMessage: null,
                localizationService,
                includeOperationPrefix,
                russianOverride);
            IsRunning = false;
            StatusText = FailureMessage;
            NotifyReportActionStateChanged();
        }

        private void MarkFailed(
            string sourceMessage,
            string? sourceExceptionMessage,
            AppLocalizationService localizationService,
            bool includeOperationPrefix)
        {
            isFinished = true;
            manualReportReturnState = null;
            stopRequested = false;
            stateKind = JobStateKind.Failed;
            SetFailureSource(
                sourceMessage,
                sourceExceptionMessage,
                localizationService,
                includeOperationPrefix);
            IsRunning = false;
            StatusText = FailureMessage;
            NotifyReportActionStateChanged();
        }

        public void MarkReportFailed(
            ProcessingResult result,
            AppLocalizationService localizationService)
        {
            MarkReportFailed(
                result.Message,
                result.Exception?.Message,
                localizationService);
        }

        public void MarkReportFailed(Exception exception, AppLocalizationService localizationService)
        {
            MarkReportFailed(
                exception.Message,
                exception.InnerException?.Message,
                localizationService);
        }

        private void MarkReportFailed(
            string sourceMessage,
            string? sourceExceptionMessage,
            AppLocalizationService localizationService)
        {
            isFinished = true;
            manualReportReturnState = null;
            stopRequested = false;
            stateKind = JobStateKind.ReportFailed;
            SetFailureSource(
                sourceMessage,
                sourceExceptionMessage,
                localizationService,
                includeOperationPrefix: true);
            IsRunning = false;
            StatusText = FailureMessage;
            NotifyReportActionStateChanged();
        }

        public void MarkPrimaryRunFinished()
        {
            primaryRunFinished = true;
        }

        public bool TryBeginCompletion()
        {
            if (completionStarted)
            {
                return false;
            }

            completionStarted = true;
            return true;
        }

        public void MarkScenarioFailed(
            string? scenario,
            string sourceMessage,
            AppLocalizationService localizationService)
        {
            MarkScenarioFailed(
                ResolveScenario(scenario),
                sourceMessage,
                sourceExceptionMessage: null,
                localizationService,
                includeOperationPrefix: false);
        }

        public void MarkScenarioFailed(
            ScenarioProgressViewModel scenario,
            ProcessingResult result,
            AppLocalizationService localizationService)
        {
            MarkScenarioFailed(
                scenario,
                result.Message,
                result.Exception?.Message,
                localizationService,
                includeOperationPrefix: true);
        }

        public void MarkScenarioFailed(
            ScenarioProgressViewModel scenario,
            Exception exception,
            AppLocalizationService localizationService)
        {
            MarkScenarioFailed(
                scenario,
                exception.Message,
                exception.InnerException?.Message,
                localizationService,
                includeOperationPrefix: true);
        }

        private void MarkScenarioFailed(
            ScenarioProgressViewModel scenario,
            string sourceMessage,
            string? sourceExceptionMessage,
            AppLocalizationService localizationService,
            bool includeOperationPrefix)
        {
            scenario.MarkFailed(
                sourceMessage,
                sourceExceptionMessage,
                localizationService,
                includeOperationPrefix);
            activeScenarioKind = scenario.ScenarioKind;
            StatusText = $"{scenario.Label}: {localizationService.CurrentText.OperationFailedMessage}";
        }

        public void MarkScenarioRetryRunning(
            ScenarioProgressViewModel scenario,
            AppLocalizationService localizationService)
        {
            completionStarted = false;
            isFinished = false;
            SetFailureMessage(null);
            scenario.MarkRetrying();
            activeScenarioKind = scenario.ScenarioKind;
            stateKind = JobStateKind.Running;
            IsRunning = true;
            StatusText = $"{scenario.Label}: {localizationService.GetJobStateLabel(JobStateKind.Running)}";
            NotifyReportActionStateChanged();
        }

        public void MarkScenarioCompleted(
            ScenarioProgressViewModel scenario,
            AppLocalizationService localizationService)
        {
            scenario.MarkCompleted();
            activeScenarioKind = scenario.ScenarioKind;
            StatusText = $"{scenario.Label}: {localizationService.GetJobStateLabel(JobStateKind.Completed)}";
        }

        public void MarkScenarioRecoveryPending(AppLocalizationService localizationService)
        {
            isFinished = false;
            SetFailureMessage(null);
            stateKind = JobStateKind.Running;
            IsRunning = true;
            StatusText = localizationService.GetJobStateLabel(JobStateKind.Running);
            NotifyReportActionStateChanged();
        }

        public void UpdateProgress(
            string? scenario,
            int completed,
            int total,
            bool isFinal,
            AppLocalizationService localizationService)
        {
            if (isFinished)
            {
                return;
            }

            ScenarioProgressViewModel target = ResolveScenario(scenario);
            activeScenarioKind = target.ScenarioKind;
            target.Update(completed, total, isFinal);
            if (stopRequested)
            {
                stateKind = JobStateKind.Stopping;
                StatusText = localizationService.GetJobStateLabel(JobStateKind.Stopping);
                return;
            }

            StatusText = string.IsNullOrWhiteSpace(target.Label)
                ? $"{completed}/{total}"
                : $"{target.Label}: {completed}/{total}";
            stateKind = JobStateKind.Running;
            IsRunning = true;
            SetFailureMessage(null);

            if (completed == 0 && total == 0)
            {
                StatusText = localizationService.GetJobStateLabel(JobStateKind.Running);
            }
        }

        public void UpdateScenarioMetrics(
            string? scenario,
            ElevateResultMetrics metrics,
            System.Globalization.CultureInfo culture)
        {
            ResolveScenario(scenario).UpdateMetrics(metrics, culture);
        }

        public void ApplyLocalization(AppLocalizationService localizationService)
        {
            Title = string.IsNullOrWhiteSpace(customTitle)
                ? localizationService.FormatJobTitle(jobId, buildingType)
                : customTitle;
            Details = localizationService.FormatJobDetails(path, buildingType, includeLunchPeak);
            ReportButtonText = PrintsMultipleReports
                ? localizationService.CurrentText.PrintReportsButton
                : localizationService.CurrentText.ReportButton;
            RetryButtonText = localizationService.CurrentText.ProjectBatchRetryButton;
            StopButtonText = localizationService.CurrentText.StopJobButton;
            DismissButtonText = localizationService.CurrentText.DismissJobButton;
            OnPropertyChanged(nameof(RetryButtonText));

            foreach (ScenarioProgressViewModel scenario in Scenarios)
            {
                scenario.ApplyLocalization(localizationService);
            }

            if (stateKind is JobStateKind.Failed or JobStateKind.ReportFailed)
            {
                SetFailureMessage(BuildLocalizedFailureMessage(localizationService));
                StatusText = FailureMessage;
                return;
            }

            switch (stateKind)
            {
                case JobStateKind.Completed:
                    StatusText = localizationService.GetJobStateLabel(JobStateKind.Completed);
                    break;
                case JobStateKind.Stopped:
                    StatusText = localizationService.GetJobStateLabel(JobStateKind.Stopped);
                    break;
                case JobStateKind.Stopping:
                    StatusText = localizationService.GetJobStateLabel(JobStateKind.Stopping);
                    break;
                case JobStateKind.Preparing:
                    StatusText = localizationService.GetJobStateLabel(JobStateKind.Preparing);
                    break;
                case JobStateKind.Reporting:
                    StatusText = localizationService.GetJobStateLabel(JobStateKind.Reporting);
                    break;
                case JobStateKind.ReportFailed:
                case JobStateKind.Failed:
                    StatusText = failureMessage ??
                        localizationService.GetJobStateLabel(JobStateKind.Failed);
                    break;
                case JobStateKind.Running:
                    {
                        ScenarioProgressViewModel activeScenario = ResolveScenario(activeScenarioKind);
                        StatusText = activeScenario.Total > 0 || activeScenario.Completed > 0
                            ? $"{activeScenario.Label}: {activeScenario.Completed}/{activeScenario.Total}"
                            : localizationService.GetJobStateLabel(JobStateKind.Running);
                        break;
                    }
                default:
                    StatusText = localizationService.GetJobStateLabel(JobStateKind.Queued);
                    break;
            }
        }

        public void SetReportActionEnabled(bool isEnabled)
        {
            if (reportActionEnabled == isEnabled)
            {
                return;
            }

            reportActionEnabled = isEnabled;
            OnPropertyChanged(nameof(CanPrintReport));
        }

        private ScenarioProgressViewModel ResolveScenario(string? scenario)
        {
            if (morningScenario is not null && IsMorningScenario(scenario))
            {
                return morningScenario;
            }

            if (lunchScenario is not null && IsLunchScenario(scenario))
            {
                return lunchScenario;
            }

            return primaryScenario ?? morningScenario ?? lunchScenario!;
        }

        private ScenarioProgressViewModel ResolveScenario(JobScenarioKind scenarioKind)
        {
            if (scenarioKind == JobScenarioKind.Morning && morningScenario is not null)
            {
                return morningScenario;
            }

            if (scenarioKind == JobScenarioKind.Lunch && lunchScenario is not null)
            {
                return lunchScenario;
            }

            return primaryScenario ?? morningScenario ?? lunchScenario!;
        }

        private static bool IsMorningScenario(string? scenario)
        {
            return ContainsScenario(scenario, "morning");
        }

        private static bool IsLunchScenario(string? scenario)
        {
            return ContainsScenario(scenario, "lunch");
        }

        private static bool ContainsScenario(string? scenario, string token)
        {
            return !string.IsNullOrWhiteSpace(scenario) &&
                   scenario.Contains(token, StringComparison.OrdinalIgnoreCase);
        }

        private void SetFailureMessage(string? message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                failureSourceMessage = null;
                failureSourceExceptionMessage = null;
                failureRussianOverride = null;
                failureIncludesOperationPrefix = false;
            }

            if (failureMessage == message)
            {
                return;
            }

            failureMessage = message;
            OnPropertyChanged(nameof(FailureMessage));
            OnPropertyChanged(nameof(FailureMessageVisibility));
            OnPropertyChanged(nameof(HeaderStatusText));
            OnPropertyChanged(nameof(HeaderStatusVisibility));
            OnPropertyChanged(nameof(IsFailed));
            OnPropertyChanged(nameof(CanRetry));
            OnPropertyChanged(nameof(RetryButtonVisibility));
            OnPropertyChanged(nameof(QueueSortGroup));
        }

        private void SetFailureSource(
            string? sourceMessage,
            string? sourceExceptionMessage,
            AppLocalizationService localizationService,
            bool includeOperationPrefix,
            string? russianOverride = null)
        {
            failureSourceMessage = sourceMessage;
            failureSourceExceptionMessage = sourceExceptionMessage;
            failureRussianOverride = russianOverride;
            failureIncludesOperationPrefix = includeOperationPrefix;
            SetFailureMessage(BuildLocalizedFailureMessage(localizationService));
        }

        private string BuildLocalizedFailureMessage(AppLocalizationService localizationService)
        {
            string sourceMessage = StripOperationFailurePrefix(failureSourceMessage ?? string.Empty);
            string translatedMessage = localizationService.CurrentLanguage == AppLanguage.Russian &&
                                       !string.IsNullOrWhiteSpace(failureRussianOverride)
                ? failureRussianOverride
                : localizationService.TranslateRuntimeMessage(sourceMessage);
            string operationPrefix = localizationService.CurrentText.OperationFailedMessage;
            string message = string.IsNullOrWhiteSpace(translatedMessage)
                ? operationPrefix
                : translatedMessage;

            if (failureIncludesOperationPrefix &&
                !message.StartsWith(operationPrefix, StringComparison.OrdinalIgnoreCase))
            {
                message = $"{operationPrefix} {message}";
            }

            string exceptionMessage = StripOperationFailurePrefix(
                failureSourceExceptionMessage ?? string.Empty);
            if (string.IsNullOrWhiteSpace(exceptionMessage))
            {
                return message;
            }

            string translatedException = localizationService.TranslateRuntimeMessage(exceptionMessage);
            return string.Equals(
                    translatedException,
                    translatedMessage,
                    StringComparison.OrdinalIgnoreCase)
                ? message
                : $"{message} | {translatedException}";
        }

        private static string StripOperationFailurePrefix(string message)
        {
            string trimmed = message.Trim();
            foreach (string prefix in new[]
                     {
                         "Operation failed.",
                         "Операция завершилась ошибкой.",
                     })
            {
                if (!trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return trimmed[prefix.Length..].TrimStart();
            }

            return trimmed;
        }

        private void NotifyReportActionStateChanged()
        {
            OnPropertyChanged(nameof(IsFinished));
            OnPropertyChanged(nameof(CanPrintReport));
            OnPropertyChanged(nameof(PrintReportVisibility));
            OnPropertyChanged(nameof(CanRetry));
            OnPropertyChanged(nameof(RetryButtonVisibility));
            OnPropertyChanged(nameof(CanDismiss));
            OnPropertyChanged(nameof(DismissButtonVisibility));
            OnPropertyChanged(nameof(IsFailed));
            OnPropertyChanged(nameof(HeaderStatusText));
            OnPropertyChanged(nameof(HeaderStatusVisibility));
            OnPropertyChanged(nameof(FailureMessage));
            OnPropertyChanged(nameof(FailureMessageVisibility));
            OnPropertyChanged(nameof(QueueSortGroup));
            OnPropertyChanged(nameof(WasStoppedEarly));
            OnPropertyChanged(nameof(IsQueued));
            NotifyStopActionStateChanged();
        }

        private void NotifyStopActionStateChanged()
        {
            OnPropertyChanged(nameof(StopRequested));
            OnPropertyChanged(nameof(WasStoppedEarly));
            OnPropertyChanged(nameof(CanStop));
            OnPropertyChanged(nameof(StopButtonVisibility));
            OnPropertyChanged(nameof(IsQueued));
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed class ScenarioProgressViewModel : INotifyPropertyChanged
    {
        private readonly JobProgressViewModel job;
        private readonly JobScenarioKind scenarioKind;
        private string label;
        private int completed;
        private int total;
        private double value;
        private double maximum;
        private bool isIndeterminate = true;
        private string progressText = "0/0";
        private string metricsText = string.Empty;
        private ElevateResultMetrics? metrics;
        private string failureMessage = string.Empty;
        private string? failureSourceMessage;
        private string? failureSourceExceptionMessage;
        private bool failureIncludesOperationPrefix;
        private string retryButtonText = string.Empty;
        private bool isRunning;
        private bool isCompleted;

        public ScenarioProgressViewModel(
            JobProgressViewModel job,
            JobScenarioKind scenarioKind)
        {
            this.job = job;
            this.scenarioKind = scenarioKind;
            label = string.Empty;
            maximum = 1;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public JobScenarioKind ScenarioKind => scenarioKind;

        public JobProgressViewModel Job => job;

        public bool IsRunning
        {
            get => isRunning;
            private set
            {
                if (isRunning == value)
                {
                    return;
                }

                isRunning = value;
                OnPropertyChanged(nameof(IsRunning));
                NotifyRetryStateChanged();
            }
        }

        public bool IsCompleted
        {
            get => isCompleted;
            private set
            {
                if (isCompleted == value)
                {
                    return;
                }

                isCompleted = value;
                OnPropertyChanged(nameof(IsCompleted));
            }
        }

        public string FailureMessage
        {
            get => failureMessage;
            private set
            {
                if (failureMessage == value)
                {
                    return;
                }

                failureMessage = value;
                OnPropertyChanged(nameof(FailureMessage));
                OnPropertyChanged(nameof(FailureMessageVisibility));
                NotifyRetryStateChanged();
            }
        }

        public Visibility FailureMessageVisibility => string.IsNullOrWhiteSpace(FailureMessage)
            ? Visibility.Collapsed
            : Visibility.Visible;

        public bool CanRetry => scenarioKind is JobScenarioKind.Morning or JobScenarioKind.Lunch &&
                                !IsRunning &&
                                !string.IsNullOrWhiteSpace(FailureMessage);

        public Visibility RetryButtonVisibility => CanRetry
            ? Visibility.Visible
            : Visibility.Collapsed;

        public string RetryButtonText
        {
            get => retryButtonText;
            private set
            {
                if (retryButtonText == value)
                {
                    return;
                }

                retryButtonText = value;
                OnPropertyChanged(nameof(RetryButtonText));
            }
        }

        public string Label
        {
            get => label;
            private set
            {
                if (label == value)
                {
                    return;
                }

                label = value;
                OnPropertyChanged(nameof(Label));
            }
        }

        public string ProgressText
        {
            get => progressText;
            private set
            {
                if (progressText == value)
                {
                    return;
                }

                progressText = value;
                OnPropertyChanged(nameof(ProgressText));
            }
        }

        public string MetricsText
        {
            get => metricsText;
            set
            {
                if (metricsText == value)
                {
                    return;
                }

                metricsText = value;
                OnPropertyChanged(nameof(MetricsText));
                OnPropertyChanged(nameof(MetricsVisibility));
            }
        }

        public Visibility MetricsVisibility => string.IsNullOrWhiteSpace(metricsText)
            ? Visibility.Collapsed
            : Visibility.Visible;

        public int Completed
        {
            get => completed;
            private set
            {
                if (completed == value)
                {
                    return;
                }

                completed = value;
                OnPropertyChanged(nameof(Completed));
            }
        }

        public int Total
        {
            get => total;
            private set
            {
                if (total == value)
                {
                    return;
                }

                total = value;
                OnPropertyChanged(nameof(Total));
            }
        }

        public double Value
        {
            get => value;
            private set
            {
                if (Math.Abs(this.value - value) < double.Epsilon)
                {
                    return;
                }

                this.value = value;
                OnPropertyChanged(nameof(Value));
            }
        }

        public double Maximum
        {
            get => maximum;
            private set
            {
                if (Math.Abs(maximum - value) < double.Epsilon)
                {
                    return;
                }

                maximum = value;
                OnPropertyChanged(nameof(Maximum));
            }
        }

        public bool IsIndeterminate
        {
            get => isIndeterminate;
            private set
            {
                if (isIndeterminate == value)
                {
                    return;
                }

                isIndeterminate = value;
                OnPropertyChanged(nameof(IsIndeterminate));
            }
        }

        public void Update(int completed, int total, bool isFinal)
        {
            Completed = Math.Max(0, completed);
            Total = Math.Max(0, total);
            Maximum = Total > 0 ? Total : 1;
            Value = Total > 0 ? Math.Min(Completed, Total) : 0;
            IsIndeterminate = Total <= 0;
            ProgressText = $"{Completed}/{Total}";
            ClearFailure();
            IsCompleted = isFinal;
            IsRunning = !isFinal;
        }

        public void MarkFailed(
            string sourceMessage,
            string? sourceExceptionMessage,
            AppLocalizationService localizationService,
            bool includeOperationPrefix)
        {
            failureSourceMessage = sourceMessage;
            failureSourceExceptionMessage = sourceExceptionMessage;
            failureIncludesOperationPrefix = includeOperationPrefix;
            FailureMessage = BuildLocalizedFailureMessage(localizationService);
            IsCompleted = false;
            IsRunning = false;
        }

        public void MarkRetrying()
        {
            ClearFailure();
            IsCompleted = false;
            IsRunning = true;
            Completed = 0;
            Value = 0;
            ProgressText = $"0/{Total}";
            IsIndeterminate = Total <= 0;
        }

        public void MarkCompleted()
        {
            ClearFailure();
            IsCompleted = true;
            IsRunning = false;
            if (Total > 0)
            {
                Completed = Total;
                Value = Total;
                ProgressText = $"{Total}/{Total}";
            }
        }

        public void UpdateMetrics(
            ElevateResultMetrics value,
            System.Globalization.CultureInfo culture)
        {
            metrics = value;
            MetricsText = ElevateResultMetricsService.Format(value, culture);
        }

        public void ApplyLocalization(AppLocalizationService localizationService)
        {
            Label = localizationService.GetScenarioLabel(scenarioKind);
            RetryButtonText = localizationService.CurrentText.ProjectBatchRetryButton;
            if (metrics is not null)
            {
                MetricsText = ElevateResultMetricsService.Format(
                    metrics,
                    localizationService.CurrentCulture);
            }

            if (!string.IsNullOrWhiteSpace(failureSourceMessage))
            {
                FailureMessage = BuildLocalizedFailureMessage(localizationService);
            }
        }

        private void ClearFailure()
        {
            failureSourceMessage = null;
            failureSourceExceptionMessage = null;
            failureIncludesOperationPrefix = false;
            FailureMessage = string.Empty;
        }

        private string BuildLocalizedFailureMessage(AppLocalizationService localizationService)
        {
            string sourceMessage = StripOperationFailurePrefix(failureSourceMessage ?? string.Empty);
            string translatedMessage = localizationService.TranslateRuntimeMessage(sourceMessage);
            string operationPrefix = localizationService.CurrentText.OperationFailedMessage;
            string message = string.IsNullOrWhiteSpace(translatedMessage)
                ? operationPrefix
                : translatedMessage;

            if (failureIncludesOperationPrefix &&
                !message.StartsWith(operationPrefix, StringComparison.OrdinalIgnoreCase))
            {
                message = $"{operationPrefix} {message}";
            }

            string exceptionMessage = StripOperationFailurePrefix(
                failureSourceExceptionMessage ?? string.Empty);
            if (string.IsNullOrWhiteSpace(exceptionMessage))
            {
                return message;
            }

            string translatedException = localizationService.TranslateRuntimeMessage(exceptionMessage);
            return string.Equals(
                    translatedException,
                    translatedMessage,
                    StringComparison.OrdinalIgnoreCase)
                ? message
                : $"{message} | {translatedException}";
        }

        private static string StripOperationFailurePrefix(string message)
        {
            string trimmed = message.Trim();
            foreach (string prefix in new[]
                     {
                         "Operation failed.",
                         "Операция завершилась ошибкой.",
                     })
            {
                if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return trimmed[prefix.Length..].TrimStart();
                }
            }

            return trimmed;
        }

        private void NotifyRetryStateChanged()
        {
            OnPropertyChanged(nameof(CanRetry));
            OnPropertyChanged(nameof(RetryButtonVisibility));
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed class FloorEditorRowViewModel : INotifyPropertyChanged
    {
        private string floorLevelText;
        private string populationText;
        private string levelLabel;
        private string populationLabel;
        private string entranceLabel;

        public FloorEditorRowViewModel(ElevateProjectEditorFloor floor, AppLocalizationService localizationService)
        {
            FloorName = floor.FloorName;
            EntranceFloor = floor.EntranceFloor;
            floorLevelText = floor.FloorLevel.ToString("0.###", localizationService.CurrentCulture);
            populationText = floor.Population.ToString("0.###", localizationService.CurrentCulture);
            levelLabel = string.Empty;
            populationLabel = string.Empty;
            entranceLabel = string.Empty;
            ApplyLocalization(localizationService);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string FloorName { get; }

        public bool EntranceFloor { get; }

        public string FloorLevelText
        {
            get => floorLevelText;
            set
            {
                if (floorLevelText == value)
                {
                    return;
                }

                floorLevelText = value;
                OnPropertyChanged(nameof(FloorLevelText));
            }
        }

        public string PopulationText
        {
            get => populationText;
            set
            {
                if (populationText == value)
                {
                    return;
                }

                populationText = value;
                OnPropertyChanged(nameof(PopulationText));
            }
        }

        public string LevelLabel
        {
            get => levelLabel;
            private set
            {
                if (levelLabel == value)
                {
                    return;
                }

                levelLabel = value;
                OnPropertyChanged(nameof(LevelLabel));
            }
        }

        public string PopulationLabel
        {
            get => populationLabel;
            private set
            {
                if (populationLabel == value)
                {
                    return;
                }

                populationLabel = value;
                OnPropertyChanged(nameof(PopulationLabel));
            }
        }

        public string EntranceLabel
        {
            get => entranceLabel;
            private set
            {
                if (entranceLabel == value)
                {
                    return;
                }

                entranceLabel = value;
                OnPropertyChanged(nameof(EntranceLabel));
            }
        }

        public void ApplyLocalization(AppLocalizationService localizationService)
        {
            LevelLabel = localizationService.CurrentText.EditorFloorLevelLabel;
            PopulationLabel = localizationService.CurrentText.EditorFloorPopulationLabel;
            EntranceLabel = localizationService.CurrentText.EditorFloorEntranceLabel;
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed class CarEditorRowViewModel : INotifyPropertyChanged
    {
        private string title;
        private string capacityText;
        private string areaText;
        private string speedText;
        private string accelerationText;
        private string jerkText;
        private string preOpeningText;
        private string openTimeText;
        private string closeTimeText;
        private string homeFloorText;
        private string capacityLabel;
        private string areaLabel;
        private string speedLabel;
        private string accelerationLabel;
        private string jerkLabel;
        private string preOpeningLabel;
        private string openTimeLabel;
        private string closeTimeLabel;
        private string homeFloorLabel;

        public CarEditorRowViewModel(ElevateProjectEditorCar car, AppLocalizationService localizationService)
        {
            Id = car.Id;
            title = string.Empty;
            capacityText = car.CapacityKg;
            areaText = car.FloorAreaM2;
            speedText = car.Speed;
            accelerationText = car.Acceleration;
            jerkText = car.Jerk;
            preOpeningText = car.DoorPreOpening;
            openTimeText = car.DoorOpenTime;
            closeTimeText = car.DoorCloseTime;
            homeFloorText = car.HomeFloor;
            capacityLabel = string.Empty;
            areaLabel = string.Empty;
            speedLabel = string.Empty;
            accelerationLabel = string.Empty;
            jerkLabel = string.Empty;
            preOpeningLabel = string.Empty;
            openTimeLabel = string.Empty;
            closeTimeLabel = string.Empty;
            homeFloorLabel = string.Empty;
            ApplyLocalization(localizationService);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Id { get; }

        public string Title
        {
            get => title;
            private set
            {
                if (title == value)
                {
                    return;
                }

                title = value;
                OnPropertyChanged(nameof(Title));
            }
        }

        public string CapacityText
        {
            get => capacityText;
            set
            {
                if (capacityText == value)
                {
                    return;
                }

                capacityText = value;
                OnPropertyChanged(nameof(CapacityText));
            }
        }

        public string AreaText
        {
            get => areaText;
            set
            {
                if (areaText == value)
                {
                    return;
                }

                areaText = value;
                OnPropertyChanged(nameof(AreaText));
            }
        }

        public string SpeedText
        {
            get => speedText;
            set
            {
                if (speedText == value)
                {
                    return;
                }

                speedText = value;
                OnPropertyChanged(nameof(SpeedText));
            }
        }

        public string AccelerationText
        {
            get => accelerationText;
            set
            {
                if (accelerationText == value)
                {
                    return;
                }

                accelerationText = value;
                OnPropertyChanged(nameof(AccelerationText));
            }
        }

        public string JerkText
        {
            get => jerkText;
            set
            {
                if (jerkText == value)
                {
                    return;
                }

                jerkText = value;
                OnPropertyChanged(nameof(JerkText));
            }
        }

        public string PreOpeningText
        {
            get => preOpeningText;
            set
            {
                if (preOpeningText == value)
                {
                    return;
                }

                preOpeningText = value;
                OnPropertyChanged(nameof(PreOpeningText));
            }
        }

        public string OpenTimeText
        {
            get => openTimeText;
            set
            {
                if (openTimeText == value)
                {
                    return;
                }

                openTimeText = value;
                OnPropertyChanged(nameof(OpenTimeText));
            }
        }

        public string CloseTimeText
        {
            get => closeTimeText;
            set
            {
                if (closeTimeText == value)
                {
                    return;
                }

                closeTimeText = value;
                OnPropertyChanged(nameof(CloseTimeText));
            }
        }

        public string HomeFloorText
        {
            get => homeFloorText;
            set
            {
                if (homeFloorText == value)
                {
                    return;
                }

                homeFloorText = value;
                OnPropertyChanged(nameof(HomeFloorText));
            }
        }

        public string CapacityLabel
        {
            get => capacityLabel;
            private set
            {
                if (capacityLabel == value)
                {
                    return;
                }

                capacityLabel = value;
                OnPropertyChanged(nameof(CapacityLabel));
            }
        }

        public string AreaLabel
        {
            get => areaLabel;
            private set
            {
                if (areaLabel == value)
                {
                    return;
                }

                areaLabel = value;
                OnPropertyChanged(nameof(AreaLabel));
            }
        }

        public string SpeedLabel
        {
            get => speedLabel;
            private set
            {
                if (speedLabel == value)
                {
                    return;
                }

                speedLabel = value;
                OnPropertyChanged(nameof(SpeedLabel));
            }
        }

        public string AccelerationLabel
        {
            get => accelerationLabel;
            private set
            {
                if (accelerationLabel == value)
                {
                    return;
                }

                accelerationLabel = value;
                OnPropertyChanged(nameof(AccelerationLabel));
            }
        }

        public string JerkLabel
        {
            get => jerkLabel;
            private set
            {
                if (jerkLabel == value)
                {
                    return;
                }

                jerkLabel = value;
                OnPropertyChanged(nameof(JerkLabel));
            }
        }

        public string PreOpeningLabel
        {
            get => preOpeningLabel;
            private set
            {
                if (preOpeningLabel == value)
                {
                    return;
                }

                preOpeningLabel = value;
                OnPropertyChanged(nameof(PreOpeningLabel));
            }
        }

        public string OpenTimeLabel
        {
            get => openTimeLabel;
            private set
            {
                if (openTimeLabel == value)
                {
                    return;
                }

                openTimeLabel = value;
                OnPropertyChanged(nameof(OpenTimeLabel));
            }
        }

        public string CloseTimeLabel
        {
            get => closeTimeLabel;
            private set
            {
                if (closeTimeLabel == value)
                {
                    return;
                }

                closeTimeLabel = value;
                OnPropertyChanged(nameof(CloseTimeLabel));
            }
        }

        public string HomeFloorLabel
        {
            get => homeFloorLabel;
            private set
            {
                if (homeFloorLabel == value)
                {
                    return;
                }

                homeFloorLabel = value;
                OnPropertyChanged(nameof(HomeFloorLabel));
            }
        }

        public void ApplyLocalization(AppLocalizationService localizationService)
        {
            Title = localizationService.CurrentLanguage == AppLanguage.Russian
                ? $"Лифт {Id}"
                : $"Car {Id}";
            CapacityLabel = localizationService.CurrentText.EditorCarCapacityLabel;
            AreaLabel = localizationService.CurrentText.EditorCarAreaLabel;
            SpeedLabel = localizationService.CurrentText.EditorCarSpeedLabel;
            AccelerationLabel = localizationService.CurrentText.EditorCarAccelerationLabel;
            JerkLabel = localizationService.CurrentText.EditorCarJerkLabel;
            PreOpeningLabel = localizationService.CurrentText.EditorCarPreOpeningLabel;
            OpenTimeLabel = localizationService.CurrentText.EditorCarOpenTimeLabel;
            CloseTimeLabel = localizationService.CurrentText.EditorCarCloseTimeLabel;
            HomeFloorLabel = localizationService.CurrentText.EditorCarHomeFloorLabel;
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed record LanguageOption(AppLanguage Language, string DisplayName);
}
