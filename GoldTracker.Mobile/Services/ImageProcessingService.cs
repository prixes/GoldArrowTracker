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
/// Includes YOLO-based target detection for arrow scoring.
/// </summary>
public class ImageProcessingService
{
    private readonly TargetScoringService? _targetScoringService;
    private readonly string? _modelPath;
    private readonly YoloConfig _yoloConfig;

    /// <summary>
    /// Initializes the service with optional YOLO model path.
    /// If no model path provided, attempts to load from default location.
    /// </summary>
    public ImageProcessingService(string? modelPath = null)
    {
        // Load YoloConfig from embedded JSON
        _yoloConfig = Archery.Shared.Models.YoloConfig.LoadFromJsonAsync("Archery.Shared.Configurations.yoloConfig.json").GetAwaiter().GetResult();

        // Use provided path, or try to find default
        _modelPath = modelPath ?? GetDefaultModelPath();
        
        // Initialize target scoring service if model path is available and valid
        if (!string.IsNullOrEmpty(_modelPath))
        {
            if (!System.IO.File.Exists(_modelPath))
            {
                _modelPath = null;
                return;
            }

            try
            {
                var yoloInferenceService = new YoloInferenceService(_modelPath, _yoloConfig); // Pass the instantiated YoloConfig
                _targetScoringService = new TargetScoringService(yoloInferenceService);
            }
            catch (System.IO.FileNotFoundException)
            {
                _targetScoringService = null;
            }
            catch (InvalidOperationException)
            {
                _targetScoringService = null;
            }
            catch (Exception)
            {
                _targetScoringService = null;
            }
        }
    }

    /// <summary>
    /// Gets the default YOLO model path by checking multiple locations.
    /// </summary>
    private static string? GetDefaultModelPath()
    {
        var possiblePaths = new[]
        {
            // Primary: Deployed model in app data (from YoloModelDeploymentService)
            System.IO.Path.Combine(FileSystem.AppDataDirectory, "yolo11s.onnx"),
            
            // Development path (hardcoded for dev machine - Windows only)
            @"C:\Users\david\source\repos\GoldArrowTracker\Archery.Shared\ObjectModels\yolo11s.onnx",
            
            // Relative to base directory
            System.IO.Path.Combine(AppContext.BaseDirectory, "ObjectModels", "yolo11s.onnx"),
            System.IO.Path.Combine(AppContext.BaseDirectory, "yolo11s.onnx"),
        };

        foreach (var path in possiblePaths)
        {
            try
            {
                if (System.IO.File.Exists(path))
                {
                    return path;
                }
            }
            catch (Exception)
            {
                // Ignore errors checking paths
            }
        }
        return null;
    }

    /// <summary>
    /// Checks if YOLO model is available and ready for inference.
    /// </summary>
    public bool IsModelAvailable => _targetScoringService != null;

    /// <summary>
    /// Gets the model path being used, or null if not found.
    /// </summary>
    public string? ModelPath => _modelPath;

    /// <summary>
    /// Analyzes an archery target image using YOLO detection.
    /// </summary>
    public async Task<TargetAnalysisResult> AnalyzeTargetImageAsync(string imagePath)
    {
        if (_targetScoringService == null)
        {
            return new TargetAnalysisResult
            {
                Status = AnalysisStatus.Failure,
                ErrorMessage = "Target scoring service not initialized. YOLO model not found."
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
                ErrorMessage = "Target scoring service not initialized. YOLO model not found."
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

        // Load font
        var fontCollection = new FontCollection();
        using var fontStream = await FileSystem.OpenAppPackageFileAsync("OpenSans-Regular.ttf");
        
        // Copy the fontStream to a MemoryStream to ensure seekability
        using var memoryStream = new System.IO.MemoryStream();
        await fontStream.CopyToAsync(memoryStream);
        memoryStream.Position = 0; // Reset position to the beginning
        
        var fontFamily = fontCollection.Add(memoryStream);
        var font = fontFamily.CreateFont(24, FontStyle.Bold);

        // Draw all YOLO detections with a yellow ring
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
                        };            image.Mutate(x => x.DrawText(labelTextOptions, labelText, Color.Yellow));
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
    /// Flattens an image using perspective transformation based on corner points.
    /// Kept for backward compatibility but not used in YOLO workflow.
    /// </summary>
    [Obsolete("Perspective flattening is no longer needed with YOLO detection.")]
    public async Task<bool> FlattenImagePerspectiveAsync(
        string imagePath,
        string outputPath,
        (float X, float Y)[] sourceCorners)
    {
        try
        {
            if (!System.IO.File.Exists(imagePath))
                return false;

            using var image = await SixLabors.ImageSharp.Image.LoadAsync<Rgba32>(imagePath);

            if (sourceCorners == null || sourceCorners.Length != 4)
            {
                sourceCorners = AutoDetectTargetCorners(image);
            }

            var flattened = ApplyPerspectiveTransform(image, sourceCorners);
            await flattened.SaveAsync(outputPath);
            flattened.Dispose();

            return true;
        }
        catch
        {
            return false;
        }
    }

    private (float X, float Y)[] AutoDetectTargetCorners(SixLabors.ImageSharp.Image<Rgba32> image)
    {
        float inset = Math.Min(image.Width, image.Height) * 0.1f;

        return new[]
        {
            (inset, inset),
            (image.Width - inset, inset),
            (image.Width - inset, image.Height - inset),
            (inset, image.Height - inset)
        };
    }

    private SixLabors.ImageSharp.Image<Rgba32> ApplyPerspectiveTransform(SixLabors.ImageSharp.Image<Rgba32> sourceImage, (float X, float Y)[] corners)
    {
        if (corners == null || corners.Length != 4)
            return sourceImage.Clone();

        float topWidth = CalculateDistance(corners[0], corners[1]);
        float bottomWidth = CalculateDistance(corners[3], corners[2]);
        float leftHeight = CalculateDistance(corners[0], corners[3]);
        float rightHeight = CalculateDistance(corners[1], corners[2]);

        int outputWidth = (int)Math.Round((topWidth + bottomWidth) / 2f);
        int outputHeight = (int)Math.Round((leftHeight + rightHeight) / 2f);

        outputWidth = Math.Max(50, outputWidth);
        outputHeight = Math.Max(50, outputHeight);

        var output = new SixLabors.ImageSharp.Image<Rgba32>(outputWidth, outputHeight);

        for (int outY = 0; outY < outputHeight; outY++)
        {
            for (int outX = 0; outX < outputWidth; outX++)
            {
                float u = outputWidth > 1 ? outX / (float)(outputWidth - 1) : 0;
                float v = outputHeight > 1 ? outY / (float)(outputHeight - 1) : 0;

                var sourcePos = InversePerspectiveMap(corners, u, v, sourceImage.Width, sourceImage.Height);

                if (sourcePos.X >= 0 && sourcePos.X < sourceImage.Width - 1 &&
                    sourcePos.Y >= 0 && sourcePos.Y < sourceImage.Height - 1)
                {
                    var pixel = BilinearSamplePixel(sourceImage, sourcePos.X, sourcePos.Y);
                    output[outX, outY] = pixel;
                }
                else if (sourcePos.X >= 0 && sourcePos.X < sourceImage.Width &&
                         sourcePos.Y >= 0 && sourcePos.Y < sourceImage.Height)
                {
                    int sx = (int)Math.Round(sourcePos.X);
                    int sy = (int)Math.Round(sourcePos.Y);
                    sx = Math.Clamp(sx, 0, sourceImage.Width - 1);
                    sy = Math.Clamp(sy, 0, sourceImage.Height - 1);
                    output[outX, outY] = sourceImage[sx, sy];
                }
            }
        }

        return output;
    }

    private (float X, float Y) InversePerspectiveMap((float X, float Y)[] sourceCorners, float u, float v, int imgWidth, int imgHeight)
    {
        float topX = sourceCorners[0].X + (sourceCorners[1].X - sourceCorners[0].X) * u;
        float topY = sourceCorners[0].Y + (sourceCorners[1].Y - sourceCorners[0].Y) * u;

        float bottomX = sourceCorners[3].X + (sourceCorners[2].X - sourceCorners[3].X) * u;
        float bottomY = sourceCorners[3].Y + (sourceCorners[2].Y - sourceCorners[3].Y) * u;

        float srcX = topX + (bottomX - topX) * v;
        float srcY = topY + (bottomY - topY) * v;

        return (srcX, srcY);
    }

    private Rgba32 BilinearSamplePixel(SixLabors.ImageSharp.Image<Rgba32> source, float x, float y)
    {
        int x0 = (int)Math.Floor(x);
        int y0 = (int)Math.Floor(y);
        int x1 = Math.Min(x0 + 1, source.Width - 1);
        int y1 = Math.Min(y0 + 1, source.Height - 1);

        x0 = Math.Max(0, x0);
        y0 = Math.Max(0, y0);

        float fx = x - x0;
        float fy = y - y0;

        var p00 = source[x0, y0];
        var p10 = source[x1, y0];
        var p01 = source[x0, y1];
        var p11 = source[x1, y1];

        var top = LerpColor(p00, p10, fx);
        var bot = LerpColor(p01, p11, fx);
        return LerpColor(top, bot, fy);
    }

    private Rgba32 LerpColor(Rgba32 a, Rgba32 b, float t)
    {
        return new Rgba32(
            (byte)Math.Round(a.R + (b.R - a.R) * t),
            (byte)Math.Round(a.G + (b.G - a.G) * t),
            (byte)Math.Round(a.B + (b.B - a.B) * t),
            (byte)Math.Round(a.A + (b.A - a.A) * t)
        );
    }

    private float CalculateDistance((float X, float Y) p1, (float X, float Y) p2)
    {
        float dx = p2.X - p1.X;
        float dy = p2.Y - p1.Y;
        return (float)Math.Sqrt(dx * dx + dy * dy);
    }
}