// Copyright (c) GoldArrowTracker. Licensed under the GNU AFFERO GENERAL PUBLIC LICENSE v3.0

namespace GoldTracker.Mobile.Services;

/// <summary>
/// Handles deployment of Object Detection model from embedded resources to app data directory.
/// This ensures the model is available on all platforms (Android, iOS, Windows, etc.)
/// </summary>
public class ObjectDetectionModelDeploymentService
{
    private const string EmbeddedResourceName = "GoldTracker.Mobile.ObjectModels.object_detection_model.onnx";
    private const string OutputFileName = "object_detection_model.onnx";
    
    /// <summary>
    /// Ensures Object Detection model is extracted to app data directory.
    /// Returns the path to the extracted model file.
    /// </summary>
    public static string EnsureModelDeployed()
    {
        try
        {
            var appDataPath = FileSystem.AppDataDirectory;
            var modelPath = Path.Combine(appDataPath, OutputFileName);
            
            System.Diagnostics.Debug.WriteLine($"[ObjectDetectionModelDeploymentService] Model target path: {modelPath}");
            System.Diagnostics.Debug.WriteLine($"[ObjectDetectionModelDeploymentService] Model already exists: {File.Exists(modelPath)}");
            
            // If model already extracted, return path
            if (File.Exists(modelPath))
            {
                var fileInfo = new FileInfo(modelPath);
                System.Diagnostics.Debug.WriteLine($"[ObjectDetectionModelDeploymentService] ? Using existing model ({fileInfo.Length} bytes)");
                return modelPath;
            }
            
            // Extract embedded resource
            System.Diagnostics.Debug.WriteLine($"[ObjectDetectionModelDeploymentService] Extracting embedded resource: {EmbeddedResourceName}");
            
            var assembly = typeof(App).Assembly;
            using (var resourceStream = assembly.GetManifestResourceStream(EmbeddedResourceName))
            {
                if (resourceStream == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[ObjectDetectionModelDeploymentService] ? Embedded resource not found: {EmbeddedResourceName}");
                    System.Diagnostics.Debug.WriteLine($"[ObjectDetectionModelDeploymentService] Available resources: {string.Join(", ", assembly.GetManifestResourceNames().Where(n => n.Contains("object", StringComparison.OrdinalIgnoreCase)))}");
                    throw new InvalidOperationException($"Embedded resource not found: {EmbeddedResourceName}"); // Throw exception instead of null
                }
                
                System.Diagnostics.Debug.WriteLine($"[ObjectDetectionModelDeploymentService] Resource found. Size: {resourceStream.Length} bytes");
                
                // Create output file
                using (var fileStream = File.Create(modelPath))
                {
                    resourceStream.CopyTo(fileStream);
                }
                
                var extractedInfo = new FileInfo(modelPath);
                System.Diagnostics.Debug.WriteLine($"[ObjectDetectionModelDeploymentService] ? Model extracted successfully ({extractedInfo.Length} bytes)");
                System.Diagnostics.Debug.WriteLine($"[ObjectDetectionModelDeploymentService] ? Model path: {modelPath}");
                
                return modelPath;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ObjectDetectionModelDeploymentService] ? Error deploying model: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[ObjectDetectionModelDeploymentService] ? Stack: {ex.StackTrace}");
            throw new InvalidOperationException($"Error deploying Object Detection model: {ex.Message}", ex); // Throw exception instead of null
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
            throw new InvalidOperationException($"Object Detection model not found at expected path: {modelPath}. Ensure it has been deployed.");
        }
        return modelPath;
    }
}
