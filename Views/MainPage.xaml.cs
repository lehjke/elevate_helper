using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using ElevateHelperWinUI.Models;
using ElevateHelperWinUI.Services;

namespace ElevateHelperWinUI.Views;

public sealed partial class MainPage : Page
{
    private readonly IElevateIntegrationService integrationService = new ElevateIntegrationService();
    private readonly IElevateProcessingService processingService = new ElevateProcessingService();
    private readonly IElevateReportService reportService = new ElevateReportService();
    private readonly ObservableCollection<JobProgressViewModel> jobs = [];
    private int nextJobId = 1;

    public ObservableCollection<JobProgressViewModel> Jobs => jobs;

    public MainPage()
    {
        this.InitializeComponent();
        OfficeRadioButton.IsChecked = true;
        UpdateModeButtons(BuildingType.Office);
        RefreshIntegrationStatus(showStatusMessage: true);
        RefreshJobsSummary();
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
            SetStatus("Run Morning Only is available only for Office.", InfoBarSeverity.Warning);
            return;
        }

        StartProcessingJob(path, buildingType, includeLunchPeak: false);
    }

    private async void OnReportButtonClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetInputs(out string path, out BuildingType buildingType))
        {
            return;
        }

        await ExecuteReportActionAsync(
            "Generating report...",
            async () =>
            {
                ProcessingResult result = await reportService.PrintReportAsync(path, buildingType);
                HandleReportResult(result, "Report generated successfully.");
            });
    }

    private async void OnMorningReportButtonClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetInputs(out string path, out BuildingType buildingType))
        {
            return;
        }

        await ExecuteReportActionAsync(
            "Generating morning report...",
            async () =>
            {
                string morningPath = Path.Combine(path, "morning");
                ProcessingResult result = await reportService.PrintReportAsync(morningPath, buildingType);
                HandleReportResult(result, "Morning report generated successfully.");
            });
    }

    private async void OnLunchReportButtonClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetInputs(out string path, out BuildingType buildingType))
        {
            return;
        }

        await ExecuteReportActionAsync(
            "Generating lunch report...",
            async () =>
            {
                string lunchPath = Path.Combine(path, "lunch");
                ProcessingResult result = await reportService.PrintReportAsync(lunchPath, buildingType);
                HandleReportResult(result, "Lunch report generated successfully.");
            });
    }

    private void OnExitButtonClick(object sender, RoutedEventArgs e)
    {
        Application.Current.Exit();
    }

    private void OnBuildingTypeRadioButtonChecked(object sender, RoutedEventArgs e)
    {
        BuildingType? selectedType = GetSelectedBuildingType();
        if (!selectedType.HasValue)
        {
            return;
        }

        UpdateModeButtons(selectedType.Value);
        SetStatus($"Selected building type: {selectedType.Value}.", InfoBarSeverity.Informational);
    }

    private bool TryGetInputs(out string path, out BuildingType buildingType)
    {
        path = PathTextBox.Text?.Trim() ?? string.Empty;
        buildingType = BuildingType.Office;

        if (string.IsNullOrWhiteSpace(path))
        {
            SetStatus("Enter the path to the Elevate folder.", InfoBarSeverity.Warning);
            return false;
        }

        if (!Directory.Exists(path))
        {
            SetStatus("The specified folder does not exist.", InfoBarSeverity.Error);
            return false;
        }

        BuildingType? selectedType = GetSelectedBuildingType();
        if (!selectedType.HasValue)
        {
            SetStatus("Select a building type.", InfoBarSeverity.Warning);
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
        job.MarkRunning();
        RefreshJobsSummary();
        SetStatus($"{job.Title} started.", InfoBarSeverity.Informational);

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
        try
        {
            SetStatus(busyText, InfoBarSeverity.Informational);
            await action();
        }
        catch (Exception ex)
        {
            SetStatus(BuildExceptionMessage(ex), InfoBarSeverity.Error);
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
            job.MarkCompleted();
            SetStatus($"{job.Title} completed successfully.", InfoBarSeverity.Success);
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
        MethodInfo? method = FindRunMethod();
        if (method is null)
        {
            return await processingService.RunAsync(path, buildingType, includeLunchPeak);
        }

        object?[] arguments = BuildArguments(method, job, path, buildingType, includeLunchPeak);
        object? invocationResult = method.Invoke(processingService, arguments);
        return await UnwrapProcessingResultAsync(invocationResult);
    }

    private MethodInfo? FindRunMethod()
    {
        return processingService
            .GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => string.Equals(method.Name, nameof(IElevateProcessingService.RunAsync), StringComparison.Ordinal))
            .Where(method =>
            {
                ParameterInfo[] parameters = method.GetParameters();
                return parameters.Length > 0 &&
                       parameters[0].ParameterType == typeof(string) &&
                       parameters.Any(parameter => parameter.ParameterType == typeof(BuildingType)) &&
                       parameters.Any(parameter => parameter.ParameterType == typeof(bool));
            })
            .OrderByDescending(method => method.GetParameters().Any(parameter => IsProgressParameter(parameter.ParameterType)))
            .ThenByDescending(method => method.GetParameters().Length)
            .FirstOrDefault();
    }

    private object?[] BuildArguments(
        MethodInfo method,
        JobProgressViewModel job,
        string path,
        BuildingType buildingType,
        bool includeLunchPeak)
    {
        ParameterInfo[] parameters = method.GetParameters();
        object?[] arguments = new object?[parameters.Length];

        for (int index = 0; index < parameters.Length; index++)
        {
            ParameterInfo parameter = parameters[index];
            if (parameter.ParameterType == typeof(string))
            {
                arguments[index] = path;
                continue;
            }

            if (parameter.ParameterType == typeof(BuildingType))
            {
                arguments[index] = buildingType;
                continue;
            }

            if (parameter.ParameterType == typeof(bool))
            {
                arguments[index] = includeLunchPeak;
                continue;
            }

            if (parameter.ParameterType == typeof(CancellationToken))
            {
                arguments[index] = CancellationToken.None;
                continue;
            }

            if (IsProgressParameter(parameter.ParameterType))
            {
                arguments[index] = CreateProgressReporter(parameter.ParameterType, job);
                continue;
            }

            arguments[index] = parameter.HasDefaultValue ? parameter.DefaultValue : null;
        }

        return arguments;
    }

    private object? CreateProgressReporter(Type progressParameterType, JobProgressViewModel job)
    {
        if (!IsProgressParameter(progressParameterType))
        {
            return null;
        }

        Type updateType = progressParameterType.GetGenericArguments()[0];
        MethodInfo callbackFactory = typeof(MainPage).GetMethod(
            nameof(CreateProgressCallback),
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        MethodInfo genericFactory = callbackFactory.MakeGenericMethod(updateType);
        Delegate callback = (Delegate)genericFactory.Invoke(this, [job])!;
        Type progressType = typeof(Progress<>).MakeGenericType(updateType);
        return Activator.CreateInstance(progressType, callback);
    }

    private Action<T> CreateProgressCallback<T>(JobProgressViewModel job)
    {
        return update => HandleProgressUpdate(job, update);
    }

    private void HandleProgressUpdate(JobProgressViewModel job, object? update)
    {
        if (update is null)
        {
            return;
        }

        if (!DispatcherQueue.HasThreadAccess)
        {
            _ = DispatcherQueue.TryEnqueue(() => HandleProgressUpdate(job, update));
            return;
        }

        int completed = ReadIntProperty(update, "Completed");
        int total = ReadIntProperty(update, "Total");
        string? scenario = ReadStringProperty(update, "Scenario");

        job.UpdateProgress(scenario, completed, total);
        RefreshJobsSummary();
    }

    private static int ReadIntProperty(object value, string propertyName)
    {
        PropertyInfo? property = value.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        object? raw = property?.GetValue(value);
        return raw switch
        {
            null => 0,
            int number => number,
            long number => (int)number,
            short number => number,
            byte number => number,
            uint number => (int)number,
            ulong number => (int)number,
            _ when int.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out int parsed) => parsed,
            _ => 0,
        };
    }

    private static string? ReadStringProperty(object value, string propertyName)
    {
        PropertyInfo? property = value.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        object? raw = property?.GetValue(value);
        return Convert.ToString(raw, CultureInfo.InvariantCulture);
    }

    private static bool IsProgressParameter(Type type)
    {
        return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IProgress<>);
    }

    private static async Task<ProcessingResult> UnwrapProcessingResultAsync(object? invocationResult)
    {
        if (invocationResult is ProcessingResult processingResult)
        {
            return processingResult;
        }

        if (invocationResult is Task<ProcessingResult> typedTask)
        {
            return await typedTask;
        }

        if (invocationResult is Task task)
        {
            await task;

            Type taskType = task.GetType();
            if (taskType.IsGenericType)
            {
                object? result = taskType.GetProperty("Result", BindingFlags.Instance | BindingFlags.Public)?.GetValue(task);
                if (result is ProcessingResult typedResult)
                {
                    return typedResult;
                }
            }

            return ProcessingResult.Ok();
        }

        throw new InvalidOperationException("RunAsync returned an unsupported result.");
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
            _ = DispatcherQueue.TryEnqueue(() => RefreshJobsSummary());
            return;
        }

        int runningJobs = Jobs.Count(job => job.IsRunning);
        BusyRing.IsActive = runningJobs > 0;
        BusyTextBlock.Text = runningJobs > 0
            ? $"{runningJobs} active job(s)"
            : "Ready";
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

        SetStatus(
            "Peters Research Elevate is not detected. Install Elevate or set ELEVATE_EXE_PATH.",
            InfoBarSeverity.Error);
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
                : $" Version: {info.ProductVersion}.";
            SetStatus(
                $"Elevate found.{versionPart} Path: {info.ExecutablePath}",
                InfoBarSeverity.Success);
            return;
        }

        SetStatus(
            "Elevate was not found. Check installation or define ELEVATE_EXE_PATH.",
            InfoBarSeverity.Warning);
    }

    private JobProgressViewModel CreateJob(string path, BuildingType buildingType, bool includeLunchPeak)
    {
        int jobId = nextJobId++;
        string title = $"Job {jobId} - {buildingType}";
        string mode = includeLunchPeak
            ? "morning + lunch"
            : buildingType == BuildingType.Office
                ? "morning only"
                : "single scenario";
        string details = $"{path} - {mode}";

        JobProgressViewModel job = new(title, details, buildingType, includeLunchPeak, RefreshJobsSummary);
        Jobs.Insert(0, job);
        RefreshJobsSummary();
        return job;
    }

    private static string FormatResultMessage(ProcessingResult result)
    {
        string message = string.IsNullOrWhiteSpace(result.Message) ? "Operation failed." : result.Message;
        if (result.Exception is null)
        {
            return message;
        }

        return $"{message} | {result.Exception.Message}";
    }

    private static string BuildExceptionMessage(Exception exception)
    {
        string message = exception.Message;
        if (exception.InnerException is not null)
        {
            message = $"{message} | {exception.InnerException.Message}";
        }

        return message;
    }

    public sealed class JobProgressViewModel : INotifyPropertyChanged
    {
        private readonly Action refreshSummary;
        private readonly ScenarioProgressViewModel? primaryScenario;
        private readonly ScenarioProgressViewModel? morningScenario;
        private readonly ScenarioProgressViewModel? lunchScenario;
        private string title;
        private string details;
        private string statusText;
        private bool isRunning;
        private bool isFinished;

        public JobProgressViewModel(
            string title,
            string details,
            BuildingType buildingType,
            bool includeLunchPeak,
            Action refreshSummary)
        {
            this.refreshSummary = refreshSummary;
            this.title = title;
            this.details = details;
            statusText = "Queued";

            bool isOffice = buildingType == BuildingType.Office;
            if (isOffice && includeLunchPeak)
            {
                morningScenario = new ScenarioProgressViewModel("Morning", refreshSummary);
                lunchScenario = new ScenarioProgressViewModel("Lunch", refreshSummary);
                Scenarios = new ObservableCollection<ScenarioProgressViewModel>
                {
                    morningScenario,
                    lunchScenario,
                };
            }
            else if (isOffice)
            {
                primaryScenario = new ScenarioProgressViewModel("Morning", refreshSummary);
                Scenarios = new ObservableCollection<ScenarioProgressViewModel>
                {
                    primaryScenario!,
                };
            }
            else
            {
                primaryScenario = new ScenarioProgressViewModel("Progress", refreshSummary);
                Scenarios = new ObservableCollection<ScenarioProgressViewModel>
                {
                    primaryScenario!,
                };
            }
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

        public void MarkRunning()
        {
            isFinished = false;
            IsRunning = true;
            StatusText = "Running";
            refreshSummary();
        }

        public void MarkCompleted()
        {
            isFinished = true;
            IsRunning = false;
            StatusText = "Completed";
            refreshSummary();
        }

        public void MarkFailed(string message)
        {
            isFinished = true;
            IsRunning = false;
            StatusText = message;
            refreshSummary();
        }

        public void UpdateProgress(string? scenario, int completed, int total)
        {
            if (isFinished)
            {
                return;
            }

            ScenarioProgressViewModel target = ResolveScenario(scenario);
            target.Update(completed, total);
            StatusText = string.IsNullOrWhiteSpace(target.Label)
                ? $"{completed}/{total}"
                : $"{target.Label}: {completed}/{total}";
            IsRunning = true;
            refreshSummary();
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
            refreshSummary();
        }
    }

    public sealed class ScenarioProgressViewModel : INotifyPropertyChanged
    {
        private readonly Action refreshSummary;
        private string label;
        private int completed;
        private int total;
        private double value;
        private double maximum;
        private bool isIndeterminate = true;
        private string progressText = "0/0";

        public ScenarioProgressViewModel(string label, Action refreshSummary)
        {
            this.refreshSummary = refreshSummary;
            this.label = label;
            maximum = 1;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

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
            refreshSummary();
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            refreshSummary();
        }
    }
}
