// Copyright (c) GoldArrowTracker. Licensed under the GNU AFFERO GENERAL PUBLIC LICENSE v3.0

namespace Archery.Shared.Services;

using Archery.Shared.Models;

/// <summary>
/// Interface for YOLO inference service.
/// </summary>
public interface IYoloInferenceService : IDisposable
{
    /// <summary>
    /// Gets the YOLO configuration used by the service.
    /// </summary>
    YoloConfig Config { get; }

    /// <summary>
    /// Runs YOLO inference on an image and returns detections.
    /// </summary>
    List<YoloDetection> Predict(byte[] imageBytes);
}
