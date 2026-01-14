// Copyright (c) GoldArrowTracker. Licensed under the GNU AFFERO GENERAL PUBLIC LICENSE v3.0

namespace Archery.Shared.Services;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Microsoft.ML.OnnxRuntime.Tensors;

/// <summary>
/// Utilities for preprocessing images for Object Detection inference.
/// </summary>
public static class ObjectDetectionPreprocessingUtility
{
    /// <summary>
    /// Preprocesses image bytes into a normalized tensor suitable for inference.
    /// </summary>
    /// <param name="imageBytes">Raw image bytes (JPEG/PNG).</param>
    /// <param name="inputSize">Target input size (default 640).</param>
    /// <returns>Preprocessed tensor in (1, 3, H, W) format with normalized float values.</returns>
    public static (DenseTensor<float> Tensor, int OriginalWidth, int OriginalHeight, float ScaleX, float ScaleY) PreprocessImage(byte[] imageBytes, int inputSize = 640)
    {
        System.Diagnostics.Debug.WriteLine($"[ObjectDetectionPreprocessing] Input image size: {imageBytes.Length} bytes");
        
        using var imageStream = new MemoryStream(imageBytes);
        using var image = Image.Load<Rgb24>(imageStream);

        var originalWidth = image.Width;
        var originalHeight = image.Height;

        System.Diagnostics.Debug.WriteLine($"[ObjectDetectionPreprocessing] Original image dimensions: {originalWidth}x{originalHeight}");

        // Resize image to input size while maintaining aspect ratio
        image.Mutate(x => x.Resize(new ResizeOptions
        {
            Size = new Size(inputSize, inputSize),
            Mode = ResizeMode.Pad,
            PadColor = Color.Black
        }));

        System.Diagnostics.Debug.WriteLine($"[ObjectDetectionPreprocessing] Resized to: {inputSize}x{inputSize}");

        // Calculate scale factors for post-processing
        float scaleX = (float)originalWidth / inputSize;
        float scaleY = (float)originalHeight / inputSize;

        System.Diagnostics.Debug.WriteLine($"[ObjectDetectionPreprocessing] Scale factors: X={scaleX:F4}, Y={scaleY:F4}");

        // Create tensor (1, 3, H, W)
        var tensor = new DenseTensor<float>(new[] { 1, 3, inputSize, inputSize });

        // Copy pixel data and normalize to [0, 1]
        // Using ProcessPixelRows for much faster access than image[x, y]
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < inputSize; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < inputSize; x++)
                {
                    var pixel = row[x];
                    tensor[0, 0, y, x] = pixel.R / 255.0f;  // R channel
                    tensor[0, 1, y, x] = pixel.G / 255.0f;  // G channel
                    tensor[0, 2, y, x] = pixel.B / 255.0f;  // B channel
                }
            }
        });

        System.Diagnostics.Debug.WriteLine($"[ObjectDetectionPreprocessing] ? Tensor created: shape=[{string.Join(", ", tensor.Dimensions.ToArray())}]");

        return (tensor, originalWidth, originalHeight, scaleX, scaleY);
    }

    /// <summary>
    /// Converts image bytes to pixel array for analysis.
    /// </summary>
    public static byte[] ImageToPixelArray(byte[] imageBytes)
    {
        using var imageStream = new MemoryStream(imageBytes);
        using var image = Image.Load<Rgba32>(imageStream);

        var pixelArray = new byte[image.Width * image.Height * 4];
        image.CopyPixelDataTo(pixelArray);
        return pixelArray;
    }

    /// <summary>
    /// Gets image dimensions without fully loading the image.
    /// </summary>
    public static (int Width, int Height) GetImageDimensions(byte[] imageBytes)
    {
        using var imageStream = new MemoryStream(imageBytes);
        var imageInfo = Image.Identify(imageStream);
        return (imageInfo!.Width, imageInfo.Height);
    }
}
