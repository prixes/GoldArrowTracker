// Copyright (c) GoldArrowTracker. Licensed under the GNU AFFERO GENERAL PUBLIC LICENSE v3.0

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Archery.Shared.Services;
using Archery.Shared.Models;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing;
using GoldTracker.Shared.UI.Services.Abstractions;

namespace GoldTracker.Mobile.Services;

/// <summary>
/// Service for processing images and performing archery target analysis.
/// Includes Object Detection-based target detection for arrow scoring.
/// </summary>
public class ImageProcessingService : IPlatformImageService
{
    private readonly ITargetScoringService? _targetScoringService;
    private readonly ObjectDetectionConfig _objectDetectionConfig;
    
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
        return await _targetScoringService.AnalyzeTargetImageAsync(imageBytes, imagePath);
    }

    /// <summary>
    /// Analyzes raw image bytes for archery target detection.
    /// </summary>
    public async Task<TargetAnalysisResult> AnalyzeTargetFromBytesAsync(byte[] imageBytes, string? filePath = null)
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
            throw new ArgumentException("Image bytes cannot be null or empty.");
        }

        return await _targetScoringService.AnalyzeTargetImageAsync(imageBytes, filePath);
    }

    /// <summary>
    /// Draws the analysis results onto the image (interface implementation).
    /// </summary>
    public async Task<string> DrawDetectionsOnImageAsync(byte[] imageBytes, TargetAnalysisResult analysisResult, int originalWidth, int originalHeight)
    {
        // Call the actual implementation with nullable parameters (cast to int? to call the correct overload)
        return await DrawDetectionsOnImageAsync(imageBytes, analysisResult, (int?)originalWidth, (int?)originalHeight);
    }

    /// <summary>
    /// Draws the analysis results onto the image.
    /// </summary>
    public async Task<string> DrawDetectionsOnImageAsync(byte[] imageBytes, TargetAnalysisResult analysisResult, int? sourceWidth = null, int? sourceHeight = null)
    {
#if ANDROID
        // Run on background thread to avoid blocking UI
        return await Task.Run(async () =>
        {
            var annotatedBytes = await GoldTracker.Mobile.Platforms.Android.AndroidImageProcessor.DrawDetectionsAsync(imageBytes, analysisResult, sourceWidth, sourceHeight);
            return Convert.ToBase64String(annotatedBytes);
        });
#else
        // Fallback for non-Android platforms (e.g. Simulator/Windows)
        return await Task.Run(async () => {
            try 
            {
                using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(imageBytes);

                if (analysisResult.Status != AnalysisStatus.Success)
                {
                    return Convert.ToBase64String(imageBytes);
                }

                // If non-android drawing logic is needed with scaling, it would be implemented here.
                
                // Simplify fallback drawing for non-Android
                foreach (var detection in analysisResult.ArrowScores)
                {
                    float x = detection.Detection.CenterX;
                    float y = detection.Detection.CenterY;
                    image.Mutate(ctx => ctx.Draw(SixLabors.ImageSharp.Color.Red, 2, new EllipsePolygon(x, y, 10)));
                }
                
                using var ms = new System.IO.MemoryStream();
                await image.SaveAsJpegAsync(ms);
                return Convert.ToBase64String(ms.ToArray());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ImageProcessingService] DrawDetections Fallback Error: {ex.Message}");
                return Convert.ToBase64String(imageBytes);
            }
        });
#endif
    }

    /// <summary>
    /// Gets the dimensions of an image from its bytes.
    /// </summary>
    public async Task<(int Width, int Height)> GetImageDimensionsAsync(byte[] imageBytes)
    {
        if (imageBytes == null || imageBytes.Length == 0) return (1024, 1024);
#if ANDROID
        return await Task.Run(() => {
            var options = new global::Android.Graphics.BitmapFactory.Options { InJustDecodeBounds = true };
            global::Android.Graphics.BitmapFactory.DecodeByteArray(imageBytes, 0, imageBytes.Length, options);
            
            int width = options.OutWidth;
            int height = options.OutHeight;

            // Check EXIF rotation to correctly report dimensions as they appear in UI
            if (imageBytes.Length > 2 && imageBytes[0] == 0xFF && imageBytes[1] == 0xD8)
            {
                try
                {
                    using var stream = new MemoryStream(imageBytes);
                    var exif = new global::Android.Media.ExifInterface(stream);
                    var orientation = exif.GetAttributeInt(global::Android.Media.ExifInterface.TagOrientation, 1);
                    
                    // 6: 90 deg, 8: 270 deg, 5: 90 deg + flip, 7: 270 deg + flip
                    if (orientation == 6 || orientation == 8 || orientation == 5 || orientation == 7)
                    {
                        return (height, width);
                    }
                }
                catch { /* Ignore EXIF errors */ }
            }

            return (width, height);
        });
#else
        try
        {
            return await Task.Run(() => {
                var info = SixLabors.ImageSharp.Image.Identify(imageBytes);
                return info != null ? (info.Width, info.Height) : (1024, 1024);
            });
        }
        catch { return (1024, 1024); }
#endif
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
    public async Task<byte[]> CropImageAsync(byte[] imageBytes, double startXNorm, double startYNorm, double widthNorm, double heightNorm, string? filePath = null)
    {
#if ANDROID
        // Pass filePath to LoadBitmapAsync to ensure EXIF rotation is robust
        using var bitmap = await GoldTracker.Mobile.Platforms.Android.AndroidImageProcessor.LoadBitmapAsync(imageBytes, filePath);
        if (bitmap == null) return Array.Empty<byte>();
        return await GoldTracker.Mobile.Platforms.Android.AndroidImageProcessor.CropBitmapAsync(bitmap, startXNorm, startYNorm, widthNorm, heightNorm);
#else
        return await Task.Run(async () => {
            try 
            {
                using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(imageBytes);
                image.Mutate(x => x.AutoOrient());
                
                int x = (int)(startXNorm * image.Width);
                int y = (int)(startYNorm * image.Height);
                int w = (int)(widthNorm * image.Width);
                int h = (int)(heightNorm * image.Height);

                // Ensure bounds
                x = Math.Max(0, x);
                y = Math.Max(0, y);
                w = Math.Min(w, image.Width - x);
                h = Math.Min(h, image.Height - y);

                if (w <= 0 || h <= 0) return Array.Empty<byte>();

                image.Mutate(ctx => ctx.Crop(new Rectangle(x, y, w, h)));

                using var ms = new System.IO.MemoryStream();
                await image.SaveAsJpegAsync(ms);
                return ms.ToArray();
            }
            catch { return Array.Empty<byte>(); }
        });
#endif
    }

    /// <summary>
    /// Resizes an image file to a base64 string with a maximum dimension.
    /// Good for displaying thumbnails or previews.
    /// </summary>
    public async Task<string> ResizeImageToBase64Async(string imagePath, int maxDimension)
    {
        try
        {
            var bytes = await System.IO.File.ReadAllBytesAsync(imagePath);
            var resizedBytes = await ResizeImageAsync(bytes, maxDimension, 80, imagePath);
            return Convert.ToBase64String(resizedBytes);
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Resizes image bytes to a maximum dimension.
    /// </summary>
    public async Task<byte[]> ResizeImageAsync(byte[] imageBytes, int maxDimension, int quality, string? filePath = null)
    {
#if ANDROID
        return await GoldTracker.Mobile.Platforms.Android.AndroidImageProcessor.ResizeImageAsync(imageBytes, maxDimension, quality, filePath);
#else
        return await Task.Run(() =>
        {
            try
            {
                using var image = SixLabors.ImageSharp.Image.Load(imageBytes);
                
                // Calculate new dimensions
                var ratio = Math.Min((double)maxDimension / image.Width, (double)maxDimension / image.Height);
                
                if (ratio >= 1.0) return imageBytes; // No resize needed
                
                int newWidth = (int)(image.Width * ratio);
                int newHeight = (int)(image.Height * ratio);
                
                image.Mutate(x => x
                    .AutoOrient()
                    .Resize(newResizeOptions(newWidth, newHeight)));
                
                using var ms = new System.IO.MemoryStream();
                image.SaveAsJpeg(ms);
                return ms.ToArray();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ImageProcessing] Error resizing: {ex.Message}");
                return imageBytes; // Return original on error
            }
        });
#endif
    }

    private ResizeOptions newResizeOptions(int w, int h) => new ResizeOptions
    {
        Size = new SixLabors.ImageSharp.Size(w, h),
        Mode = SixLabors.ImageSharp.Processing.ResizeMode.Max
    };

    /// <summary>
    /// Crops an image based on normalized coordinates (0-1) using a pre-loaded Image object.
    /// This is much more efficient when performing multiple crops on the same source image.
    /// </summary>
    public async Task<byte[]> CropImageAsync(Image<Rgba32> sourceImage, double startXNorm, double startYNorm, double widthNorm, double heightNorm)
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
        using var croppedImage = sourceImage.Clone(ctx => ctx.Crop(new Rectangle(x, y, w, h)));

        using var ms = new System.IO.MemoryStream();
        // Use quality 60 for faster encoding (was using default 75)
        var encoder = new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder { Quality = 60 };
        await croppedImage.SaveAsJpegAsync(ms, encoder);
        return ms.ToArray();
    }


    public string ImageToBase64(byte[] imageBytes)
    {
        return Convert.ToBase64String(imageBytes);
    }

    public byte[] Base64ToImage(string base64Data)
    {
        return Convert.FromBase64String(base64Data);
    }


    public async Task<byte[]> ResizeImageAsync(byte[] imageBytes, int maxDimension, int quality = 80)
    {
        return await ResizeImageAsync(imageBytes, maxDimension, quality, null);
    }

    public async Task<byte[]> LoadImageBytesAsync(string path, Guid? sessionId = null)
    {
        try
        {
            if (System.IO.File.Exists(path))
            {
                return await System.IO.File.ReadAllBytesAsync(path);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ImageProcessingService] Failed to load image: {ex.Message}");
        }
        return Array.Empty<byte>();
    }
    public async Task<string> GetImageDisplaySourceAsync(byte[] imageBytes, TargetAnalysisResult? analysisResult = null, string? filePath = null)
    {
        // On Mobile, we resize to 1024 for display and optionally burn in detections
        // to save memory and avoid complex canvas logic in simple views.
        // Pass filePath to ResizeImageAsync to ensure EXIF rotation is robust (reading from file header).
        var displayBytes = await ResizeImageAsync(imageBytes, 1024, 80, filePath);
        
        if (analysisResult != null)
        {
            var base64 = await DrawDetectionsOnImageAsync(displayBytes, analysisResult, null, null);
            return $"data:image/jpeg;base64,{base64}";
        }
        
        return $"data:image/jpeg;base64,{ImageToBase64(displayBytes)}";
    }
}