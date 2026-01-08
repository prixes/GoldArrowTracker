// Copyright (c) GoldArrowTracker. Licensed under the GNU AFFERO GENERAL PUBLIC LICENSE v3.0

namespace Archery.Shared.Models;

/// <summary>
/// Represents a single detection output from YOLO model.
/// </summary>
public class YoloDetection
{
    /// <summary>
    /// Gets or sets the class ID (0-10 for arrows, special value for target).
    /// </summary>
    public int ClassId { get; set; }

    /// <summary>
    /// Gets or sets the class name (e.g., "0", "2", "target", etc.).
    /// </summary>
    public string ClassName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the confidence score (0.0 to 1.0).
    /// </summary>
    public float Confidence { get; set; }

    /// <summary>
    /// Gets or sets the bounding box X coordinate (center).
    /// </summary>
    public float X { get; set; }

    /// <summary>
    /// Gets or sets the bounding box Y coordinate (center).
    /// </summary>
    public float Y { get; set; }

    /// <summary>
    /// Gets or sets the bounding box width.
    /// </summary>
    public float Width { get; set; }

    /// <summary>
    /// Gets or sets the bounding box height.
    /// </summary>
    public float Height { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this is a miss (class 0).
    /// </summary>
    public bool IsMiss => ClassId == 0;

    /// <summary>
    /// Gets or sets a value indicating whether this is the target face detection.
    /// </summary>
    public bool IsTargetFace => ClassName == "target";
}
