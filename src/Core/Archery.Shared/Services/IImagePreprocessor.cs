// Copyright (c) GoldArrowTracker. Licensed under the GNU AFFERO GENERAL PUBLIC LICENSE v3.0

using Microsoft.ML.OnnxRuntime.Tensors;

namespace Archery.Shared.Services;

/// <summary>
/// Interface for image preprocessing (Resizing, Normalization, Tensorization).
/// Allows platform-specific optimizations (e.g., native Android Bitmap APIs).
/// </summary>
public record PreprocessingResult(
    DenseTensor<float> Tensor,
    int OriginalWidth,
    int OriginalHeight,
    float Scale,
    float PadX,
    float PadY
);

public interface IImagePreprocessor
{
    /// <summary>
    /// Processes image bytes into a normalized tensor ready for ONNX inference.
    /// Returns the tensor and metadata for post-processing coordinates.
    /// </summary>
    PreprocessingResult Preprocess(byte[] imageBytes, int inputSize, string? filePath = null);
}
