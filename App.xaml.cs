namespace ElevateHelperWinUI
{
    public partial class App : Application
    {
        private const string SingleInstanceMutexName = @"Local\ElevateHelperWinUI";
        private const string ActivationEventName = @"Local\ElevateHelperWinUI.Activate";
        private Mutex? singleInstanceMutex;
        private EventWaitHandle? activationEvent;
        private RegisteredWaitHandle? activationRegistration;

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
                    SignalPrimaryInstance();
                    Exit();
                    return;
                }

                singleInstanceMutex = candidateMutex;
                StartActivationListener();
            }

            MainWindow ??= new MainWindow();
            MainWindow.Activate();
        }

        private void StartActivationListener()
        {
            activationEvent = new EventWaitHandle(
                initialState: false,
                EventResetMode.AutoReset,
                ActivationEventName);
            activationRegistration = ThreadPool.RegisterWaitForSingleObject(
                activationEvent,
                static (state, _) =>
                {
                    if (state is not App || MainWindow is null)
                    {
                        return;
                    }

                    _ = MainWindow.DispatcherQueue.TryEnqueue(() =>
                    {
                        MainWindow.AppWindow.Show(activateWindow: true);
                        MainWindow.Activate();
                    });
                },
                this,
                Timeout.Infinite,
                executeOnlyOnce: false);
        }

        private static void SignalPrimaryInstance()
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    using EventWaitHandle activationSignal = EventWaitHandle.OpenExisting(ActivationEventName);
                    _ = activationSignal.Set();
                    return;
                }
                catch (WaitHandleCannotBeOpenedException)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }
}
