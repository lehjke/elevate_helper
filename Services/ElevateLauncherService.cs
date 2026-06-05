using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using ElevateHelperWinUI.Models;

namespace ElevateHelperWinUI.Services;

public sealed class ElevateLauncherService : IElevateLauncherService
{
    private const int RunBatchCommandId = 32819;
    private const int DialogOkControlId = 1;
    private const int DialogNoControlId = 7;
    private const int DialogFolderEditControlId = 14148;
    private const uint WmCommand = 0x0111;
    private const uint WmClose = 0x0010;
    private const uint WmSetText = 0x000C;
    private const uint BmClick = 0x00F5;
    private static readonly SemaphoreSlim BatchSubmissionLock = new(1, 1);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan WindowPollDelay = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan ProgressPollDelay = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan DialogCloseTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan BatchStartTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CompletedOutputsSettleDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan DesignWindowAppearTimeout = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan ProcessStopGracePeriod = TimeSpan.FromSeconds(8);

    private readonly IElevateIntegrationService integrationService;

    public ElevateLauncherService()
        : this(new ElevateIntegrationService())
    {
    }

    public ElevateLauncherService(IElevateIntegrationService integrationService)
    {
        this.integrationService = integrationService;
    }

    public Task LaunchResidenceAsync(string path, CancellationToken cancellationToken = default)
    {
        return LaunchResidenceAsync(path, progress: null, cancellationToken);
    }

    public Task LaunchResidenceAsync(
        string path,
        IProgress<ElevateProgressInfo>? progress,
        CancellationToken cancellationToken = default)
    {
        return LaunchAndSubmitPathAsync(path, progress, cancellationToken);
    }

    public Task LaunchOfficeAsync(
        string path,
        bool includeLunchPeak,
        CancellationToken cancellationToken = default)
    {
        return LaunchOfficeAsync(path, includeLunchPeak, morningProgress: null, lunchProgress: null, cancellationToken);
    }

    public async Task LaunchOfficeAsync(
        string path,
        bool includeLunchPeak,
        IProgress<ElevateProgressInfo>? morningProgress,
        IProgress<ElevateProgressInfo>? lunchProgress,
        CancellationToken cancellationToken = default)
    {
        string morningPath = Path.Combine(path, "morning");
        Task morningTask = LaunchAndSubmitPathIfNeededAsync(morningPath, morningProgress, cancellationToken);

        if (!includeLunchPeak)
        {
            await morningTask;
            return;
        }

        string lunchPath = Path.Combine(path, "lunch");
        Task lunchTask = LaunchAndSubmitPathIfNeededAsync(lunchPath, lunchProgress, cancellationToken);

        await Task.WhenAll(morningTask, lunchTask);
    }

    private async Task LaunchAndSubmitPathIfNeededAsync(
        string path,
        IProgress<ElevateProgressInfo>? progress,
        CancellationToken cancellationToken)
    {
        if (TryBuildProgressContext(path, out ProgressContext progressContext) &&
            HasCompletedScenarioOutputs(path, progressContext))
        {
            ReportProgress(progress, progressContext, progressContext.Total, "csv", null, isFinal: true);
            return;
        }

        await LaunchAndSubmitPathAsync(path, progress, cancellationToken);
    }

    private async Task LaunchAndSubmitPathAsync(
        string path,
        IProgress<ElevateProgressInfo>? progress,
        CancellationToken cancellationToken)
    {
        ProgressContext progressContext = BuildProgressContext(path);
        ReportProgress(progress, progressContext, 0, "window", null, isFinal: false);

        ElevateIntegrationInfo integrationInfo = integrationService.GetIntegrationInfo();
        if (!integrationInfo.IsDetected || string.IsNullOrWhiteSpace(integrationInfo.ExecutablePath))
        {
            throw new FileNotFoundException(
                BuildIntegrationNotFoundMessage(integrationInfo),
                integrationInfo.ExecutablePath);
        }

        Process? process = null;
        ResultFileBaseline? resultBaseline = null;
        bool submissionLockAcquired = false;
        CancellationTokenRegistration stopRegistration = default;
        ProcessStopRequest? stopRequest = null;

        try
        {
            try
            {
                await BatchSubmissionLock.WaitAsync(cancellationToken);
                submissionLockAcquired = true;

                process = Process.Start(new ProcessStartInfo
                {
                    FileName = integrationInfo.ExecutablePath,
                    UseShellExecute = true,
                });

                if (process is null)
                {
                    throw new InvalidOperationException("Unable to start Elevate.exe.");
                }

                stopRequest = new ProcessStopRequest(process);
                stopRegistration = cancellationToken.Register(
                    static state => ((ProcessStopRequest)state!).Start(),
                    stopRequest);

                try
                {
                    _ = process.WaitForInputIdle(5000);
                }
                catch
                {
                    // Some startup modes do not support WaitForInputIdle.
                }

                await Task.Delay(StartupDelay, cancellationToken);
                resultBaseline = CaptureResultFileBaseline(path);
                await SubmitBatchFolderAsync(process, path, progressContext, resultBaseline, cancellationToken);
            }
            finally
            {
                if (submissionLockAcquired)
                {
                    _ = BatchSubmissionLock.Release();
                }
            }

            await MonitorProgressAsync(
                process,
                path,
                progressContext,
                resultBaseline ?? new ResultFileBaseline(DateTimeOffset.UtcNow, new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase)),
                progress,
                cancellationToken);
        }
        finally
        {
            stopRegistration.Dispose();
            if (stopRequest?.PendingTask is { } pendingStopTask)
            {
                await pendingStopTask;
            }

            process?.Dispose();
        }
    }

    private static void RequestProcessStop(Process? process)
    {
        if (process is null)
        {
            return;
        }

        try
        {
            process.Refresh();
            if (process.HasExited)
            {
                return;
            }

            bool closeRequested = process.CloseMainWindow();
            if (closeRequested && WaitForExitWhileAnsweringNo(process, ProcessStopGracePeriod))
            {
                return;
            }

            process.Refresh();
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
        catch (NotSupportedException)
        {
        }
    }

    private static bool WaitForExitWhileAnsweringNo(Process process, TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            TryClickNoOnSaveConfirmationDialogs(process.Id);
            if (process.WaitForExit((int)WindowPollDelay.TotalMilliseconds))
            {
                return true;
            }

            process.Refresh();
            if (process.HasExited)
            {
                return true;
            }
        }

        return false;
    }

    private sealed class ProcessStopRequest
    {
        private readonly object syncRoot = new();
        private readonly Process process;
        private Task? stopTask;

        public ProcessStopRequest(Process process)
        {
            this.process = process;
        }

        public Task? PendingTask
        {
            get
            {
                lock (syncRoot)
                {
                    return stopTask;
                }
            }
        }

        public void Start()
        {
            lock (syncRoot)
            {
                stopTask ??= Task.Run(() => RequestProcessStop(process));
            }
        }
    }

    private static ProgressContext BuildProgressContext(string path)
    {
        List<string> elvxFiles = GetElvxFiles(path);
        if (elvxFiles.Count == 0)
        {
            throw new FileNotFoundException($"No .elvx files found in '{path}'.");
        }

        string projectPrefix = GetProjectPrefix(elvxFiles);
        HashSet<string> elvxBaseNames = new(StringComparer.OrdinalIgnoreCase);
        foreach (string fileName in elvxFiles)
        {
            string? baseName = Path.GetFileNameWithoutExtension(fileName);
            if (!string.IsNullOrWhiteSpace(baseName))
            {
                elvxBaseNames.Add(baseName);
            }
        }

        string scenario = GetScenarioFromPath(path);
        return new ProgressContext(projectPrefix, scenario, elvxBaseNames, elvxFiles.Count);
    }

    internal static bool HasCompletedScenarioOutputs(string path, int? expectedTotal = null)
    {
        return TryBuildProgressContext(path, out ProgressContext progressContext) &&
               HasCompletedScenarioOutputs(path, progressContext, expectedTotal);
    }

    private static bool TryBuildProgressContext(string path, out ProgressContext progressContext)
    {
        try
        {
            progressContext = BuildProgressContext(path);
            return true;
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException or FileNotFoundException or IOException or UnauthorizedAccessException)
        {
            progressContext = new ProgressContext(string.Empty, GetScenarioFromPath(path), [], 0);
            return false;
        }
    }

    private static bool HasCompletedScenarioOutputs(
        string path,
        ProgressContext progressContext,
        int? expectedTotal = null)
    {
        int requiredTotal = expectedTotal ?? progressContext.Total;
        return requiredTotal > 0 &&
               progressContext.Total == requiredTotal &&
               HasFreshBatchResults(path, baseline: null) &&
               CountCompletedCsvFiles(path, progressContext.ElvxBaseNames, baseline: null) >= requiredTotal;
    }

    private static async Task SubmitBatchFolderAsync(
        Process process,
        string path,
        ProgressContext progressContext,
        ResultFileBaseline resultBaseline,
        CancellationToken cancellationToken)
    {
        IntPtr windowHandle = await WaitForMainWindowAsync(process, cancellationToken);
        if (windowHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Elevate main window did not appear.");
        }

        if (!PostMessage(windowHandle, WmCommand, (IntPtr)RunBatchCommandId, IntPtr.Zero))
        {
            throw new InvalidOperationException("Unable to open the Elevate Run Batch dialog.");
        }

        IntPtr dialogHandle = await WaitForRunBatchDialogAsync(process.Id, cancellationToken);
        IntPtr editHandle = GetDlgItem(dialogHandle, DialogFolderEditControlId);
        IntPtr okHandle = GetDlgItem(dialogHandle, DialogOkControlId);

        if (editHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Run Batch folder input was not found.");
        }

        if (okHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Run Batch confirmation button was not found.");
        }

        _ = SendMessage(editHandle, WmSetText, IntPtr.Zero, path);
        _ = SendMessage(okHandle, BmClick, IntPtr.Zero, IntPtr.Zero);

        await WaitForDialogToCloseAsync(dialogHandle, cancellationToken);
        await WaitForBatchStartAsync(process, path, progressContext, resultBaseline, cancellationToken);
    }

    private static async Task<IntPtr> WaitForRunBatchDialogAsync(
        int processId,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IntPtr dialogHandle = FindRunBatchDialog(processId);
            if (dialogHandle != IntPtr.Zero)
            {
                return dialogHandle;
            }

            await Task.Delay(WindowPollDelay, cancellationToken);
        }

        throw new InvalidOperationException("Run Batch dialog did not open.");
    }

    private static async Task WaitForDialogToCloseAsync(
        IntPtr dialogHandle,
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + DialogCloseTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsWindow(dialogHandle))
            {
                return;
            }

            await Task.Delay(WindowPollDelay, cancellationToken);
        }

        throw new InvalidOperationException("Run Batch dialog did not close after folder submission.");
    }

    private static async Task WaitForBatchStartAsync(
        Process process,
        string path,
        ProgressContext progressContext,
        ResultFileBaseline resultBaseline,
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + BatchStartTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            process.Refresh();
            ProgressObservation observation = ObserveProgress(process.Id, path, progressContext, resultBaseline);
            if (observation.HasStarted)
            {
                return;
            }

            if (process.HasExited)
            {
                throw new InvalidOperationException("Elevate exited before batch processing started.");
            }

            await Task.Delay(ProgressPollDelay, cancellationToken);
        }

        throw new InvalidOperationException("Run Batch was submitted but calculation did not start.");
    }

    private static async Task MonitorProgressAsync(
        Process process,
        string path,
        ProgressContext progressContext,
        ResultFileBaseline resultBaseline,
        IProgress<ElevateProgressInfo>? progress,
        CancellationToken cancellationToken)
    {
        int observedMaximum = 0;
        int observedCompletedCsvFiles = 0;
        string? observedTitle = null;
        bool hasStarted = false;
        DateTimeOffset? completedOutputsSince = null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            process.Refresh();

            ProgressObservation observation = ObserveProgress(process.Id, path, progressContext, resultBaseline);
            hasStarted |= observation.HasStarted;
            observedMaximum = Math.Max(observedMaximum, observation.HighestWindowNumber);
            observedCompletedCsvFiles = Math.Max(observedCompletedCsvFiles, observation.CompletedCsvFiles);

            if (!string.IsNullOrWhiteSpace(observation.HighestWindowTitle))
            {
                observedTitle = observation.HighestWindowTitle;
            }
            else if (!string.IsNullOrWhiteSpace(observation.ActiveDocumentTitle))
            {
                observedTitle = observation.ActiveDocumentTitle;
            }

            int completed = Math.Clamp(Math.Max(observedMaximum, observedCompletedCsvFiles), 0, progressContext.Total);
            string source = observedMaximum > 0
                ? "window"
                : observedCompletedCsvFiles > 0
                    ? "csv"
                    : "window";

            ReportProgress(progress, progressContext, completed, source, observedTitle, isFinal: false);

            bool outputsComplete = hasStarted &&
                observation.HasBatchResults &&
                observation.CompletedCsvFiles >= progressContext.Total;

            if (outputsComplete)
            {
                completedOutputsSince ??= DateTimeOffset.UtcNow;
                if (observation.DesignVisible ||
                    DateTimeOffset.UtcNow - completedOutputsSince >= CompletedOutputsSettleDelay)
                {
                    await CloseDesignWindowsAfterCompletionAsync(process, cancellationToken);
                    ReportProgress(
                        progress,
                        progressContext,
                        progressContext.Total,
                        source,
                        observation.DesignWindowTitle ?? observedTitle,
                        isFinal: true);
                    return;
                }
            }
            else
            {
                completedOutputsSince = null;
            }

            if (process.HasExited)
            {
                if (observation.HasBatchResults && observation.CompletedCsvFiles >= progressContext.Total)
                {
                    ReportProgress(progress, progressContext, progressContext.Total, "csv", observedTitle, isFinal: true);
                    return;
                }

                throw new InvalidOperationException("Elevate exited before batch processing completed.");
            }

            await Task.Delay(ProgressPollDelay, cancellationToken);
        }
    }

    private static ProgressObservation ObserveProgress(
        int processId,
        string path,
        ProgressContext progressContext,
        ResultFileBaseline resultBaseline)
    {
        List<string> titles = GetObservedWindowTitles(processId);
        string? designTitle = titles.FirstOrDefault(IsDesignWindowTitle);
        (int highestWindowNumber, string? highestWindowTitle) = GetHighestWindowNumber(titles, progressContext.ProjectPrefix);
        string? activeDocumentTitle = titles
            .Where(title => IsProjectWindowTitle(title, progressContext.ProjectPrefix))
            .OrderByDescending(title => title.EndsWith(".elvx", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();
        int completedFiles = CountCompletedResultFiles(path, progressContext.ElvxBaseNames, resultBaseline);
        int completedCsvFiles = CountCompletedCsvFiles(path, progressContext.ElvxBaseNames, resultBaseline);
        bool hasBatchResults = HasFreshBatchResults(path, resultBaseline);
        bool hasStarted = highestWindowNumber > 0 || completedFiles > 0 || !string.IsNullOrWhiteSpace(activeDocumentTitle);

        return new ProgressObservation(
            highestWindowNumber,
            highestWindowTitle,
            activeDocumentTitle,
            designTitle,
            completedFiles,
            completedCsvFiles,
            hasBatchResults,
            hasStarted);
    }

    private static void ReportProgress(
        IProgress<ElevateProgressInfo>? progress,
        ProgressContext context,
        int completed,
        string source,
        string? windowTitle,
        bool isFinal)
    {
        progress?.Report(new ElevateProgressInfo(
            context.ProjectPrefix,
            context.Scenario,
            Math.Clamp(completed, 0, context.Total),
            context.Total,
            source,
            windowTitle,
            isFinal));
    }

    private static string GetScenarioFromPath(string path)
    {
        string folderName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (folderName.Equals("morning", StringComparison.OrdinalIgnoreCase))
        {
            return "morning";
        }

        if (folderName.Equals("lunch", StringComparison.OrdinalIgnoreCase))
        {
            return "lunch";
        }

        return "main";
    }

    internal static int CountCompletedResultFiles(string path, IReadOnlyCollection<string> elvxBaseNames)
    {
        return CountCompletedResultFiles(path, elvxBaseNames, baseline: null);
    }

    internal static int CountCompletedResultFiles(
        string path,
        IReadOnlyCollection<string> elvxBaseNames,
        ResultFileBaseline? baseline)
    {
        return CountCompletedFiles(path, elvxBaseNames, baseline, includeElvr: true);
    }

    internal static int CountCompletedCsvFiles(
        string path,
        IReadOnlyCollection<string> elvxBaseNames,
        ResultFileBaseline? baseline)
    {
        return CountCompletedFiles(path, elvxBaseNames, baseline, includeElvr: false);
    }

    private static int CountCompletedFiles(
        string path,
        IReadOnlyCollection<string> elvxBaseNames,
        ResultFileBaseline? baseline,
        bool includeElvr)
    {
        HashSet<string> allowedBaseNames = new(elvxBaseNames, StringComparer.OrdinalIgnoreCase);
        HashSet<string> completed = new(StringComparer.OrdinalIgnoreCase);

        foreach (string file in Directory.EnumerateFiles(path))
        {
            if (!IsTrackableResultFile(file, includeElvr) || !IsFreshResultFile(file, baseline))
            {
                continue;
            }

            string fileName = Path.GetFileName(file);
            string normalizedBaseName = NormalizeResultBaseName(Path.GetFileNameWithoutExtension(fileName));
            if (allowedBaseNames.Contains(normalizedBaseName))
            {
                completed.Add(normalizedBaseName);
            }
        }

        return completed.Count;
    }

    private static ResultFileBaseline CaptureResultFileBaseline(string path)
    {
        Dictionary<string, DateTime> existingFiles = new(StringComparer.OrdinalIgnoreCase);

        foreach (string file in Directory.EnumerateFiles(path))
        {
            if (!IsTrackableResultFile(file, includeElvr: true) && !IsBatchResultsFile(file))
            {
                continue;
            }

            existingFiles[file] = File.GetLastWriteTimeUtc(file);
        }

        return new ResultFileBaseline(DateTimeOffset.UtcNow, existingFiles);
    }

    private static bool IsTrackableResultFile(string file, bool includeElvr)
    {
        string extension = Path.GetExtension(file);
        if (!extension.Equals(".csv", StringComparison.OrdinalIgnoreCase) &&
            (!includeElvr || !extension.Equals(".elvr", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        string fileName = Path.GetFileName(file);
        return !fileName.Equals("batch_results.csv", StringComparison.OrdinalIgnoreCase) &&
               !fileName.Equals("floor_area.csv", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBatchResultsFile(string file)
    {
        return Path.GetFileName(file).Equals("batch_results.csv", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasFreshBatchResults(string path, ResultFileBaseline? baseline)
    {
        string batchResultsPath = Path.Combine(path, "batch_results.csv");
        return File.Exists(batchResultsPath) && IsFreshResultFile(batchResultsPath, baseline);
    }

    private static bool IsFreshResultFile(string file, ResultFileBaseline? baseline)
    {
        if (baseline is null)
        {
            return true;
        }

        DateTime lastWriteTimeUtc = File.GetLastWriteTimeUtc(file);
        if (!baseline.ExistingFileWriteTimesUtc.TryGetValue(file, out DateTime previousWriteTimeUtc))
        {
            return true;
        }

        return lastWriteTimeUtc > previousWriteTimeUtc;
    }

    internal static string NormalizeResultBaseName(string? baseName)
    {
        if (string.IsNullOrWhiteSpace(baseName))
        {
            return string.Empty;
        }

        string normalized = baseName.Trim();
        if (normalized.EndsWith("_elvx", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^5];
        }

        return normalized.Trim();
    }

    private static (int Maximum, string? Title) GetHighestWindowNumber(
        IEnumerable<string> titles,
        string projectPrefix)
    {
        int maximum = 0;
        string? title = null;

        foreach (string currentTitle in titles)
        {
            if (!TryParseWindowNumber(currentTitle, projectPrefix, out int number))
            {
                continue;
            }

            if (number <= maximum)
            {
                continue;
            }

            maximum = number;
            title = currentTitle;
        }

        return (maximum, title);
    }

    internal static bool TryParseWindowNumber(
        string title,
        string projectPrefix,
        out int number)
    {
        number = 0;

        string normalizedTitle = NormalizeWindowTitle(title);
        string normalizedPrefix = NormalizeWindowTitle(projectPrefix);
        if (!normalizedTitle.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        ReadOnlySpan<char> suffix = normalizedTitle.AsSpan(normalizedPrefix.Length).TrimStart();
        if (suffix.IsEmpty || !char.IsDigit(suffix[0]))
        {
            return false;
        }

        int digitLength = 0;
        while (digitLength < suffix.Length && char.IsDigit(suffix[digitLength]))
        {
            digitLength++;
        }

        return int.TryParse(suffix[..digitLength], NumberStyles.Integer, CultureInfo.InvariantCulture, out number);
    }

    internal static bool IsProjectWindowTitle(string? title, string projectPrefix)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        string normalizedTitle = NormalizeWindowTitle(title);
        if (string.IsNullOrWhiteSpace(normalizedTitle) || IsDesignWindowTitle(normalizedTitle))
        {
            return false;
        }

        string normalizedPrefix = NormalizeWindowTitle(projectPrefix);
        return normalizedTitle.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsDesignWindowTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        string normalized = NormalizeWindowTitle(title).Replace(" ", string.Empty, StringComparison.Ordinal);
        return normalized.Equals("Design1", StringComparison.OrdinalIgnoreCase);
    }

    internal static string NormalizeWindowTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        string normalized = title.Trim();
        if (TryExtractDocumentTitle(normalized, out string documentTitle))
        {
            normalized = documentTitle;
        }

        if (normalized.EndsWith(".elvx", StringComparison.OrdinalIgnoreCase))
        {
            normalized = Path.GetFileNameWithoutExtension(normalized) ?? normalized;
        }

        return normalized.Trim();
    }

    internal static bool TryExtractDocumentTitle(string? title, out string documentTitle)
    {
        documentTitle = string.Empty;
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        int closingBracket = title.LastIndexOf(']');
        int openingBracket = closingBracket > 0
            ? title.LastIndexOf('[', closingBracket)
            : -1;

        if (openingBracket < 0 || closingBracket <= openingBracket + 1)
        {
            return false;
        }

        documentTitle = title[(openingBracket + 1)..closingBracket].Trim();
        return !string.IsNullOrWhiteSpace(documentTitle);
    }

    private static string GetProjectPrefix(IReadOnlyList<string> elvxFiles)
    {
        string prefix = Path.GetFileNameWithoutExtension(elvxFiles[0])?.Trim() ?? string.Empty;

        for (int i = 1; i < elvxFiles.Count; i++)
        {
            string candidate = Path.GetFileNameWithoutExtension(elvxFiles[i])?.Trim() ?? string.Empty;
            prefix = GetCommonPrefix(prefix, candidate);
            if (string.IsNullOrWhiteSpace(prefix))
            {
                break;
            }
        }

        prefix = TrimProjectPrefix(prefix);
        if (!string.IsNullOrWhiteSpace(prefix))
        {
            return prefix;
        }

        return TrimProjectPrefix(Path.GetFileNameWithoutExtension(elvxFiles[0]) ?? string.Empty);
    }

    private static string GetCommonPrefix(string left, string right)
    {
        int length = Math.Min(left.Length, right.Length);
        int index = 0;
        while (index < length && char.ToUpperInvariant(left[index]) == char.ToUpperInvariant(right[index]))
        {
            index++;
        }

        return left[..index];
    }

    private static string TrimProjectPrefix(string value)
    {
        string result = value.Trim();

        while (result.Length > 0 && (char.IsDigit(result[^1]) || char.IsWhiteSpace(result[^1]) || result[^1] is '-' or '_' or '.'))
        {
            result = result[..^1];
        }

        return result.Trim();
    }

    private static string BuildIntegrationNotFoundMessage(ElevateIntegrationInfo integrationInfo)
    {
        IEnumerable<string> preview = integrationInfo.ProbedPaths.Take(8);
        string inspected = string.Join("; ", preview);
        if (string.IsNullOrWhiteSpace(inspected))
        {
            inspected = "no candidate paths";
        }

        return
            "Peters Research Elevate is not detected. Install Elevate or set ELEVATE_EXE_PATH. " +
            $"Checked: {inspected}.";
    }

    private static async Task<IntPtr> WaitForMainWindowAsync(
        Process process,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 25; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            process.Refresh();
            if (process.MainWindowHandle != IntPtr.Zero)
            {
                return process.MainWindowHandle;
            }

            await Task.Delay(WindowPollDelay, cancellationToken);
        }

        return IntPtr.Zero;
    }

    private static IntPtr FindRunBatchDialog(int processId)
    {
        IntPtr dialogHandle = IntPtr.Zero;

        EnumWindows((windowHandle, _) =>
        {
            GetWindowThreadProcessId(windowHandle, out uint windowProcessId);
            if (windowProcessId != (uint)processId)
            {
                return true;
            }

            if (!IsWindowVisible(windowHandle))
            {
                return true;
            }

            string windowClass = GetWindowClass(windowHandle);
            if (!windowClass.Equals("#32770", StringComparison.Ordinal))
            {
                return true;
            }

            if (GetDlgItem(windowHandle, DialogFolderEditControlId) == IntPtr.Zero ||
                GetDlgItem(windowHandle, DialogOkControlId) == IntPtr.Zero)
            {
                return true;
            }

            dialogHandle = windowHandle;
            return false;
        }, IntPtr.Zero);

        return dialogHandle;
    }

    private static string GetWindowClass(IntPtr windowHandle)
    {
        StringBuilder builder = new(256);
        _ = GetClassName(windowHandle, builder, builder.Capacity);
        return builder.ToString().Trim();
    }

    private static List<string> GetElvxFiles(string path)
    {
        return Directory
            .EnumerateFiles(path, "*.elvx")
            .Select(Path.GetFileName)
            .Where(fileName => !string.IsNullOrWhiteSpace(fileName))
            .Cast<string>()
            .OrderBy(fileName => fileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> GetObservedWindowTitles(int processId)
    {
        HashSet<string> titles = new(StringComparer.OrdinalIgnoreCase);

        foreach (string title in GetWindowTitles(processId))
        {
            if (!string.IsNullOrWhiteSpace(title))
            {
                titles.Add(title);
            }

            if (TryExtractDocumentTitle(title, out string documentTitle))
            {
                titles.Add(documentTitle);
            }
        }

        return titles.ToList();
    }

    private static List<string> GetWindowTitles(int processId)
    {
        List<string> titles = [];

        EnumWindows((windowHandle, _) =>
        {
            if (!IsWindowVisible(windowHandle))
            {
                return true;
            }

            GetWindowThreadProcessId(windowHandle, out uint windowProcessId);
            if (windowProcessId != (uint)processId)
            {
                return true;
            }

            int length = GetWindowTextLength(windowHandle);
            if (length <= 0)
            {
                return true;
            }

            StringBuilder builder = new(length + 1);
            _ = GetWindowText(windowHandle, builder, builder.Capacity);
            string title = builder.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(title))
            {
                titles.Add(title);
            }

            return true;
        }, IntPtr.Zero);

        return titles;
    }

    private static async Task CloseDesignWindowsAfterCompletionAsync(
        Process process,
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + DesignWindowAppearTimeout;
        bool observedDesignWindow = false;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            process.Refresh();
            if (process.HasExited)
            {
                return;
            }

            List<IntPtr> designWindows = GetProcessWindowHandles(process.Id, IsDesignWindowHandle);
            if (designWindows.Count > 0)
            {
                observedDesignWindow = true;
                CloseDesignWindowHandles(process.Id, designWindows);
            }
            else if (observedDesignWindow)
            {
                TryClickNoOnSaveConfirmationDialogs(process.Id);
            }
            else
            {
                TryClickNoOnSaveConfirmationDialogs(process.Id);
            }

            await Task.Delay(WindowPollDelay, cancellationToken);
        }
    }

    private static void CloseDesignWindows(int processId)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + DialogCloseTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            List<IntPtr> designWindows = GetProcessWindowHandles(processId, IsDesignWindowHandle);
            if (designWindows.Count == 0)
            {
                return;
            }

            CloseDesignWindowHandles(processId, designWindows);
            Thread.Sleep(WindowPollDelay);
        }
    }

    private static void CloseDesignWindowHandles(int processId, IReadOnlyCollection<IntPtr> designWindows)
    {
        foreach (IntPtr windowHandle in designWindows)
        {
            _ = PostMessage(windowHandle, WmClose, IntPtr.Zero, IntPtr.Zero);
        }

        TryClickNoOnSaveConfirmationDialogs(processId);
    }

    private static bool IsDesignWindowHandle(IntPtr windowHandle)
    {
        return IsDesignWindowTitle(GetWindowTitle(windowHandle));
    }

    private static bool TryClickNoOnSaveConfirmationDialogs(int processId)
    {
        bool clicked = false;
        foreach (IntPtr dialogHandle in GetProcessWindowHandles(processId, IsSaveConfirmationDialogHandle))
        {
            IntPtr noButtonHandle = FindNoSaveButton(dialogHandle);
            if (noButtonHandle == IntPtr.Zero)
            {
                clicked |= PostMessage(dialogHandle, WmCommand, (IntPtr)DialogNoControlId, IntPtr.Zero);
            }
            else
            {
                _ = SendMessage(noButtonHandle, BmClick, IntPtr.Zero, IntPtr.Zero);
                clicked = true;
            }
        }

        return clicked;
    }

    private static bool IsSaveConfirmationDialogHandle(IntPtr windowHandle)
    {
        if (!GetWindowClass(windowHandle).Equals("#32770", StringComparison.Ordinal))
        {
            return false;
        }

        IReadOnlyList<string> childTexts = GetChildWindowTexts(windowHandle);
        return IsSaveConfirmationDialogText(GetWindowTitle(windowHandle), childTexts);
    }

    internal static bool IsSaveConfirmationDialogText(string? title, IEnumerable<string> childTexts)
    {
        string searchText = BuildDialogSearchText(title, childTexts);
        if (IsSavePromptText(searchText))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(title) &&
               title.Contains("Elevate", StringComparison.OrdinalIgnoreCase) &&
               childTexts.Any(IsNoSaveButtonText);
    }

    private static IntPtr FindNoSaveButton(IntPtr dialogHandle)
    {
        IntPtr noButtonHandle = GetDlgItem(dialogHandle, DialogNoControlId);
        if (noButtonHandle != IntPtr.Zero)
        {
            return noButtonHandle;
        }

        IntPtr foundHandle = IntPtr.Zero;
        EnumChildWindows(dialogHandle, (childHandle, _) =>
        {
            if (GetWindowClass(childHandle).Equals("Button", StringComparison.OrdinalIgnoreCase) &&
                IsNoSaveButtonText(GetWindowTitle(childHandle)))
            {
                foundHandle = childHandle;
                return false;
            }

            return true;
        }, IntPtr.Zero);

        return foundHandle;
    }

    internal static bool IsNoSaveButtonText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string normalized = text
            .Replace("&", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Trim();

        return normalized.Equals("No", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("Нет", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsSavePromptText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string normalized = text.ToLowerInvariant();
        return normalized.Contains("save", StringComparison.Ordinal) ||
               normalized.Contains("сохран", StringComparison.Ordinal) ||
               normalized.Contains("close", StringComparison.Ordinal) ||
               normalized.Contains("exit", StringComparison.Ordinal) ||
               normalized.Contains("quit", StringComparison.Ordinal) ||
               normalized.Contains("закры", StringComparison.Ordinal) ||
               normalized.Contains("выход", StringComparison.Ordinal) ||
               normalized.Contains("заверш", StringComparison.Ordinal);
    }

    private static string BuildDialogSearchText(IntPtr dialogHandle)
    {
        return BuildDialogSearchText(GetWindowTitle(dialogHandle), GetChildWindowTexts(dialogHandle));
    }

    private static string BuildDialogSearchText(string? title, IEnumerable<string> childTexts)
    {
        StringBuilder builder = new();
        builder.Append(title);

        foreach (string childText in childTexts)
        {
            builder.Append(' ');
            builder.Append(childText);
        }

        return builder.ToString();
    }

    private static IReadOnlyList<string> GetChildWindowTexts(IntPtr parentHandle)
    {
        List<string> texts = [];

        EnumChildWindows(parentHandle, (childHandle, _) =>
        {
            string text = GetWindowTitle(childHandle);
            if (!string.IsNullOrWhiteSpace(text))
            {
                texts.Add(text);
            }

            return true;
        }, IntPtr.Zero);

        return texts;
    }

    private static List<IntPtr> GetProcessWindowHandles(
        int processId,
        Func<IntPtr, bool> predicate)
    {
        List<IntPtr> handles = [];

        EnumWindows((windowHandle, _) =>
        {
            if (!IsWindowVisible(windowHandle))
            {
                return true;
            }

            GetWindowThreadProcessId(windowHandle, out uint windowProcessId);
            if (windowProcessId != (uint)processId)
            {
                return true;
            }

            if (predicate(windowHandle))
            {
                handles.Add(windowHandle);
            }

            return true;
        }, IntPtr.Zero);

        return handles;
    }

    private static string GetWindowTitle(IntPtr windowHandle)
    {
        int length = GetWindowTextLength(windowHandle);
        if (length <= 0)
        {
            return string.Empty;
        }

        StringBuilder builder = new(length + 1);
        _ = GetWindowText(windowHandle, builder, builder.Capacity);
        return builder.ToString().Trim();
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, string lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDlgItem(IntPtr hDlg, int nIDDlgItem);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    private sealed record ProgressContext(
        string ProjectPrefix,
        string Scenario,
        HashSet<string> ElvxBaseNames,
        int Total);

    private sealed record ProgressObservation(
        int HighestWindowNumber,
        string? HighestWindowTitle,
        string? ActiveDocumentTitle,
        string? DesignWindowTitle,
        int CompletedFiles,
        int CompletedCsvFiles,
        bool HasBatchResults,
        bool HasStarted)
    {
        public bool DesignVisible => !string.IsNullOrWhiteSpace(DesignWindowTitle);
    }

    internal sealed record ResultFileBaseline(
        DateTimeOffset CapturedAtUtc,
        IReadOnlyDictionary<string, DateTime> ExistingFileWriteTimesUtc);
}
