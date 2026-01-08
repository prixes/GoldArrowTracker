// Copyright (c) GoldArrowTracker. Licensed under the GNU AFFERO GENERAL PUBLIC LICENSE v3.0

namespace GoldTracker.Mobile.Services;

/// <summary>
/// Handles deployment of YOLO model from embedded resources to app data directory.
/// This ensures the model is available on all platforms (Android, iOS, Windows, etc.)
/// </summary>
public class YoloModelDeploymentService
{
    private const string EmbeddedResourceName = "GoldTracker.Mobile.ObjectModels.yolo11s.onnx";
    private const string OutputFileName = "yolo11s.onnx";
    
    /// <summary>
    /// Ensures YOLO model is extracted to app data directory.
    /// Returns the path to the extracted model file.
    /// </summary>
    public static string EnsureModelDeployed()
    {
        try
        {
            var appDataPath = FileSystem.AppDataDirectory;
            var modelPath = Path.Combine(appDataPath, OutputFileName);
            
            System.Diagnostics.Debug.WriteLine($"[YoloModelDeploymentService] Model target path: {modelPath}");
            System.Diagnostics.Debug.WriteLine($"[YoloModelDeploymentService] Model already exists: {File.Exists(modelPath)}");
            
            // If model already extracted, return path
            if (File.Exists(modelPath))
            {
                var fileInfo = new FileInfo(modelPath);
                System.Diagnostics.Debug.WriteLine($"[YoloModelDeploymentService] ? Using existing model ({fileInfo.Length} bytes)");
                return modelPath;
            }
            
            // Extract embedded resource
            System.Diagnostics.Debug.WriteLine($"[YoloModelDeploymentService] Extracting embedded resource: {EmbeddedResourceName}");
            
            var assembly = typeof(App).Assembly;
            using (var resourceStream = assembly.GetManifestResourceStream(EmbeddedResourceName))
            {
                if (resourceStream == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[YoloModelDeploymentService] ? Embedded resource not found: {EmbeddedResourceName}");
                    System.Diagnostics.Debug.WriteLine($"[YoloModelDeploymentService] Available resources: {string.Join(", ", assembly.GetManifestResourceNames().Where(n => n.Contains("yolo", StringComparison.OrdinalIgnoreCase)))}");
                    throw new InvalidOperationException($"Embedded resource not found: {EmbeddedResourceName}"); // Throw exception instead of null
                }
                
                System.Diagnostics.Debug.WriteLine($"[YoloModelDeploymentService] Resource found. Size: {resourceStream.Length} bytes");
                
                // Create output file
                using (var fileStream = File.Create(modelPath))
                {
                    resourceStream.CopyTo(fileStream);
                }
                
                var extractedInfo = new FileInfo(modelPath);
                System.Diagnostics.Debug.WriteLine($"[YoloModelDeploymentService] ? Model extracted successfully ({extractedInfo.Length} bytes)");
                System.Diagnostics.Debug.WriteLine($"[YoloModelDeploymentService] ? Model path: {modelPath}");
                
                return modelPath;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[YoloModelDeploymentService] ? Error deploying model: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[YoloModelDeploymentService] ? Stack: {ex.StackTrace}");
            throw new InvalidOperationException($"Error deploying YOLO model: {ex.Message}", ex); // Throw exception instead of null
        }
    }
    
    /// <summary>
    /// Gets the expected model path in app data directory.
    /// </summary>
    public static string GetModelPath()
    {
        var modelPath = Path.Combine(FileSystem.AppDataDirectory, OutputFileName);
        if (!File.Exists(modelPath))
        {
            throw new InvalidOperationException($"YOLO model not found at expected path: {modelPath}. Ensure it has been deployed.");
        }
        return modelPath;
    }
}
