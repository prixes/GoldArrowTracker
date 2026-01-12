// Copyright (c) GoldArrowTracker. Licensed under the GNU AFFERO GENERAL PUBLIC LICENSE v3.0

global using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Archery.Shared.Services;
using Archery.Shared.Models;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing;
using SixLabors.Fonts;
using Color = SixLabors.ImageSharp.Color;

namespace GoldTracker.Mobile.Services;

/// <summary>
/// Service for processing images and performing archery target analysis.
/// Includes Object Detection-based target detection for arrow scoring.
/// </summary>
public class ImageProcessingService
{
    private readonly ITargetScoringService? _targetScoringService;
    private readonly ObjectDetectionConfig _objectDetectionConfig;
    
    private SixLabors.Fonts.Font? _cachedFont;
    private readonly SemaphoreSlim _fontLock = new(1, 1);

    /// <summary>
    /// Gets the Object Detection configuration used by the service.
    /// </summary>
    public ObjectDetectionConfig ObjectDetectionConfig => _objectDetectionConfig;

    /// <summary>
    /// Initializes the service.
    /// </summary>
    public ImageProcessingService(ITargetScoringService targetScoringService, ObjectDetectionConfig config)
    {
        _targetScoringService = targetScoringService;
        _objectDetectionConfig = config;
    }

    /// <summary>
    /// Checks if Object Detection model is available and ready for inference.
    /// </summary>
    public bool IsModelAvailable => _targetScoringService != null;

    /// <summary>
    /// Gets the model path being used (for diagnostics).
    /// </summary>
    public string? ModelPath => ObjectDetectionModelDeploymentService.GetModelPath();

    /// <summary>
    /// Analyzes an archery target image.
    /// </summary>
    public async Task<TargetAnalysisResult> AnalyzeTargetImageAsync(string imagePath)
    {
        if (_targetScoringService == null)
        {
            return new TargetAnalysisResult
            {
                Status = AnalysisStatus.Failure,
                ErrorMessage = "Target scoring service not initialized."
            };
        }

        if (!System.IO.File.Exists(imagePath))
        {
            return new TargetAnalysisResult
            {
                Status = AnalysisStatus.Failure,
                ErrorMessage = $"Image file not found: {imagePath}"
            };
        }

        var imageBytes = await System.IO.File.ReadAllBytesAsync(imagePath);
        return await _targetScoringService.AnalyzeTargetImageAsync(imageBytes);
    }

    /// <summary>
    /// Analyzes raw image bytes for archery target detection.
    /// </summary>
    public async Task<TargetAnalysisResult> AnalyzeTargetFromBytesAsync(byte[] imageBytes)
    {
        if (_targetScoringService == null)
        {
            return new TargetAnalysisResult
            {
                Status = AnalysisStatus.Failure,
                ErrorMessage = "Target scoring service not initialized."
            };
        }

        if (imageBytes == null || imageBytes.Length == 0)
        {
            return new TargetAnalysisResult
            {
                Status = AnalysisStatus.Failure,
                ErrorMessage = "Image bytes cannot be null or empty."
            };
        }

        return await _targetScoringService.AnalyzeTargetImageAsync(imageBytes);
    }

    /// <summary>
    /// Draws the analysis results onto the image.
    /// </summary>
    public async Task<string> DrawDetectionsOnImageAsync(byte[] imageBytes, TargetAnalysisResult analysisResult)
    {
        using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(imageBytes);

        if (analysisResult.Status != AnalysisStatus.Success)
        {
            // return original image if analysis failed
            return Convert.ToBase64String(imageBytes);
        }

        var font = await GetFontAsync();

        // Draw all detections with a yellow ring
        foreach (var detection in analysisResult.Detections)
        {
            // Create an ellipse from the bounding box
            var ellipse = new EllipsePolygon(detection.X, detection.Y, detection.Width / 2, detection.Height / 2);
            image.Mutate(x => x.Draw(Color.Yellow, 2, ellipse)); // Yellow color, 2px thick line

            // Optionally add a label for the detected object class
            var labelText = $"{detection.ClassName} ({detection.Confidence:P0})"; // e.g., "target (95%)"
            // Position the label above the bounding box
            var labelTextLocation = new SixLabors.ImageSharp.PointF(detection.X - detection.Width / 2, detection.Y - detection.Height / 2 - 20); 
            
            var labelTextOptions = new RichTextOptions(font)
            {
                Origin = labelTextLocation,
                HorizontalAlignment = SixLabors.Fonts.HorizontalAlignment.Left
            };
            image.Mutate(x => x.DrawText(labelTextOptions, labelText, Color.Yellow));
        }

        // Draw target center and radius if detected
        if (analysisResult.TargetRadius > 0)
        {
            var targetCenterMarker = new EllipsePolygon(analysisResult.TargetCenter.X, analysisResult.TargetCenter.Y, 10);
            image.Mutate(x => x.Draw(Color.Blue, 4, targetCenterMarker));
            var targetCircle = new EllipsePolygon(analysisResult.TargetCenter.X, analysisResult.TargetCenter.Y, analysisResult.TargetRadius);
            image.Mutate(x => x.Draw(Color.Blue, 4, targetCircle));
        }

        // Draw arrows and scores
        foreach (var arrow in analysisResult.ArrowScores)
        {
            var arrowPoint = new EllipsePolygon(arrow.Detection.CenterX, arrow.Detection.CenterY, 5);
            image.Mutate(x => x.Draw(Color.Red, 3, arrowPoint));

            var scoreText = arrow.Points.ToString();
            var textLocation = new SixLabors.ImageSharp.PointF(arrow.Detection.CenterX + 10, arrow.Detection.CenterY - 10);
            
            var textOptions = new RichTextOptions(font) 
            { 
                Origin = textLocation
            };

            image.Mutate(x => x.DrawText(textOptions, scoreText, Color.White));
        }
        
        using var ms = new System.IO.MemoryStream();
        await image.SaveAsJpegAsync(ms);
        return Convert.ToBase64String(ms.ToArray());
    }

    private async Task<SixLabors.Fonts.Font> GetFontAsync()
    {
        if (_cachedFont != null) return _cachedFont;

        await _fontLock.WaitAsync();
        try
        {
            if (_cachedFont != null) return _cachedFont;

            var fontCollection = new FontCollection();
            using var fontStream = await FileSystem.OpenAppPackageFileAsync("OpenSans-Regular.ttf");
            
            using var memoryStream = new System.IO.MemoryStream();
            await fontStream.CopyToAsync(memoryStream);
            memoryStream.Position = 0;
            
            var fontFamily = fontCollection.Add(memoryStream);
            _cachedFont = fontFamily.CreateFont(24, FontStyle.Bold);
            return _cachedFont;
        }
        finally
        {
            _fontLock.Release();
        }
    }

    /// <summary>
    /// Converts an image file to a base64 string for display in Blazor.
    /// </summary>
    public async Task<string> ImageToBase64Async(string imagePath)
    {
        try
        {
            var bytes = await System.IO.File.ReadAllBytesAsync(imagePath);
            return Convert.ToBase64String(bytes);
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Saves a base64-encoded image to a file.
    /// </summary>
    public async Task<string> SaveBase64ImageAsync(string base64Data, string fileName)
    {
        try
        {
            var appDataPath = FileSystem.AppDataDirectory;
            var filePath = System.IO.Path.Combine(appDataPath, fileName);

            var imageBytes = Convert.FromBase64String(base64Data);
            await System.IO.File.WriteAllBytesAsync(filePath, imageBytes);

            return filePath;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Crops an image based on normalized coordinates (0-1).
    /// </summary>
    public async Task<byte[]> CropImageAsync(byte[] imageBytes, double startXNorm, double startYNorm, double widthNorm, double heightNorm)
    {
        using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(imageBytes);
        image.Mutate(x => x.AutoOrient());
        
        int x = (int)(startXNorm * image.Width);
        int y = (int)(startYNorm * image.Height);
        int w = (int)(widthNorm * image.Width);
        int h = (int)(heightNorm * image.Height);

        Console.WriteLine($"[ImageProcessing] CropImageAsync: Image Size={image.Width}x{image.Height}");
        Console.WriteLine($"[ImageProcessing] Crop Coordinates: x={x}, y={y}, w={w}, h={h}");

        // Ensure bounds
        x = Math.Max(0, x);
        y = Math.Max(0, y);
        w = Math.Min(w, image.Width - x);
        h = Math.Min(h, image.Height - y);

        if (w <= 0 || h <= 0) return Array.Empty<byte>();

        image.Mutate(ctx => ctx.Crop(new SixLabors.ImageSharp.Rectangle(x, y, w, h)));

        using var ms = new System.IO.MemoryStream();
        await image.SaveAsJpegAsync(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Crops an image based on normalized coordinates (0-1) using a pre-loaded Image object.
    /// This is much more efficient when performing multiple crops on the same source image.
    /// </summary>
    public async Task<byte[]> CropImageAsync(SixLabors.ImageSharp.Image<Rgba32> sourceImage, double startXNorm, double startYNorm, double widthNorm, double heightNorm)
    {
        int x = (int)(startXNorm * sourceImage.Width);
        int y = (int)(startYNorm * sourceImage.Height);
        int w = (int)(widthNorm * sourceImage.Width);
        int h = (int)(heightNorm * sourceImage.Height);

        // Ensure bounds
        x = Math.Max(0, x);
        y = Math.Max(0, y);
        w = Math.Min(w, sourceImage.Width - x);
        h = Math.Min(h, sourceImage.Height - y);

        if (w <= 0 || h <= 0) return Array.Empty<byte>();

        // Clone the region to avoid modifying the source
        using var croppedImage = sourceImage.Clone(ctx => ctx.Crop(new SixLabors.ImageSharp.Rectangle(x, y, w, h)));

        using var ms = new System.IO.MemoryStream();
        // Use quality 60 for faster encoding (was using default 75)
        var encoder = new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder { Quality = 60 };
        await croppedImage.SaveAsJpegAsync(ms, encoder);
        return ms.ToArray();
    }
}