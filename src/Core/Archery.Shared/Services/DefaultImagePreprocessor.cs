// Copyright (c) GoldArrowTracker. Licensed under the GNU AFFERO GENERAL PUBLIC LICENSE v3.0

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Archery.Shared.Services;

/// <summary>
/// Cross-platform implementation of IImagePreprocessor using SixLabors.ImageSharp.
/// Used as a fallback or for non-Android platforms.
/// </summary>
public class DefaultImagePreprocessor : IImagePreprocessor
{
    public (DenseTensor<float> Tensor, int OriginalWidth, int OriginalHeight, float ScaleX, float ScaleY) Preprocess(byte[] imageBytes, int inputSize, string? filePath = null)
    {
        using var imageStream = new MemoryStream(imageBytes);
        using var image = Image.Load<Rgb24>(imageStream);

        var originalWidth = image.Width;
        var originalHeight = image.Height;

        // Resize image to input size while maintaining aspect ratio (centralized logic)
        image.Mutate(x => x.Resize(new ResizeOptions
        {
            Size = new Size(inputSize, inputSize),
            Mode = ResizeMode.Pad,
            PadColor = Color.Black
        }));

        // Calculate scale factors for post-processing
        float scaleX = (float)originalWidth / inputSize;
        float scaleY = (float)originalHeight / inputSize;

        // Create tensor (1, 3, H, W)
        var tensor = new DenseTensor<float>(new[] { 1, 3, inputSize, inputSize });

        // Copy pixel data and normalize to [0, 1]
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

        return (tensor, originalWidth, originalHeight, scaleX, scaleY);
    }
}
