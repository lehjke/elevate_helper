using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace ElevateHelperWinUI;

public sealed partial class MainWindow : Window
{
    private const int DefaultWidth = 475;
    private const int DefaultHeight = 760;

    public MainWindow()
    {
        this.InitializeComponent();
        this.Title = "Elevate Helper";
        ConfigureWindowSize();
    }

    private void ConfigureWindowSize()
    {
        AppWindow.Resize(new SizeInt32(DefaultWidth, DefaultHeight));

        DisplayArea displayArea = DisplayArea.GetFromWindowId(
            AppWindow.Id,
            DisplayAreaFallback.Primary);
        RectInt32 workArea = displayArea.WorkArea;

        int x = workArea.X + Math.Max(0, (workArea.Width - DefaultWidth) / 2);
        int y = workArea.Y + Math.Max(0, (workArea.Height - DefaultHeight) / 2);
        AppWindow.Move(new PointInt32(x, y));
    }
}
