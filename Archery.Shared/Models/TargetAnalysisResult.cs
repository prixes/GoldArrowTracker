// Copyright (c) GoldArrowTracker. Licensed under the GNU AFFERO GENERAL PUBLIC LICENSE v3.0

namespace Archery.Shared.Models;

/// <summary>
/// Represents the analysis status.
/// </summary>
public enum AnalysisStatus
{
    Success,
    Failure
}

/// <summary>
/// Represents the result of analyzing an archery target image.
/// </summary>
public class TargetAnalysisResult
{
    /// <summary>
    /// Gets or sets the status of the analysis.
    /// </summary>
    public AnalysisStatus Status { get; set; }

    /// <summary>
    /// Gets or sets an error message if the analysis failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Gets or sets the detected arrows on the target.
    /// </summary>
    public List<ArrowDetection> DetectedArrows { get; set; } = new();

    /// <summary>
    /// Gets or sets the total score calculated from arrow positions.
    /// </summary>
    public int TotalScore { get; set; }

    /// <summary>
    /// Gets or sets the detected center of the target (in pixels from image top-left).
    /// </summary>
    public (float X, float Y) TargetCenter { get; set; }

    /// <summary>
    /// Gets or sets the estimated radius of the target in pixels.
    /// </summary>
    public float TargetRadius { get; set; }

    /// <summary>
    /// Gets or sets details about each arrow's score breakdown.
    /// </summary>
    public List<ArrowScore> ArrowScores { get; set; } = new();

    /// <summary>
    /// Gets or sets all raw YOLO detections found in the image.
    /// </summary>
    public List<YoloDetection> Detections { get; set; } = new();
}