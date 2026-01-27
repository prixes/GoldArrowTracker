// Copyright (c) GoldArrowTracker. Licensed under the GNU AFFERO GENERAL PUBLIC LICENSE v3.0

namespace GoldTracker.Mobile.Services;

/// <summary>
/// Diagnostic service for debugging Object Detection model loading issues.
/// </summary>
public class ObjectDetectionModelDiagnosticService
{
    private readonly ImageProcessingService _imageProcessingService;
    private readonly List<string> _diagnostics = new();

    public ObjectDetectionModelDiagnosticService(ImageProcessingService imageProcessingService)
    {
        _imageProcessingService = imageProcessingService;
    }

    /// <summary>
    /// Gets a comprehensive diagnostic report about the Object Detection model status.
    /// </summary>
    public string GetDiagnosticReport()
    {
        _diagnostics.Clear();
        
        _diagnostics.Add("=== Object Detection Model Diagnostic Report ===");
        _diagnostics.Add($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        _diagnostics.Add("");
        
        // Check model availability
        _diagnostics.Add($"Model Available: {(_imageProcessingService.IsModelAvailable ? "? YES" : "? NO")}");
        _diagnostics.Add($"Model Path: {_imageProcessingService.ModelPath ?? "NULL"}");
        _diagnostics.Add("");
        
        // Check file existence
        if (!string.IsNullOrEmpty(_imageProcessingService.ModelPath))
        {
            var exists = File.Exists(_imageProcessingService.ModelPath);
            _diagnostics.Add($"File Exists: {(exists ? "? YES" : "? NO")}");
            
            if (exists)
            {
                var fileInfo = new FileInfo(_imageProcessingService.ModelPath);
                _diagnostics.Add($"File Size: {fileInfo.Length} bytes ({FormatBytes(fileInfo.Length)})");
                _diagnostics.Add($"Last Modified: {fileInfo.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
                
                // Check file permissions
                try
                {
                    using (var fs = File.OpenRead(_imageProcessingService.ModelPath))
                    {
                        _diagnostics.Add("File Read Permission: ? YES");
                    }
                }
                catch (Exception ex)
                {
                    _diagnostics.Add($"File Read Permission: ? NO ({ex.Message})");
                }
            }
        }
        
        _diagnostics.Add("");
        _diagnostics.Add("=== Environment Info ===");
        _diagnostics.Add($"Platform: {DeviceInfo.Current.Platform}");
        _diagnostics.Add($"OS Version: {DeviceInfo.Current.VersionString}");
        _diagnostics.Add($"AppData Directory: {FileSystem.AppDataDirectory}");
        _diagnostics.Add($"BaseDirectory: {AppContext.BaseDirectory}");
        _diagnostics.Add($"Current Directory: {Directory.GetCurrentDirectory()}");
        
        _diagnostics.Add("");
        _diagnostics.Add("=== Common Model Locations ===");
        
        var possiblePaths = new[]
        {
            @"C:\Users\david\source\repos\GoldArrowTracker\Archery.Shared\ObjectModels\object_detection_model.onnx",
            Path.Combine(FileSystem.AppDataDirectory, "object_detection_model.onnx"),
            Path.Combine(AppContext.BaseDirectory, "ObjectModels", "object_detection_model.onnx"),
            Path.Combine(AppContext.BaseDirectory, "object_detection_model.onnx"),
        };
        
        foreach (var path in possiblePaths)
        {
            var exists = File.Exists(path);
            _diagnostics.Add($"{(exists ? "?" : "?")} {path}");
        }
        
        return string.Join("\n", _diagnostics);
    }

    /// <summary>
    /// Writes the diagnostic report to debug output.
    /// </summary>
    public void PrintDiagnostics()
    {
        var report = GetDiagnosticReport();
        System.Diagnostics.Debug.WriteLine(report);
    }

    /// <summary>
    /// Gets a simple status string.
    /// </summary>
    public string GetStatusSummary()
    {
        if (_imageProcessingService.IsModelAvailable)
        {
            return $"? Object Detection Model Ready\nPath: {_imageProcessingService.ModelPath}";
        }
        else
        {
            return "? Object Detection Model Not Found\nCheck debug output for details";
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}
