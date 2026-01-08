using Microsoft.Extensions.Logging;
using MudBlazor.Services;
using GoldTracker.Mobile.Services;

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
            
            // Initialize ImageProcessingService with model path
            builder.Services.AddScoped<ImageProcessingService>(sp =>
            {
                System.Diagnostics.Debug.WriteLine("[MauiProgram.CreateMauiApp] Initializing ImageProcessingService...");
                
                // First, ensure model is deployed from embedded resources
                var deployedModelPath = YoloModelDeploymentService.EnsureModelDeployed();
                System.Diagnostics.Debug.WriteLine($"[MauiProgram.CreateMauiApp] Deployed model path: {deployedModelPath ?? "NULL"}");
                
                // If deployment succeeded, use that path; otherwise try other locations
                var modelPath = deployedModelPath ?? GetYoloModelPath();
                System.Diagnostics.Debug.WriteLine($"[MauiProgram.CreateMauiApp] Final model path: {modelPath ?? "NULL"}");
                
                var service = new ImageProcessingService(modelPath);
                System.Diagnostics.Debug.WriteLine($"[MauiProgram.CreateMauiApp] ImageProcessingService created. Model available: {service.IsModelAvailable}");
                return service;
            });

#if DEBUG
    		builder.Services.AddBlazorWebViewDeveloperTools();
    		builder.Logging.AddDebug();
#endif

            System.Diagnostics.Debug.WriteLine("[MauiProgram.CreateMauiApp] ✓ MauiApp created successfully");
            return builder.Build();
        }

        /// <summary>
        /// Determines the YOLO model path for the current platform.
        /// </summary>
        private static string? GetYoloModelPath()
        {
            System.Diagnostics.Debug.WriteLine("[MauiProgram.GetYoloModelPath] Starting model path detection...");
            
            try
            {
                // Primary: Check deployed model in app data (set by YoloModelDeploymentService)
                var deployedPath = YoloModelDeploymentService.GetModelPath();
                System.Diagnostics.Debug.WriteLine($"[MauiProgram.GetYoloModelPath] Checking deployed path: {deployedPath}");
                if (File.Exists(deployedPath))
                {
                    System.Diagnostics.Debug.WriteLine($"[MauiProgram.GetYoloModelPath] ✓ Found at deployed path");
                    return deployedPath;
                }
                
                // Secondary: Check the hardcoded development path (Windows only)
                var devPath = @"C:\Users\david\source\repos\GoldArrowTracker\Archery.Shared\ObjectModels\yolo11s.onnx";
                System.Diagnostics.Debug.WriteLine($"[MauiProgram.GetYoloModelPath] Checking dev path: {devPath}");
                if (File.Exists(devPath))
                {
                    System.Diagnostics.Debug.WriteLine($"[MauiProgram.GetYoloModelPath] ✓ Found at dev path");
                    return devPath;
                }

                // Tertiary: Check AppData directory
                var appDataPath = Path.Combine(FileSystem.AppDataDirectory, "yolo11s.onnx");
                System.Diagnostics.Debug.WriteLine($"[MauiProgram.GetYoloModelPath] Checking AppData: {appDataPath}");
                if (File.Exists(appDataPath))
                {
                    System.Diagnostics.Debug.WriteLine($"[MauiProgram.GetYoloModelPath] ✓ Found at AppData path");
                    return appDataPath;
                }

                // Quaternary: Check relative to executable
                var exePath = Path.Combine(AppContext.BaseDirectory, "ObjectModels", "yolo11s.onnx");
                System.Diagnostics.Debug.WriteLine($"[MauiProgram.GetYoloModelPath] Checking exe path: {exePath}");
                if (File.Exists(exePath))
                {
                    System.Diagnostics.Debug.WriteLine($"[MauiProgram.GetYoloModelPath] ✓ Found at exe path");
                    return exePath;
                }

                System.Diagnostics.Debug.WriteLine("[MauiProgram.GetYoloModelPath] ✗ Model not found in any location");
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MauiProgram.GetYoloModelPath] ✗ Error: {ex.Message}");
                return null;
            }
        }
    }
}
