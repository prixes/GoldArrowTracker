// Copyright (c) GoldArrowTracker. Licensed under the GNU AFFERO GENERAL PUBLIC LICENSE v3.0

namespace GoldTracker.Mobile.Services;

using Archery.Shared.Services;

/// <summary>
/// Service for image utilities and conversions.
/// </summary>
public class ImageUtilityService
{
    /// <summary>
    /// Converts image file to byte array.
    /// </summary>
    public async Task<byte[]> ImageFileToByteArrayAsync(string imagePath)
    {
        if (!File.Exists(imagePath))
        {
            throw new FileNotFoundException($"Image file not found: {imagePath}");
        }

        return await File.ReadAllBytesAsync(imagePath);
    }

    /// <summary>
    /// Saves byte array as image file.
    /// </summary>
    public async Task<string> SaveByteArrayAsImageAsync(byte[] imageBytes, string fileName)
    {
        if (imageBytes == null || imageBytes.Length == 0)
        {
            throw new ArgumentException("Image bytes cannot be null or empty.", nameof(imageBytes));
        }

        var appDataPath = FileSystem.AppDataDirectory;
        var filePath = Path.Combine(appDataPath, fileName);

        // Create directory if it doesn't exist
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllBytesAsync(filePath, imageBytes);
        return filePath;
    }

    /// <summary>
    /// Converts image file to base64 string for display in Blazor.
    /// </summary>
    public async Task<string> ImageToBase64Async(string imagePath)
    {
        try
        {
            var bytes = await File.ReadAllBytesAsync(imagePath);
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
            var filePath = Path.Combine(appDataPath, fileName);

            // Create directory if it doesn't exist
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var imageBytes = Convert.FromBase64String(base64Data);
            await File.WriteAllBytesAsync(filePath, imageBytes);

            return filePath;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Gets image dimensions from file.
    /// </summary>
    public async Task<(int Width, int Height)> GetImageDimensionsAsync(string imagePath)
    {
        try
        {
            var bytes = await File.ReadAllBytesAsync(imagePath);
            return ObjectDetectionPreprocessingUtility.GetImageDimensions(bytes);
        }
        catch
        {
            return (0, 0);
        }
    }
}
