namespace GoldTracker.Mobile
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            // Trigger async initialization of heavy services
            Task.Run(async () => 
            {
                try 
                {
                    var initService = activationState?.Context.Services.GetService<Services.ModelInitializationService>();
                    if (initService != null) await initService.InitializeAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[App] Init failed: {ex}");
                }
            });

            return new Window(new MainPage()) { Title = "GoldTracker.Mobile" };
        }
    }
}
