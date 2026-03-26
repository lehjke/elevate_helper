using System.Collections.ObjectModel;
using System.ComponentModel;
using ElevateHelperWinUI.Models;
using ElevateHelperWinUI.Services;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace ElevateHelperWinUI.Views;

public sealed partial class MainPage : Page
{
    private readonly AppLocalizationService localizationService = AppLocalizationService.Instance;
    private readonly IElevateIntegrationService integrationService = new ElevateIntegrationService();
    private readonly IElevateProcessingService processingService = new ElevateProcessingService();
    private readonly IElevateReportService reportService = new ElevateReportService();
    private readonly SemaphoreSlim reportExecutionLock = new(1, 1);
    private readonly ObservableCollection<JobProgressViewModel> jobs = [];
    private int nextJobId = 1;

    public MainPage()
    {
        this.InitializeComponent();
        localizationService.LanguageChanged += OnLanguageChanged;

        LanguageComboBox.SelectedItem = LanguageOptions.First(option => option.Language == localizationService.CurrentLanguage);
        OfficeRadioButton.IsChecked = true;

        if (App.MainWindow is not null)
        {
            App.MainWindow.Title = Text.WindowTitle;
        }

        UpdateModeButtons(BuildingType.Office);
        RefreshIntegrationStatus(showStatusMessage: true);
        RefreshJobsSummary();
    }

    public ObservableCollection<JobProgressViewModel> Jobs => jobs;

    public IReadOnlyList<LanguageOption> LanguageOptions { get; } =
    [
        new(AppLanguage.English, "English"),
        new(AppLanguage.Russian, "Русский"),
    ];

    public AppLocalizationService.AppTextCatalog Text => localizationService.CurrentText;

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

    private async void OnBrowseFolderButtonClick(object sender, RoutedEventArgs e)
    {
        if (App.MainWindow is null)
        {
            return;
        }

        FolderPicker picker = new();
        picker.FileTypeFilter.Add("*");

        IntPtr windowHandle = WindowNative.GetWindowHandle(App.MainWindow);
        InitializeWithWindow.Initialize(picker, windowHandle);

        Windows.Storage.StorageFolder? folder = await picker.PickSingleFolderAsync();
        if (folder is null)
        {
            return;
        }

        PathTextBox.Text = folder.Path;
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

    private void OnExitButtonClick(object sender, RoutedEventArgs e)
    {
        Application.Current.Exit();
    }

    private void OnLanguageComboBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LanguageComboBox.SelectedItem is not LanguageOption option)
        {
            return;
        }

        localizationService.SetLanguage(option.Language);
    }

    private void OnBuildingTypeRadioButtonChecked(object sender, RoutedEventArgs e)
    {
        BuildingType? selectedType = GetSelectedBuildingType();
        if (!selectedType.HasValue)
        {
            return;
        }

        UpdateModeButtons(selectedType.Value);
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

    private void StartProcessingJob(string path, BuildingType buildingType, bool includeLunchPeak)
    {
        JobProgressViewModel job = CreateJob(path, buildingType, includeLunchPeak);
        _ = RunProcessingJobAsync(job, path, buildingType, includeLunchPeak);
    }

    private async Task RunProcessingJobAsync(
        JobProgressViewModel job,
        string path,
        BuildingType buildingType,
        bool includeLunchPeak)
    {
        job.MarkRunning(localizationService);
        RefreshJobsSummary();
        SetStatus(localizationService.FormatRunStarted(job.Title), InfoBarSeverity.Informational);

        try
        {
            ProcessingResult result = await InvokeProcessingAsync(job, path, buildingType, includeLunchPeak);
            ApplyProcessingResult(job, result);
        }
        catch (Exception ex)
        {
            string message = BuildExceptionMessage(ex);
            job.MarkFailed(message);
            SetStatus(message, InfoBarSeverity.Error);
            ScheduleFinishedJobRemoval(job);
        }
        finally
        {
            RefreshJobsSummary();
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
            ScheduleFinishedJobRemoval(job);
            return;
        }

        string message = FormatResultMessage(result);
        job.MarkFailed(message);
        SetStatus(message, InfoBarSeverity.Error);
        ScheduleFinishedJobRemoval(job);
    }

    private async Task<ProcessingResult> InvokeProcessingAsync(
        JobProgressViewModel job,
        string path,
        BuildingType buildingType,
        bool includeLunchPeak)
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
            CancellationToken.None);
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

        int runningJobs = Jobs.Count(job => job.IsRunning);
        BusyRing.IsActive = runningJobs > 0;
        BusyTextBlock.Text = localizationService.GetQueueSummary(runningJobs);

        bool hasJobs = Jobs.Count > 0;
        JobsItemsControl.Visibility = hasJobs ? Visibility.Visible : Visibility.Collapsed;
        EmptyQueueBorder.Visibility = hasJobs ? Visibility.Collapsed : Visibility.Visible;
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
    }

    private void ScheduleFinishedJobRemoval(JobProgressViewModel job)
    {
        _ = RemoveFinishedJobAsync(job);
    }

    private async Task RemoveFinishedJobAsync(JobProgressViewModel job)
    {
        await Task.Delay(TimeSpan.FromSeconds(3));

        if (!DispatcherQueue.HasThreadAccess)
        {
            _ = DispatcherQueue.TryEnqueue(() => RemoveFinishedJob(job));
            return;
        }

        RemoveFinishedJob(job);
    }

    private void RemoveFinishedJob(JobProgressViewModel job)
    {
        if (!job.IsFinished || job.IsRunning)
        {
            return;
        }

        if (Jobs.Remove(job))
        {
            RefreshJobsSummary();
        }
    }

    private void UpdateModeButtons(BuildingType buildingType)
    {
        bool isOffice = buildingType == BuildingType.Office;

        RunMorningOnlyButton.Visibility = isOffice ? Visibility.Visible : Visibility.Collapsed;
        MorningReportButton.Visibility = isOffice ? Visibility.Visible : Visibility.Collapsed;
        LunchReportButton.Visibility = isOffice ? Visibility.Visible : Visibility.Collapsed;
        ReportButton.Visibility = isOffice ? Visibility.Collapsed : Visibility.Visible;
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

    private JobProgressViewModel CreateJob(string path, BuildingType buildingType, bool includeLunchPeak)
    {
        JobProgressViewModel job = new(nextJobId++, path, buildingType, includeLunchPeak, localizationService);
        Jobs.Insert(0, job);
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

        LanguageComboBox.SelectedItem = LanguageOptions.First(option => option.Language == localizationService.CurrentLanguage);

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
        private readonly ScenarioProgressViewModel? primaryScenario;
        private readonly ScenarioProgressViewModel? morningScenario;
        private readonly ScenarioProgressViewModel? lunchScenario;
        private JobScenarioKind activeScenarioKind;
        private JobStateKind stateKind;
        private string? failureMessage;
        private string title;
        private string details;
        private string statusText;
        private bool isRunning;
        private bool isFinished;

        public JobProgressViewModel(
            int jobId,
            string path,
            BuildingType buildingType,
            bool includeLunchPeak,
            AppLocalizationService localizationService)
        {
            this.jobId = jobId;
            this.path = path;
            this.buildingType = buildingType;
            this.includeLunchPeak = includeLunchPeak;

            title = string.Empty;
            details = string.Empty;
            statusText = string.Empty;
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

        public void MarkRunning(AppLocalizationService localizationService)
        {
            isFinished = false;
            failureMessage = null;
            stateKind = JobStateKind.Running;
            IsRunning = true;
            StatusText = localizationService.GetJobStateLabel(JobStateKind.Running);
        }

        public void MarkCompleted(AppLocalizationService localizationService)
        {
            isFinished = true;
            failureMessage = null;
            stateKind = JobStateKind.Completed;
            IsRunning = false;
            StatusText = localizationService.GetJobStateLabel(JobStateKind.Completed);
        }

        public void MarkFailed(string message)
        {
            isFinished = true;
            failureMessage = message;
            IsRunning = false;
            StatusText = message;
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
            Title = localizationService.FormatJobTitle(jobId, buildingType);
            Details = localizationService.FormatJobDetails(path, buildingType, includeLunchPeak);

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

    public sealed record LanguageOption(AppLanguage Language, string DisplayName);
}

