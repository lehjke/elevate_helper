using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Numerics;
using ElevateHelperWinUI.Models;
using ElevateHelperWinUI.Services;
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
    private readonly AppLocalizationService localizationService = AppLocalizationService.Instance;
    private readonly AppUpdateService updateService = new();
    private readonly IElevateProjectEditorService projectEditorService = new ElevateProjectEditorService();
    private readonly IElevateIntegrationService integrationService = new ElevateIntegrationService();
    private readonly IElevateProcessingService processingService = new ElevateProcessingService();
    private readonly IElevateReportService reportService = new ElevateReportService();
    private readonly ElevateProjectBatchDiscoveryService projectBatchDiscoveryService = new();
    private readonly SemaphoreSlim reportExecutionLock = new(1, 1);
    private readonly object activeProcessingFoldersSync = new();
    private readonly HashSet<string> activeProcessingFolders = new(StringComparer.OrdinalIgnoreCase);
    private readonly ObservableCollection<JobProgressViewModel> jobs = [];
    private readonly ObservableCollection<FloorEditorRowViewModel> editorFloors = [];
    private readonly ObservableCollection<CarEditorRowViewModel> editorCars = [];
    private ElevateProjectEditorWindow? editorWindow;
    private ElevateProjectEditorDocument? loadedEditorDocument;
    private bool suppressBuildingTypeStatus;
    private bool updateCheckStarted;
    private int nextJobId = 1;

    public MainPage()
    {
        this.InitializeComponent();
        localizationService.LanguageChanged += OnLanguageChanged;

        UpdateLanguageSelector();
        OfficeRadioButton.IsChecked = true;

        if (App.MainWindow is not null)
        {
            App.MainWindow.Title = Text.WindowTitle;
        }

        ResetEditorStatus();
        UpdateModeButtons(BuildingType.Office);
        RefreshIntegrationStatus(showStatusMessage: true);
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
            updateInfo = await updateService.CheckForUpdateAsync();
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
                System.Globalization.CultureInfo.CurrentCulture,
                Text.UpdateAvailableMessageFormat,
                updateInfo.CurrentVersion,
                updateInfo.LatestVersion),
            PrimaryButtonText = Text.UpdateInstallButton,
            CloseButtonText = Text.UpdateLaterButton,
            DefaultButton = ContentDialogButton.Primary,
        };

        ContentDialogResult result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            SetStatus(Text.UpdateDownloadingStatus, InfoBarSeverity.Informational);
            _ = await updateService.DownloadAndStartUpdateAsync(updateInfo);
            SetStatus(Text.UpdateStartedStatus, InfoBarSeverity.Success);
            await Task.Delay(TimeSpan.FromMilliseconds(500));
            Application.Current.Exit();
        }
        catch (Exception ex)
        {
            SetStatus(BuildExceptionMessage(ex), InfoBarSeverity.Error);
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
        if (!TryGetInputs(out string path, out BuildingType buildingType))
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
        if (!TryGetProjectBatchInputs(out string projectRoot, out int parallelRuns))
        {
            return;
        }

        if (!TryEnsureIntegrationForLaunch())
        {
            return;
        }

        ProjectBatchDiscoveryResult discoveryResult;
        try
        {
            discoveryResult = projectBatchDiscoveryService.Discover(projectRoot);
        }
        catch (Exception ex)
        {
            SetStatus(BuildExceptionMessage(ex), InfoBarSeverity.Error);
            return;
        }

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

        if (batchJobs.Count == 0)
        {
            SetStatus(Text.ProjectBatchNoJobsMessage, InfoBarSeverity.Warning);
            return;
        }

        if (!await ConfirmProjectBatchJobsAsync(batchJobs, discoveryResult.Warnings))
        {
            return;
        }

        StartProjectBatchJobs(batchJobs, parallelRuns, discoveryResult.Warnings.Count);
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
                    System.Globalization.CultureInfo.CurrentCulture,
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
                    System.Globalization.CultureInfo.CurrentCulture,
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
                    System.Globalization.CultureInfo.CurrentCulture,
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
                ProcessingResult result = await reportService.PrintReportAsync(path, buildingType);
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
                ProcessingResult result = await reportService.PrintReportAsync(morningPath, buildingType);
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
                ProcessingResult result = await reportService.PrintReportAsync(lunchPath, buildingType);
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
                ProcessingResult result = await PrintReportsForJobAsync(job);
                HandleReportResult(result, GetJobReportSuccessText(job));
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
        if (!TryRegisterProcessingFolder(normalizedPath))
        {
            SetStatus(
                string.Format(
                    System.Globalization.CultureInfo.CurrentCulture,
                    Text.RunFolderBusyMessage,
                    normalizedPath),
                InfoBarSeverity.Warning);
            return;
        }

        _ = RunProcessingJobAsync(
            job,
            normalizedPath,
            job.BuildingType,
            job.IncludeLunchPeak,
            normalizedPath,
            job.AutoGenerateReport,
            job.ReportOutputRoot);
    }

    private void OnStopJobButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: JobProgressViewModel job } || !job.CanStop)
        {
            return;
        }

        job.RequestStop(localizationService);
        RefreshJobsSummary();
        SetStatus($"{job.Title}: {Text.StoppingStatus}", InfoBarSeverity.Informational);
    }

    private void OnDismissJobButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: JobProgressViewModel job } || !job.CanDismiss)
        {
            return;
        }

        _ = Jobs.Remove(job);
        RefreshJobsSummary();
    }

    private void OnExitButtonClick(object sender, RoutedEventArgs e)
    {
        Application.Current.Exit();
    }

    private void OnOpenEditorWindowClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetInputs(out string path, out BuildingType buildingType))
        {
            return;
        }

        if (editorWindow is not null)
        {
            editorWindow.Activate();
            return;
        }

        editorWindow = new ElevateProjectEditorWindow(path, buildingType);
        editorWindow.Closed += OnEditorWindowClosed;
        editorWindow.Activate();
    }

    private void OnEditorWindowClosed(object sender, WindowEventArgs args)
    {
        if (editorWindow is not null)
        {
            editorWindow.Closed -= OnEditorWindowClosed;
            editorWindow = null;
        }
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

    private void UpdateLanguageSelector()
    {
        LanguageOption selectedOption = LanguageOptions.First(option => option.Language == localizationService.CurrentLanguage);
        LanguageButtonText.Text = selectedOption.DisplayName;

        bool isEnglish = selectedOption.Language == AppLanguage.English;
        EnglishLanguageSelectionBackground.Opacity = isEnglish ? 1 : 0;
        EnglishLanguageSelectionPill.Opacity = isEnglish ? 1 : 0;
        RussianLanguageSelectionBackground.Opacity = isEnglish ? 0 : 1;
        RussianLanguageSelectionPill.Opacity = isEnglish ? 0 : 1;
    }

    private void OnBetaFeaturesCheckBoxChanged(object sender, RoutedEventArgs e)
    {
        UpdateBetaFeatureVisibility();
    }

    private void OnBuildingTypeRadioButtonChecked(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(sender, OfficeRadioButton))
        {
            ResidenceRadioButton.IsChecked = false;
            HotelRadioButton.IsChecked = false;
        }
        else if (ReferenceEquals(sender, ResidenceRadioButton))
        {
            OfficeRadioButton.IsChecked = false;
            HotelRadioButton.IsChecked = false;
        }
        else if (ReferenceEquals(sender, HotelRadioButton))
        {
            OfficeRadioButton.IsChecked = false;
            ResidenceRadioButton.IsChecked = false;
        }

        BuildingType? selectedType = GetSelectedBuildingType();
        if (!selectedType.HasValue)
        {
            return;
        }

        UpdateModeButtons(selectedType.Value);
        UpdateEditorOutputPreview();

        if (suppressBuildingTypeStatus)
        {
            return;
        }

        SetStatus(localizationService.FormatSelectedBuildingType(selectedType.Value), InfoBarSeverity.Informational);
    }

    private void OnBuildingTypeRadioButtonUnchecked(object sender, RoutedEventArgs e)
    {
        if (OfficeRadioButton.IsChecked == true
            || ResidenceRadioButton.IsChecked == true
            || HotelRadioButton.IsChecked == true)
        {
            return;
        }

        if (ReferenceEquals(sender, OfficeRadioButton))
        {
            OfficeRadioButton.IsChecked = true;
        }
        else if (ReferenceEquals(sender, ResidenceRadioButton))
        {
            ResidenceRadioButton.IsChecked = true;
        }
        else if (ReferenceEquals(sender, HotelRadioButton))
        {
            HotelRadioButton.IsChecked = true;
        }
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
            SetStatus($"{Text.ProjectBatchParallelRunsHeader}: 1+", InfoBarSeverity.Warning);
            return false;
        }

        parallelRuns = Math.Max(1, (int)Math.Round(rawParallelRuns));
        return true;
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
                SelectedIndex = 0,
            };
            comboBox.Items.Add(new ComboBoxItem { Content = Text.BuildingTypeOffice, Tag = BuildingType.Office });
            comboBox.Items.Add(new ComboBoxItem { Content = Text.BuildingTypeResidence, Tag = BuildingType.Residence });
            comboBox.Items.Add(new ComboBoxItem { Content = Text.BuildingTypeHotel, Tag = BuildingType.Hotel });

            StackPanel row = new() { Spacing = 6 };
            row.Children.Add(new TextBlock
            {
                Text = filePath,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.78,
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

        ContentDialogResult result = await dialog.ShowAsync();
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

    private async Task<bool> ConfirmProjectBatchJobsAsync(
        IReadOnlyList<ProjectBatchJob> batchJobs,
        IReadOnlyList<ProjectBatchWarning> warnings)
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
            string scenarioText = job.BuildingType == BuildingType.Office
                ? Text.ProjectBatchPreviewMorningLunch
                : Text.ProjectBatchPreviewSingleScenario;

            AddProjectBatchPreviewCell(table, localizationService.FormatBuildingType(job.BuildingType), row, column: 0);
            AddProjectBatchPreviewCell(table, job.WorkingFolder, row, column: 1, wrap: true);
            AddProjectBatchPreviewCell(table, CountProjectBatchSourceFiles(job).ToString(System.Globalization.CultureInfo.CurrentCulture), row, column: 2);
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
                    System.Globalization.CultureInfo.CurrentCulture,
                    Text.ProjectBatchPreviewWarningsFormat,
                    warnings.Count),
                FontSize = 13,
                Opacity = 0.78,
                TextWrapping = TextWrapping.Wrap,
            });

            foreach (ProjectBatchWarning warning in warnings)
            {
                contentStack.Children.Add(new TextBlock
                {
                    Text = $"{warning.Message} {warning.Path}",
                    FontSize = 12,
                    Opacity = 0.82,
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

        ContentDialogResult result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
    }

    private static void AddProjectBatchPreviewHeader(Grid table, string text, int column)
    {
        AddProjectBatchPreviewText(
            table,
            text,
            row: 0,
            column,
            fontWeight: Microsoft.UI.Text.FontWeights.SemiBold,
            opacity: 0.78);
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
            opacity: 1,
            wrap);
    }

    private static void AddProjectBatchPreviewText(
        Grid table,
        string text,
        int row,
        int column,
        Windows.UI.Text.FontWeight fontWeight,
        double opacity,
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
            Opacity = opacity,
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            MaxWidth = wrap ? 520 : double.PositiveInfinity,
        };

        Grid.SetRow(textBlock, row);
        Grid.SetColumn(textBlock, column);
        table.Children.Add(textBlock);
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
                System.Globalization.CultureInfo.CurrentCulture,
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
                System.Globalization.CultureInfo.CurrentCulture,
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

    private static string FormatEditorNumber(double value)
    {
        return value.ToString("0.###", System.Globalization.CultureInfo.GetCultureInfo("ru-RU"));
    }

    private void StartProcessingJob(string path, BuildingType buildingType, bool includeLunchPeak)
    {
        string normalizedPath = NormalizeProcessingFolder(path);
        if (!TryRegisterProcessingFolder(normalizedPath))
        {
            SetStatus(
                string.Format(
                    System.Globalization.CultureInfo.CurrentCulture,
                    Text.RunFolderBusyMessage,
                    normalizedPath),
                InfoBarSeverity.Warning);
            return;
        }

        JobProgressViewModel job;
        try
        {
            job = CreateJob(normalizedPath, buildingType, includeLunchPeak);
        }
        catch
        {
            UnregisterProcessingFolder(normalizedPath);
            throw;
        }

        _ = RunProcessingJobAsync(job, normalizedPath, buildingType, includeLunchPeak, normalizedPath);
    }

    private void StartProjectBatchJobs(IReadOnlyList<ProjectBatchJob> batchJobs, int parallelRuns, int warningCount)
    {
        int effectiveParallelRuns = parallelRuns == int.MaxValue
            ? Math.Max(1, batchJobs.Count)
            : Math.Max(1, Math.Min(parallelRuns, batchJobs.Count));
        SemaphoreSlim parallelism = new(effectiveParallelRuns, effectiveParallelRuns);

        int startedJobs = 0;
        foreach (ProjectBatchJob batchJob in batchJobs)
        {
            string normalizedPath = NormalizeProcessingFolder(batchJob.WorkingFolder);
            if (!TryRegisterProcessingFolder(normalizedPath))
            {
                SetStatus(
                    string.Format(
                        System.Globalization.CultureInfo.CurrentCulture,
                        Text.RunFolderBusyMessage,
                        normalizedPath),
                    InfoBarSeverity.Warning);
                continue;
            }

            bool includeLunchPeak = batchJob.BuildingType == BuildingType.Office;
            string title = $"{batchJob.BuildingTypeFolderName} / {batchJob.GroupName}";
            JobProgressViewModel job = CreateJob(
                normalizedPath,
                batchJob.BuildingType,
                includeLunchPeak,
                title,
                batchJob.ProjectRoot);

            startedJobs++;
            _ = RunProjectBatchJobAsync(job, batchJob, includeLunchPeak, normalizedPath, parallelism);
        }

        if (startedJobs == 0)
        {
            SetStatus(Text.ProjectBatchNoJobsMessage, InfoBarSeverity.Warning);
            return;
        }

        string startedMessage = string.Format(
            System.Globalization.CultureInfo.CurrentCulture,
            Text.ProjectBatchStartedFormat,
            startedJobs);
        if (warningCount > 0)
        {
            startedMessage += " " + string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                Text.ProjectBatchWarningsFormat,
                warningCount);
        }

        SetStatus(startedMessage, warningCount > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Informational);
    }

    private async Task RunProjectBatchJobAsync(
        JobProgressViewModel job,
        ProjectBatchJob batchJob,
        bool includeLunchPeak,
        string activeProcessingFolder,
        SemaphoreSlim parallelism)
    {
        bool acquired = false;
        try
        {
            await parallelism.WaitAsync();
            acquired = true;
            await RunProcessingJobAsync(
                job,
                batchJob.WorkingFolder,
                batchJob.BuildingType,
                includeLunchPeak,
                activeProcessingFolder,
                autoGenerateReport: true,
                reportOutputRoot: batchJob.ProjectRoot);
        }
        finally
        {
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
        string activeProcessingFolder,
        bool autoGenerateReport = false,
        string? reportOutputRoot = null)
    {
        using CancellationTokenSource stopSource = new();
        job.AttachStopSource(stopSource);
        job.MarkRunning(localizationService);
        RefreshJobsSummary();
        SetStatus(localizationService.FormatRunStarted(job.Title), InfoBarSeverity.Informational);

        try
        {
            ProcessingResult result = await InvokeProcessingAsync(
                job,
                path,
                buildingType,
                includeLunchPeak,
                stopSource.Token);
            if (job.StopRequested)
            {
                await CompleteStoppedJobAsync(job, autoGenerateReport, reportOutputRoot);
                return;
            }

            if (!result.Success)
            {
                ApplyProcessingResult(job, result);
            }
            else if (autoGenerateReport)
            {
                await GenerateReportForCompletedJobAsync(job, reportOutputRoot);
            }
            else
            {
                ApplyProcessingResult(job, result);
            }
        }
        catch (OperationCanceledException) when (job.StopRequested)
        {
            await CompleteStoppedJobAsync(job, autoGenerateReport, reportOutputRoot);
        }
        catch (Exception ex)
        {
            string message = BuildExceptionMessage(ex);
            job.MarkFailed(message);
            SetStatus(message, InfoBarSeverity.Error);
        }
        finally
        {
            job.DetachStopSource();
            UnregisterProcessingFolder(activeProcessingFolder);
            RefreshJobsSummary();
        }
    }

    private bool TryRegisterProcessingFolder(string path)
    {
        lock (activeProcessingFoldersSync)
        {
            return activeProcessingFolders.Add(path);
        }
    }

    private void UnregisterProcessingFolder(string path)
    {
        lock (activeProcessingFoldersSync)
        {
            activeProcessingFolders.Remove(path);
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

    private async Task GenerateReportForCompletedJobAsync(JobProgressViewModel job, string? outputFolder)
    {
        await GenerateReportForCompletedJobAsync(job, outputFolder, preserveStoppedStatus: false);
    }

    private async Task GenerateReportForCompletedJobAsync(
        JobProgressViewModel job,
        string? outputFolder,
        bool preserveStoppedStatus)
    {
        SetStatus(Text.ProjectBatchGeneratingReports, InfoBarSeverity.Informational);
        ProcessingResult reportResult = await PrintReportsForJobWithLockAsync(job, outputFolder);
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
            job.MarkFailed(message);
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
            await GenerateReportForCompletedJobAsync(job, reportOutputRoot, preserveStoppedStatus: true);
        }
    }

    private async Task<ProcessingResult> PrintReportsForJobWithLockAsync(JobProgressViewModel job, string? outputFolder)
    {
        await reportExecutionLock.WaitAsync();
        SetReportButtonsEnabled(isEnabled: false);
        try
        {
            return await PrintReportsForJobAsync(job, outputFolder);
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
        job.MarkFailed(message);
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

    private void HandleProgressUpdate(JobProgressViewModel job, ElevateProgressInfo update)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            _ = DispatcherQueue.TryEnqueue(() => HandleProgressUpdate(job, update));
            return;
        }

        job.UpdateProgress(update.Scenario, update.Completed, update.Total, localizationService);
    }

    private void SetStatus(string message, InfoBarSeverity severity)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            _ = DispatcherQueue.TryEnqueue(() => SetStatus(message, severity));
            return;
        }

        StatusInfoBar.Severity = severity;
        StatusInfoBar.Message = message;
        StatusInfoBar.IsOpen = true;
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
        BusyTextBlock.Text = localizationService.GetQueueSummary(runningJobs);

        bool hasJobs = Jobs.Count > 0;
        JobsItemsControl.Visibility = hasJobs ? Visibility.Visible : Visibility.Collapsed;
        EmptyQueueBorder.Visibility = hasJobs ? Visibility.Collapsed : Visibility.Visible;
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

    private async Task<ProcessingResult> PrintReportsForJobAsync(JobProgressViewModel job)
    {
        return await PrintReportsForJobAsync(job, outputFolder: null);
    }

    private async Task<ProcessingResult> PrintReportsForJobAsync(JobProgressViewModel job, string? outputFolder)
    {
        if (job.BuildingType != BuildingType.Office)
        {
            return await reportService.PrintReportAsync(job.JobPath, job.BuildingType, outputFolder);
        }

        if (job.WasStoppedEarly)
        {
            return await PrintAvailableOfficeReportsForStoppedJobAsync(job, outputFolder);
        }

        string morningPath = Path.Combine(job.JobPath, "morning");
        ProcessingResult morningResult = await reportService.PrintReportAsync(morningPath, job.BuildingType, outputFolder);
        if (!morningResult.Success || !job.IncludeLunchPeak)
        {
            return morningResult;
        }

        string lunchPath = Path.Combine(job.JobPath, "lunch");
        return await reportService.PrintReportAsync(lunchPath, job.BuildingType, outputFolder);
    }

    private async Task<ProcessingResult> PrintAvailableOfficeReportsForStoppedJobAsync(
        JobProgressViewModel job,
        string? outputFolder)
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
            result = await reportService.PrintReportAsync(scenarioPath, job.BuildingType, outputFolder);
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

    private void UpdateBetaFeatureVisibility()
    {
        bool isVisible = BetaFeaturesCheckBox.IsOn;
        EditorWindowCard.Visibility = isVisible
            ? Visibility.Visible
            : Visibility.Collapsed;
        ProjectBatchCard.Visibility = isVisible
            ? Visibility.Visible
            : Visibility.Collapsed;
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
                    System.Globalization.CultureInfo.CurrentCulture,
                    Text.IntegrationVersionFormat,
                    info.ProductVersion);
            SetStatus(
                string.Format(
                    System.Globalization.CultureInfo.CurrentCulture,
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
        string? reportOutputRoot = null)
    {
        JobProgressViewModel job = new(
            nextJobId++,
            path,
            buildingType,
            includeLunchPeak,
            localizationService,
            title,
            reportOutputRoot);
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
        if (!message.Contains(Text.OperationFailedMessage, StringComparison.CurrentCultureIgnoreCase))
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

        foreach (JobProgressViewModel job in Jobs)
        {
            job.ApplyLocalization(localizationService);
        }

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
        RefreshJobsSummary();
    }

    public sealed class JobProgressViewModel : INotifyPropertyChanged
    {
        private readonly int jobId;
        private readonly string path;
        private readonly BuildingType buildingType;
        private readonly bool includeLunchPeak;
        private readonly string? customTitle;
        private readonly string? reportOutputRoot;
        private readonly ScenarioProgressViewModel? primaryScenario;
        private readonly ScenarioProgressViewModel? morningScenario;
        private readonly ScenarioProgressViewModel? lunchScenario;
        private JobScenarioKind activeScenarioKind;
        private JobStateKind stateKind;
        private string? failureMessage;
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
        private CancellationTokenSource? stopSource;

        public JobProgressViewModel(
            int jobId,
            string path,
            BuildingType buildingType,
            bool includeLunchPeak,
            AppLocalizationService localizationService,
            string? customTitle = null,
            string? reportOutputRoot = null)
        {
            this.jobId = jobId;
            this.path = path;
            this.buildingType = buildingType;
            this.includeLunchPeak = includeLunchPeak;
            this.customTitle = customTitle;
            this.reportOutputRoot = reportOutputRoot;

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
                morningScenario = new ScenarioProgressViewModel(JobScenarioKind.Morning);
                lunchScenario = new ScenarioProgressViewModel(JobScenarioKind.Lunch);
                Scenarios = new ObservableCollection<ScenarioProgressViewModel>
                {
                    morningScenario,
                    lunchScenario,
                };
            }
            else if (isOffice)
            {
                primaryScenario = new ScenarioProgressViewModel(JobScenarioKind.Morning);
                Scenarios = new ObservableCollection<ScenarioProgressViewModel>
                {
                    primaryScenario!,
                };
            }
            else
            {
                primaryScenario = new ScenarioProgressViewModel(JobScenarioKind.Progress);
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
            }
        }

        public string JobPath => path;

        public BuildingType BuildingType => buildingType;

        public bool IncludeLunchPeak => includeLunchPeak;

        public bool AutoGenerateReport => !string.IsNullOrWhiteSpace(reportOutputRoot);

        public string? ReportOutputRoot => reportOutputRoot;

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

        public bool CanPrintReport => (stateKind is JobStateKind.Completed or JobStateKind.Stopped) &&
                                      reportActionEnabled;

        public bool CanRetry => isFinished && !isRunning && !string.IsNullOrWhiteSpace(failureMessage);

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

        public bool WasStoppedEarly => stateKind == JobStateKind.Stopped || stopRequested;

        public bool CanStop => stateKind == JobStateKind.Running &&
                               isRunning &&
                               !isFinished &&
                               !stopRequested &&
                               stopSource is not null;

        public Visibility PrintReportVisibility => stateKind is JobStateKind.Completed or JobStateKind.Stopped
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
            stopSource = cancellationTokenSource;
            NotifyStopActionStateChanged();
        }

        public void DetachStopSource()
        {
            stopSource = null;
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
            stopSource?.Cancel();
        }

        public void MarkRunning(AppLocalizationService localizationService)
        {
            isFinished = false;
            failureMessage = null;
            stopRequested = false;
            stateKind = JobStateKind.Running;
            IsRunning = true;
            StatusText = localizationService.GetJobStateLabel(JobStateKind.Running);
            NotifyReportActionStateChanged();
        }

        public void MarkCompleted(AppLocalizationService localizationService)
        {
            isFinished = true;
            failureMessage = null;
            stopRequested = false;
            stateKind = JobStateKind.Completed;
            IsRunning = false;
            StatusText = localizationService.GetJobStateLabel(JobStateKind.Completed);
            NotifyReportActionStateChanged();
        }

        public void MarkStopped(AppLocalizationService localizationService)
        {
            isFinished = true;
            failureMessage = null;
            stopRequested = false;
            stateKind = JobStateKind.Stopped;
            IsRunning = false;
            StatusText = localizationService.GetJobStateLabel(JobStateKind.Stopped);
            NotifyReportActionStateChanged();
        }

        public void MarkFailed(string message)
        {
            isFinished = true;
            failureMessage = message;
            stopRequested = false;
            IsRunning = false;
            StatusText = message;
            NotifyReportActionStateChanged();
        }

        public void UpdateProgress(string? scenario, int completed, int total, AppLocalizationService localizationService)
        {
            if (isFinished)
            {
                return;
            }

            ScenarioProgressViewModel target = ResolveScenario(scenario);
            activeScenarioKind = target.ScenarioKind;
            target.Update(completed, total);
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
            failureMessage = null;

            if (completed == 0 && total == 0)
            {
                StatusText = localizationService.GetJobStateLabel(JobStateKind.Running);
            }
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

            if (!string.IsNullOrWhiteSpace(failureMessage))
            {
                StatusText = failureMessage;
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
            OnPropertyChanged(nameof(QueueSortGroup));
            OnPropertyChanged(nameof(WasStoppedEarly));
            NotifyStopActionStateChanged();
        }

        private void NotifyStopActionStateChanged()
        {
            OnPropertyChanged(nameof(StopRequested));
            OnPropertyChanged(nameof(WasStoppedEarly));
            OnPropertyChanged(nameof(CanStop));
            OnPropertyChanged(nameof(StopButtonVisibility));
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed class ScenarioProgressViewModel : INotifyPropertyChanged
    {
        private readonly JobScenarioKind scenarioKind;
        private string label;
        private int completed;
        private int total;
        private double value;
        private double maximum;
        private bool isIndeterminate = true;
        private string progressText = "0/0";

        public ScenarioProgressViewModel(JobScenarioKind scenarioKind)
        {
            this.scenarioKind = scenarioKind;
            label = string.Empty;
            maximum = 1;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public JobScenarioKind ScenarioKind => scenarioKind;

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

        public void Update(int completed, int total)
        {
            Completed = Math.Max(0, completed);
            Total = Math.Max(0, total);
            Maximum = Total > 0 ? Total : 1;
            Value = Total > 0 ? Math.Min(Completed, Total) : 0;
            IsIndeterminate = Total <= 0;
            ProgressText = $"{Completed}/{Total}";
        }

        public void ApplyLocalization(AppLocalizationService localizationService)
        {
            Label = localizationService.GetScenarioLabel(scenarioKind);
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
            floorLevelText = FormatEditorNumber(floor.FloorLevel);
            populationText = FormatEditorNumber(floor.Population);
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
