using System.IO;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace ElevateHelperWinUI;

public sealed partial class MainWindow : Window
{
    private const int PreferredClientWidth = 1100;
    private const int PreferredClientHeight = 900;
    private const int WorkAreaMargin = 40;
    private bool closeAllowed;
    private bool shutdownInProgress;

    public MainWindow()
    {
        this.InitializeComponent();
        this.Title = "Elevate Helper";
        ConfigureWindowIcon();
        ConfigureWindowSize();
        MainPageContent.Loaded += OnMainPageContentLoaded;
        AppWindow.Closing += OnAppWindowClosing;
    }

    private void OnMainPageContentLoaded(object sender, RoutedEventArgs e)
    {
        MainPageContent.Loaded -= OnMainPageContentLoaded;
        ConfigureWindowSize();
    }

    private async void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (closeAllowed)
        {
            return;
        }

        args.Cancel = true;
        if (shutdownInProgress)
        {
            return;
        }

        shutdownInProgress = true;
        bool shutdownConfirmed;
        try
        {
            shutdownConfirmed = await MainPageContent.ConfirmShutdownAsync();
        }
        catch
        {
            // If confirmation cannot be shown, keep the app open rather than discard work.
            shutdownInProgress = false;
            return;
        }

        if (!shutdownConfirmed)
        {
            shutdownInProgress = false;
            return;
        }

        try
        {
            MainPageContent.BeginShutdownFeedback();
            await MainPageContent.ShutdownAsync();
        }
        catch
        {
            // Closing must still complete after best-effort process and COM cleanup.
        }

        shutdownInProgress = false;
        closeAllowed = true;
        Close();
    }

    private void ConfigureWindowIcon()
    {
        try
        {
            string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            if (File.Exists(iconPath))
            {
                AppWindow.SetIcon(iconPath);
            }
        }
        catch
        {
            // Keep startup resilient if the icon cannot be applied at runtime.
        }
    }

    private void ConfigureWindowSize()
    {
        DisplayArea displayArea = DisplayArea.GetFromWindowId(
            AppWindow.Id,
            DisplayAreaFallback.Primary);
        RectInt32 workArea = displayArea.WorkArea;

        double rasterizationScale = MainPageContent.XamlRoot?.RasterizationScale ?? 1d;
        int margin = Math.Max(WorkAreaMargin, (int)Math.Round(WorkAreaMargin * rasterizationScale));
        int width = Math.Min(
            (int)Math.Round(PreferredClientWidth * rasterizationScale),
            Math.Max(1, workArea.Width - (margin * 2)));
        int height = Math.Min(
            (int)Math.Round(PreferredClientHeight * rasterizationScale),
            Math.Max(1, workArea.Height - (margin * 2)));

        AppWindow.ResizeClient(new SizeInt32(width, height));

        int x = workArea.X + Math.Max(0, (workArea.Width - AppWindow.Size.Width) / 2);
        int y = workArea.Y + Math.Max(0, (workArea.Height - AppWindow.Size.Height) / 2);
        AppWindow.Move(new PointInt32(x, y));
    }
}
