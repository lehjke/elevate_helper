namespace ElevateHelperWinUI
{
    public partial class App : Application
    {
        private const string SingleInstanceMutexName = @"Local\ElevateHelperWinUI";
        private Mutex? singleInstanceMutex;

        public static Window? MainWindow { get; private set; }

        public App()
        {
            this.InitializeComponent();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            if (singleInstanceMutex is null)
            {
                Mutex candidateMutex = new(
                    initiallyOwned: true,
                    SingleInstanceMutexName,
                    out bool isPrimaryInstance);
                if (!isPrimaryInstance)
                {
                    candidateMutex.Dispose();
                    Exit();
                    return;
                }

                singleInstanceMutex = candidateMutex;
            }

            MainWindow ??= new MainWindow();
            MainWindow.Activate();
        }
    }
}
