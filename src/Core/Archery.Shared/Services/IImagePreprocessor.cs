// Copyright (c) GoldArrowTracker. Licensed under the GNU AFFERO GENERAL PUBLIC LICENSE v3.0

using Microsoft.ML.OnnxRuntime.Tensors;

namespace Archery.Shared.Services;

/// <summary>
/// Interface for image preprocessing (Resizing, Normalization, Tensorization).
/// Allows platform-specific optimizations (e.g., native Android Bitmap APIs).
/// </summary>
public interface IImagePreprocessor
{
    /// <summary>
    /// Processes image bytes into a normalized tensor ready for ONNX inference.
    /// </summary>
    /// <param name="imageBytes">Raw image data (JPEG/PNG).</param>
    /// <param name="inputSize">Target dimension (typically 640).</param>
    /// <param name="filePath">Optional path to the image file (for metadata optimization).</param>
    /// <returns>Preprocessed tensor and scaling metadata for post-processing.</returns>
    (DenseTensor<float> Tensor, int OriginalWidth, int OriginalHeight, float ScaleX, float ScaleY) Preprocess(byte[] imageBytes, int inputSize, string? filePath = null);
}
