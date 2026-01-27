// Copyright (c) GoldArrowTracker. Licensed under the GNU AFFERO GENERAL PUBLIC LICENSE v3.0

using GoldTracker.Shared.UI.Services.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using Archery.Shared.Models;
using Archery.Shared.Services;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.Fonts;

namespace GoldTracker.Web.Services;

/// <summary>
/// Browser implementation of image processing using ImageSharp.
/// </summary>
public class BrowserImageService : IPlatformImageService
{
    private readonly ITargetScoringService _targetScoringService;
    private readonly ObjectDetectionConfig _objectDetectionConfig;

    public BrowserImageService(ITargetScoringService targetScoringService, ObjectDetectionConfig objectDetectionConfig)
    {
        _targetScoringService = targetScoringService;
        _objectDetectionConfig = objectDetectionConfig;
    }

    public bool IsModelAvailable => _targetScoringService != null;
    public ObjectDetectionConfig ObjectDetectionConfig => _objectDetectionConfig;

    public async Task<(int Width, int Height)> GetImageDimensionsAsync(byte[] imageBytes)
    {
        try
        {
            return await Task.Run(() =>
            {
                var info = Image.Identify(imageBytes);
                return info != null ? (info.Width, info.Height) : (1024, 1024);
            });
        }
        catch
        {
            return (1024, 1024);
        }
    }

    public async Task<byte[]> CropImageAsync(byte[] imageBytes, double startXNorm, double startYNorm, double widthNorm, double heightNorm)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var image = Image.Load<Rgba32>(imageBytes);
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

                using var ms = new MemoryStream();
                image.SaveAsJpeg(ms);
                return ms.ToArray();
            }
            catch
            {
                return Array.Empty<byte>();
            }
        });
    }

    public async Task<byte[]> ResizeImageAsync(byte[] imageBytes, int maxDimension, int quality = 80)
    {
        return await ResizeImageAsync(imageBytes, maxDimension, quality, null);
    }

    public async Task<byte[]> ResizeImageAsync(byte[] imageBytes, int maxDimension, int quality, string? filePath)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var image = Image.Load(imageBytes);

                // Calculate new dimensions
                var ratio = Math.Min((double)maxDimension / image.Width, (double)maxDimension / image.Height);

                if (ratio >= 1.0) return imageBytes; // No resize needed

                int newWidth = (int)(image.Width * ratio);
                int newHeight = (int)(image.Height * ratio);

                image.Mutate(x => x
                    .AutoOrient()
                    .Resize(new ResizeOptions
                    {
                        Size = new Size(newWidth, newHeight),
                        Mode = ResizeMode.Max
                    }));

                using var ms = new MemoryStream();
                var encoder = new JpegEncoder { Quality = quality };
                image.SaveAsJpeg(ms, encoder);
                return ms.ToArray();
            }
            catch
            {
                return imageBytes; // Return original on error
            }
        });
    }

    public Task<string> ResizeImageToBase64Async(string imagePath, int maxDimension)
    {
        // In browser/WASM, direct file path access is limited.
        // This is a stub implementation.
        return Task.FromResult(string.Empty);
    }

    public string ImageToBase64(byte[] imageBytes)
    {
        return Convert.ToBase64String(imageBytes);
    }

    public byte[] Base64ToImage(string base64Data)
    {
        return Convert.FromBase64String(base64Data);
    }

    public async Task<TargetAnalysisResult> AnalyzeTargetFromBytesAsync(byte[] imageBytes, string? filePath = null)
    {
        // In browser, filePath might not be relevant or accessible, passing logic to scoring service
        if (_targetScoringService == null)
        {
             return new TargetAnalysisResult 
             { 
                 Status = AnalysisStatus.Failure, 
                 ErrorMessage = "Scoring service not available" 
             };
        }
        return await _targetScoringService.AnalyzeTargetImageAsync(imageBytes, filePath);
    }

    public async Task<string> DrawDetectionsOnImageAsync(byte[] imageBytes, TargetAnalysisResult analysisResult, int originalWidth, int originalHeight)
    {
         return await Task.Run(() => {
            try
            {
                using var image = Image.Load<Rgba32>(imageBytes);
                // Scale factor if analyzed image was different size
                // But typically we draw on the display image.
                
                // Simple drawing implementation
                foreach (var detection in analysisResult.Detections)
                {
                    var rect = new RectangleF(detection.X - detection.Width / 2, detection.Y - detection.Height / 2, detection.Width, detection.Height);
                    
                    var color = detection.ClassId == 10 ? Color.Purple : Color.Red;
                    // Draw bounding box
                    image.Mutate(ctx => ctx.Draw(color, 2, rect));
                }

                using var ms = new MemoryStream();
                image.SaveAsJpeg(ms);
                return Convert.ToBase64String(ms.ToArray());
            }
            catch
            {
                return Convert.ToBase64String(imageBytes);
            }
        });
    }
}
