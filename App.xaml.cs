namespace ElevateHelperWinUI
{
    public partial class App : Application
    {
        public static Window? MainWindow { get; private set; }

        public App()
        {
            this.InitializeComponent();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            MainWindow ??= new MainWindow();
            MainWindow.Activate();
        }
    }
}
