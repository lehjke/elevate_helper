namespace ElevateHelperWinUI
{
    public partial class App : Application
    {
        private Window? window;

        public App()
        {
            this.InitializeComponent();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            window ??= new MainWindow();
            window.Activate();
        }
    }
}
