// Copyright (c) GoldArrowTracker. Licensed under the GNU AFFERO GENERAL PUBLIC LICENSE v3.0

namespace Archery.Shared.Services;

using Archery.Shared.Models;

/// <summary>
/// Service interface for scoring archery targets from images.
/// </summary>
public interface ITargetScoringService
{
    /// <summary>
    /// Analyzes an archery target image and detects arrows, calculating score.
    /// </summary>
    /// <param name="imageData">Raw image bytes (JPEG/PNG).</param>
    /// <returns>Analysis result with detected arrows and total score.</returns>
    Task<TargetAnalysisResult> AnalyzeTargetImageAsync(byte[] imageData);

    /// <summary>
    /// Calculates the score for a detected arrow based on its distance from target center.
    /// Standard Olympic archery scoring: 10 rings, outer ring = 1 point, inner ring = 10 points.
    /// </summary>
    /// <param name="distanceFromCenter">Distance in pixels from target center.</param>
    /// <param name="targetRadius">Radius of the target in pixels.</param>
    /// <returns>Points awarded (0-10).</returns>
    int CalculateArrowScore(float distanceFromCenter, float targetRadius);
}