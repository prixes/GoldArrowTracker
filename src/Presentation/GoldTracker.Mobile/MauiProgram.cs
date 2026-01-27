using Microsoft.Extensions.Logging;
using MudBlazor.Services;
using GoldTracker.Mobile.Services;
using GoldTracker.Mobile.Services.Sessions;
using Archery.Shared.Models;
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

            // Register Object Detection Configuration
            builder.Services.AddSingleton(sp => 
            {
                // Load config synchronously during startup
                try 
                {
                    return ObjectDetectionConfig.LoadFromJsonAsync("Archery.Shared.Configurations.object_detection_config.json").Result;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[MauiProgram] Critical Error loading ObjectDetectionConfig: {ex.Message}");
                    throw;
                }
            });

            // Register Object Detection Service
            builder.Services.AddScoped<IObjectDetectionService>(sp => 
            {
                var config = sp.GetRequiredService<ObjectDetectionConfig>();
                var preprocessor = sp.GetRequiredService<IImagePreprocessor>();
                
                // Ensure model is deployed
                string modelPath;
                try
                {
                    modelPath = ObjectDetectionModelDeploymentService.EnsureModelDeployed();
                    System.Diagnostics.Debug.WriteLine($"[MauiProgram] Model deployed to: {modelPath}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[MauiProgram] Critical Error deploying model: {ex.Message}");
                    // In a real app we might want to handle this gracefully, but for now throw
                    throw;
                }

                return new ObjectDetectionService(modelPath, config, preprocessor);
            });

            // Register Target Scoring Service
            builder.Services.AddScoped<ITargetScoringService, TargetScoringService>();

            // Register platform-specific services
            builder.Services.AddScoped<IPlatformImageService, ImageProcessingService>();

            // Register Model Diagnostic Service
            builder.Services.AddScoped<ObjectDetectionModelDiagnosticService>();

            // Register Session Services
            builder.Services.AddSingleton<ISessionService, SessionService>();

            builder.Services.AddSingleton<ISessionState, SessionState>();
            builder.Services.AddScoped<IDatasetExportService, DatasetExportService>();
            
#if DEBUG
    		builder.Services.AddBlazorWebViewDeveloperTools();
    		builder.Logging.AddDebug();
#endif

            System.Diagnostics.Debug.WriteLine("[MauiProgram.CreateMauiApp] ✓ MauiApp created successfully");
            return builder.Build();
        }
    }
}
