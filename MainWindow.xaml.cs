using System.IO;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace ElevateHelperWinUI;

public sealed partial class MainWindow : Window
{
    private const int PreferredWidth = 960;
    private const int PreferredHeight = 900;
    private const int WorkAreaMargin = 80;

    public MainWindow()
    {
        this.InitializeComponent();
        this.Title = "Elevate Helper";
        ConfigureWindowIcon();
        ConfigureWindowSize();
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

        int width = Math.Min(PreferredWidth, Math.Max(1, workArea.Width - WorkAreaMargin));
        int height = Math.Min(PreferredHeight, Math.Max(1, workArea.Height - WorkAreaMargin));

        AppWindow.Resize(new SizeInt32(width, height));

        int x = workArea.X + Math.Max(0, (workArea.Width - width) / 2);
        int y = workArea.Y + Math.Max(0, (workArea.Height - height) / 2);
        AppWindow.Move(new PointInt32(x, y));
    }
}
