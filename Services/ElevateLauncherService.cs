using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using ElevateHelperWinUI.Models;

namespace ElevateHelperWinUI.Services;

public sealed class ElevateLauncherService : IElevateLauncherService
{
    private const uint InputKeyboard = 1;
    private const uint KeyEventFKeyUp = 0x0002;
    private const uint KeyEventFUnicode = 0x0004;
    private const ushort VkAlt = 0x12;
    private const ushort VkA = 0x41;
    private const ushort VkDown = 0x28;
    private const ushort VkEnter = 0x0D;
    private const ushort VkTab = 0x09;
    private static readonly SemaphoreSlim UiAutomationLock = new(1, 1);

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
        Task morningTask = LaunchAndSubmitPathAsync(morningPath, morningProgress, cancellationToken);

        if (!includeLunchPeak)
        {
            await morningTask;
            return;
        }

        string lunchPath = Path.Combine(path, "lunch");
        Task lunchTask = LaunchAndSubmitPathAsync(lunchPath, lunchProgress, cancellationToken);

        await Task.WhenAll(morningTask, lunchTask);
    }

    private async Task LaunchAndSubmitPathAsync(
        string path,
        IProgress<ElevateProgressInfo>? progress,
        CancellationToken cancellationToken)
    {
        ProgressContext progressContext = BuildProgressContext(path);
        ReportProgress(progress, progressContext, 0, "window", null, isFinal: false);

        Process? process = null;
        await UiAutomationLock.WaitAsync(cancellationToken);
        try
        {
            ElevateIntegrationInfo integrationInfo = integrationService.GetIntegrationInfo();
            if (!integrationInfo.IsDetected || string.IsNullOrWhiteSpace(integrationInfo.ExecutablePath))
            {
                throw new FileNotFoundException(
                    BuildIntegrationNotFoundMessage(integrationInfo),
                    integrationInfo.ExecutablePath);
            }

            process = Process.Start(new ProcessStartInfo
            {
                FileName = integrationInfo.ExecutablePath,
                UseShellExecute = true,
            });

            if (process is null)
            {
                throw new InvalidOperationException("Unable to start Elevate.exe.");
            }

            try
            {
                _ = process.WaitForInputIdle(5000);
            }
            catch
            {
                // Some app startup modes do not support WaitForInputIdle.
            }

            await Task.Delay(TimeSpan.FromMilliseconds(1500), cancellationToken);

            IntPtr windowHandle = await WaitForMainWindowAsync(process, cancellationToken);
            if (windowHandle != IntPtr.Zero)
            {
                _ = SetForegroundWindow(windowHandle);
            }

            SetEnglishKeyboardLayout();

            await SendAltAAsync(cancellationToken);
            await PressRepeatedAsync(VkDown, 5, cancellationToken);
            await PressKeyAsync(VkEnter, cancellationToken);

            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
            await PressRepeatedAsync(VkTab, 3, cancellationToken);
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);

            TypeUnicodeText(path);
            await PressKeyAsync(VkEnter, cancellationToken);
        }
        finally
        {
            _ = UiAutomationLock.Release();
        }

        if (process is null)
        {
            throw new InvalidOperationException("Unable to start Elevate.exe.");
        }

        await MonitorProgressAsync(process, path, progressContext, progress, cancellationToken);
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

    private static async Task MonitorProgressAsync(
        Process process,
        string path,
        ProgressContext progressContext,
        IProgress<ElevateProgressInfo>? progress,
        CancellationToken cancellationToken)
    {
        int observedMaximum = 0;
        string? observedTitle = null;
        bool hasObservedWindowProgress = false;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            List<string> titles = GetWindowTitles(process.Id);
            string? finalTitle = titles.FirstOrDefault(
                title => title.Contains("Design 1", StringComparison.OrdinalIgnoreCase));
            if (finalTitle is not null)
            {
                ReportProgress(progress, progressContext, progressContext.Total, "window", finalTitle, isFinal: true);
                return;
            }

            (int currentMaximum, string? currentTitle) = GetHighestWindowNumber(titles, progressContext.ProjectPrefix);
            if (currentMaximum > 0)
            {
                hasObservedWindowProgress = true;
                if (currentMaximum >= observedMaximum)
                {
                    observedMaximum = currentMaximum;
                    observedTitle = currentTitle;
                }
            }

            int completed = hasObservedWindowProgress
                ? observedMaximum
                : CountCompletedCsvFiles(path, progressContext.ElvxBaseNames);
            completed = Math.Min(completed, progressContext.Total);

            string source = hasObservedWindowProgress ? "window" : "csv";
            ReportProgress(progress, progressContext, completed, source, observedTitle, isFinal: false);

            if (process.HasExited)
            {
                int csvCompleted = CountCompletedCsvFiles(path, progressContext.ElvxBaseNames);
                if (csvCompleted >= progressContext.Total)
                {
                    ReportProgress(progress, progressContext, progressContext.Total, "csv", null, isFinal: true);
                    return;
                }

                throw new InvalidOperationException("Elevate exited before the Design 1 window was detected.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(750), cancellationToken);
        }
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

    private static int CountCompletedCsvFiles(string path, IReadOnlyCollection<string> elvxBaseNames)
    {
        HashSet<string> allowedBaseNames = new(elvxBaseNames, StringComparer.OrdinalIgnoreCase);
        int count = 0;

        foreach (string csvFile in Directory.GetFiles(path, "*.csv"))
        {
            string fileName = Path.GetFileName(csvFile);
            if (fileName.Equals("batch_results.csv", StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals("floor_area.csv", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string? csvBaseName = Path.GetFileNameWithoutExtension(fileName);
            if (!string.IsNullOrWhiteSpace(csvBaseName) && allowedBaseNames.Contains(csvBaseName))
            {
                count++;
            }
        }

        return count;
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

    private static bool TryParseWindowNumber(
        string title,
        string projectPrefix,
        out int number)
    {
        number = 0;

        if (!title.StartsWith(projectPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        ReadOnlySpan<char> suffix = title.AsSpan(projectPrefix.Length).TrimStart();
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

            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
        }

        return IntPtr.Zero;
    }

    private static void SetEnglishKeyboardLayout()
    {
        // 00000409 is English (US).
        IntPtr hkl = LoadKeyboardLayout("00000409", 1);
        if (hkl != IntPtr.Zero)
        {
            _ = ActivateKeyboardLayout(hkl, 0);
        }
    }

    private static async Task SendAltAAsync(CancellationToken cancellationToken)
    {
        SendKeyDown(VkAlt);
        await Task.Delay(30, cancellationToken);
        PressKey(VkA);
        await Task.Delay(30, cancellationToken);
        SendKeyUp(VkAlt);
        await Task.Delay(50, cancellationToken);
    }

    private static async Task PressRepeatedAsync(
        ushort keyCode,
        int count,
        CancellationToken cancellationToken)
    {
        for (int i = 0; i < count; i++)
        {
            await PressKeyAsync(keyCode, cancellationToken);
        }
    }

    private static async Task PressKeyAsync(ushort keyCode, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PressKey(keyCode);
        await Task.Delay(40, cancellationToken);
    }

    private static void PressKey(ushort keyCode)
    {
        SendKeyDown(keyCode);
        SendKeyUp(keyCode);
    }

    private static void SendKeyDown(ushort keyCode)
    {
        SendKeyboardInput(keyCode, 0, 0);
    }

    private static void SendKeyUp(ushort keyCode)
    {
        SendKeyboardInput(keyCode, 0, KeyEventFKeyUp);
    }

    private static void TypeUnicodeText(string text)
    {
        foreach (char character in text)
        {
            SendKeyboardInput(0, character, KeyEventFUnicode);
            SendKeyboardInput(0, character, KeyEventFUnicode | KeyEventFKeyUp);
        }
    }

    private static void SendKeyboardInput(ushort virtualKey, ushort scanCode, uint flags)
    {
        INPUT[] inputs =
        [
            new INPUT
            {
                Type = InputKeyboard,
                Union = new InputUnion
                {
                    KeyboardInput = new KEYBDINPUT
                    {
                        VirtualKey = virtualKey,
                        ScanCode = scanCode,
                        Flags = flags,
                        Time = 0,
                        ExtraInfo = IntPtr.Zero,
                    },
                },
            },
        ];

        uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != (uint)inputs.Length)
        {
            int errorCode = Marshal.GetLastWin32Error();
            string message = errorCode switch
            {
                5 => "Access denied while sending keyboard input. Run Elevate Helper with the same privileges as Elevate.",
                87 => "SendInput received invalid parameters (cbSize mismatch).",
                _ => $"Unable to send keyboard input to Elevate. Win32Error={errorCode}.",
            };
            throw new InvalidOperationException(message);
        }
    }

    private static List<string> GetElvxFiles(string path)
    {
        return Directory
            .GetFiles(path)
            .Where(file => file.EndsWith(".elvx", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileName)
            .Where(fileName => !string.IsNullOrWhiteSpace(fileName))
            .Cast<string>()
            .ToList();
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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint numberOfInputs, INPUT[] inputs, int sizeOfInputStructure);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadKeyboardLayout(string keyboardLayoutId, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr ActivateKeyboardLayout(IntPtr keyboardLayoutHandle, uint flags);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint Type;
        public InputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MOUSEINPUT MouseInput;

        [FieldOffset(0)]
        public KEYBDINPUT KeyboardInput;

        [FieldOffset(0)]
        public HARDWAREINPUT HardwareInput;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint Message;
        public ushort ParamL;
        public ushort ParamH;
    }

    private sealed record ProgressContext(
        string ProjectPrefix,
        string Scenario,
        HashSet<string> ElvxBaseNames,
        int Total);
}
