// Copyright (c) GoldArrowTracker. Licensed under the GNU AFFERO GENERAL PUBLIC LICENSE v3.0

namespace Archery.Shared.Services;

using Archery.Shared.Models;

/// <summary>
/// Interface for Object Detection inference service.
/// </summary>
public interface IObjectDetectionService : IDisposable
{
    /// <summary>
    /// Gets the configuration used by the service.
    /// </summary>
    ObjectDetectionConfig Config { get; }

    /// <summary>
    /// Runs inference on an image and returns detections.
    /// </summary>
    List<ObjectDetectionResult> Predict(byte[] imageBytes);
}
