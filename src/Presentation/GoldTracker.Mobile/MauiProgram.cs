using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MudBlazor.Services;
using GoldTracker.Mobile.Services;
using GoldTracker.Mobile.Services.Sessions;
using Archery.Shared.Services;
using GoldTracker.Shared.UI.Services;
using GoldTracker.Shared.UI.Services.Abstractions;

namespace GoldTracker.Mobile
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                });

            // Load appsettings.json
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream("GoldTracker.Mobile.appsettings.json");
            
            var config = new ConfigurationBuilder()
                .AddJsonStream(stream)
                .Build();

            builder.Configuration.AddConfiguration(config);

            builder.Services.AddMauiBlazorWebView();
            builder.Services.AddMudBlazorDialog();
            builder.Services.AddMudBlazorSnackbar();
            builder.Services.AddMudServices();
            builder.Services.AddScoped<CameraService>();
            builder.Services.AddScoped<ICameraService>(sp => sp.GetRequiredService<CameraService>());
            
            // Register Image Preprocessor (Platform-specific optimization)
#if ANDROID
            builder.Services.AddSingleton<IImagePreprocessor, GoldTracker.Mobile.Platforms.Android.AndroidImagePreprocessorService>();
#else
            builder.Services.AddSingleton<IImagePreprocessor, DefaultImagePreprocessor>();
#endif

            // Register Model Initialization Service
            builder.Services.AddSingleton<ModelInitializationService>();

            // Register Object Detection Configuration (Proxy)
            builder.Services.AddSingleton(sp => 
            {
                var initService = sp.GetRequiredService<ModelInitializationService>();
                if (!initService.IsInitialized || initService.Config == null)
                {
                     // Return null if not ready, consumers should handle this or wait for init
                     return null!; 
                }
                return initService.Config!;
            });

            // Register Object Detection Service
            builder.Services.AddScoped<IObjectDetectionService>(sp => 
            {
                var initService = sp.GetRequiredService<ModelInitializationService>();
                var preprocessor = sp.GetRequiredService<IImagePreprocessor>();
                
                if (!initService.IsInitialized || string.IsNullOrEmpty(initService.ModelPath))
                {
                    // Return null if not ready
                    return null!;
                }

                return new ObjectDetectionService(initService.ModelPath!, initService.Config!, preprocessor);
            });

            // Register Target Scoring Service
            builder.Services.AddScoped<ITargetScoringService, TargetScoringService>();

            // Register platform-specific services
            builder.Services.AddScoped<IPlatformImageService, ImageProcessingService>();

            // Register Model Diagnostic Service
            builder.Services.AddScoped<ObjectDetectionModelDiagnosticService>();

            // Register Session Services
            builder.Services.AddSingleton<ISessionService, SessionService>();
            builder.Services.AddSingleton<ISessionSyncService>(sp => (SessionService)sp.GetRequiredService<ISessionService>());

            builder.Services.AddSingleton<ISessionState, SessionState>();
            builder.Services.AddScoped<IDatasetExportService, DatasetExportService>();
            builder.Services.AddSingleton<IServerAuthService, ServerAuthService>();
            builder.Services.AddSingleton<IDatasetSyncService, DatasetSyncService>();
            builder.Services.AddSingleton<IPathService, PathService>();
            builder.Services.AddSingleton<IPreferenceService, MauiPreferenceService>();
            builder.Services.AddSingleton<IPlatformProvider, MauiPlatformProvider>();
            builder.Services.AddSingleton<BackgroundSyncService>();
            
#if DEBUG
    		builder.Services.AddBlazorWebViewDeveloperTools();
    		builder.Logging.AddDebug();
#endif

            System.Diagnostics.Debug.WriteLine("[MauiProgram.CreateMauiApp] ✓ MauiApp created successfully");
            var app = builder.Build();

            // Task-based initialization to avoid blocking the main thread
            Task.Run(async () => {
                try {
                    // Small delay to let the UI finish launching and initial rendering
                    await Task.Delay(1000);

                    var initService = app.Services.GetRequiredService<ModelInitializationService>();
                    await initService.InitializeAsync();
                    
                    // Trigger BackgroundSyncService instantiation so it starts listening to events
                    app.Services.GetRequiredService<BackgroundSyncService>();
                    
                    System.Diagnostics.Debug.WriteLine("[MauiProgram] Background services initialized.");
                } catch (Exception ex) {
                    System.Diagnostics.Debug.WriteLine($"[MauiProgram] Background Init Error: {ex.Message}");
                }
            });

            return app;
        }
    }
}
