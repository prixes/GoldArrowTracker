// Copyright (c) GoldArrowTracker. Licensed under the GNU AFFERO GENERAL PUBLIC LICENSE v3.0

namespace Archery.Shared.Models;

/// <summary>
/// Represents a detected arrow on an archery target image.
/// </summary>
public class ArrowDetection
{
    /// <summary>
    /// Gets or sets the X coordinate of the arrow center in the original image.
    /// </summary>
    public float CenterX { get; set; }

    /// <summary>
    /// Gets or sets the Y coordinate of the arrow center in the original image.
    /// </summary>
    public float CenterY { get; set; }

    /// <summary>
    /// Gets or sets the radius of the detected arrow hole (in pixels).
    /// </summary>
    public float Radius { get; set; }

    /// <summary>
    /// Gets or sets the confidence score of the detection (0.0 to 1.0).
    /// </summary>
    public float Confidence { get; set; }
}