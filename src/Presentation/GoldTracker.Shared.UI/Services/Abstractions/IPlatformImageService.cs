// Copyright (c) GoldArrowTracker. Licensed under the GNU AFFERO GENERAL PUBLIC LICENSE v3.0

using Archery.Shared.Models;

namespace GoldTracker.Shared.UI.Services.Abstractions;

/// <summary>
/// Platform-agnostic interface for image processing operations.
/// Implementations: Mobile uses Android native APIs where possible, Web uses ImageSharp.
/// </summary>
public interface IPlatformImageService
{
    /// <summary>
    /// Gets whether the object detection model is available.
    /// </summary>
    bool IsModelAvailable { get; }

    /// <summary>
    /// Gets the object detection configuration.
    /// </summary>
    ObjectDetectionConfig ObjectDetectionConfig { get; }

    /// <summary>
    /// Gets the dimensions of an image from its bytes.
    /// </summary>
    Task<(int Width, int Height)> GetImageDimensionsAsync(byte[] imageBytes);

    /// <summary>
    /// Crops an image based on normalized coordinates (0-1).
    /// </summary>
    Task<byte[]> CropImageAsync(byte[] imageBytes, double startXNorm, double startYNorm, double widthNorm, double heightNorm);

    /// <summary>
    /// Resizes image bytes to a maximum dimension.
    /// </summary>
    Task<byte[]> ResizeImageAsync(byte[] imageBytes, int maxDimension, int quality = 80);

    /// <summary>
    /// Resizes image bytes to a maximum dimension (overload with file path for EXIF handling).
    /// </summary>
    Task<byte[]> ResizeImageAsync(byte[] imageBytes, int maxDimension, int quality, string? filePath);

    /// <summary>
    /// Resizes an image from a file path and returns as base64 string.
    /// </summary>
    Task<string> ResizeImageToBase64Async(string imagePath, int maxDimension);

    /// <summary>
    /// Converts image bytes to base64 string for display.
    /// </summary>
    string ImageToBase64(byte[] imageBytes);

    /// <summary>
    /// Converts base64 string to image bytes.
    /// </summary>
    byte[] Base64ToImage(string base64Data);

    /// <summary>
    /// Analyzes a target image and returns detection results.
    /// </summary>
    Task<TargetAnalysisResult> AnalyzeTargetFromBytesAsync(byte[] imageBytes, string? filePath = null);

    /// <summary>
    /// Draws detection boxes and labels on an image.
    /// </summary>
    /// <param name="imageBytes">Original image bytes</param>
    /// <param name="result">Detection results to draw</param>
    /// <param name="originalWidth">Original image width</param>
    /// <param name="originalHeight">Original image height</param>
    /// <returns>Base64 encoded annotated image</returns>
    Task<string> DrawDetectionsOnImageAsync(byte[] imageBytes, TargetAnalysisResult result, int originalWidth, int originalHeight);

    /// <summary>
    /// Loads image bytes from a path (local or remote depending on platform).
    /// </summary>
    Task<byte[]> LoadImageBytesAsync(string path, Guid? sessionId = null);

    /// <summary>
    /// Prepares an image for display, optionally with detections burned in.
    /// This allows platforms to optimize (e.g., Web uses native canvas, Mobile burns in).
    /// </summary>
    Task<string> GetImageDisplaySourceAsync(byte[] imageBytes, TargetAnalysisResult? analysisResult = null);
}
