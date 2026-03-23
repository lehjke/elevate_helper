using System.Diagnostics;
using System.Runtime.InteropServices;
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
        return LaunchAndSubmitPathAsync(path, cancellationToken);
    }

    public async Task LaunchOfficeAsync(
        string path,
        bool includeLunchPeak,
        CancellationToken cancellationToken = default)
    {
        string morningPath = Path.Combine(path, "morning");
        await LaunchAndSubmitPathAsync(morningPath, cancellationToken);

        if (includeLunchPeak)
        {
            string lunchPath = Path.Combine(path, "lunch");
            await LaunchAndSubmitPathAsync(lunchPath, cancellationToken);
        }
    }

    private async Task LaunchAndSubmitPathAsync(string path, CancellationToken cancellationToken)
    {
        ElevateIntegrationInfo integrationInfo = integrationService.GetIntegrationInfo();
        if (!integrationInfo.IsDetected || string.IsNullOrWhiteSpace(integrationInfo.ExecutablePath))
        {
            throw new FileNotFoundException(
                BuildIntegrationNotFoundMessage(integrationInfo),
                integrationInfo.ExecutablePath);
        }

        Process? process = Process.Start(new ProcessStartInfo
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
            throw new InvalidOperationException("Unable to send keyboard input to Elevate.");
        }
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
        public KEYBDINPUT KeyboardInput;
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
}
