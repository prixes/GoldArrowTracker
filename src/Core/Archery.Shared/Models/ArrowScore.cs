// Copyright (c) GoldArrowTracker. Licensed under the GNU AFFERO GENERAL PUBLIC LICENSE v3.0

namespace Archery.Shared.Models;

/// <summary>
/// Represents the scored points for a single detected arrow.
/// </summary>
public class ArrowScore
{
    /// <summary>
    /// Gets or sets the detected arrow information.
    /// </summary>
    public ArrowDetection Detection { get; set; } = new();

    /// <summary>
    /// Gets or sets the points awarded for this arrow.
    /// </summary>
    public int Points { get; set; }

    /// <summary>
    /// Gets or sets the ring the arrow hit (1=inner gold, 10=outer white, 0=miss).
    /// </summary>
    public int Ring { get; set; }

    /// <summary>
    /// Gets or sets the distance from arrow center to target center (in pixels).
    /// </summary>
    public float DistanceFromCenter { get; set; }
}