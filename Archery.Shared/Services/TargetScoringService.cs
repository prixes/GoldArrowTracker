// Copyright (c) GoldArrowTracker. Licensed under the GNU AFFERO GENERAL PUBLIC LICENSE v3.0

namespace Archery.Shared.Services;

using Archery.Shared.Models;

/// <summary>
/// On-device implementation of target scoring using Object Detection.
/// Detects arrows and target face, then calculates archery scores.
/// </summary>
public class TargetScoringService : ITargetScoringService
{
    private readonly IObjectDetectionService _objectDetectionService;

    /// <summary>
    /// Initializes a new instance of the TargetScoringService.
    /// </summary>
    /// <param name="objectDetectionService">The Object Detection service implementation.</param>
    public TargetScoringService(IObjectDetectionService objectDetectionService)
    {
        _objectDetectionService = objectDetectionService;
    }

    /// <summary>
    /// Analyzes an archery target image using Object Detection.
    /// Detects the target face and all arrows, then scores each arrow.
    /// </summary>
    public async Task<TargetAnalysisResult> AnalyzeTargetImageAsync(byte[] imageData, string? filePath = null)
    {
        if (imageData == null || imageData.Length == 0)
        {
            return new TargetAnalysisResult 
            { 
                Status = AnalysisStatus.Failure, 
                ErrorMessage = "Image data cannot be null or empty." 
            };
        }

        return await Task.Run(() =>
        {
            // 1. Run inference
            var detections = _objectDetectionService.Predict(imageData, filePath);
            
            var result = new TargetAnalysisResult 
            {
                Detections = detections.ToList() // Populate all detections regardless of success
            };
            
            if (detections.Count == 0)
            {
                result.Status = AnalysisStatus.Failure;
                result.ErrorMessage = "Object Detection model did not detect any objects in the image. Ensure the image contains a clear archery target.";
                return result;
            }

            // 2. Find target face detection
            var targetDetection = detections.FirstOrDefault(d => d.IsTargetFace);
            if (targetDetection == null)
            {
                var detectionSummary = string.Join(", ", detections.Select(d => $"{d.ClassName}({d.Confidence:P})"));
                result.Status = AnalysisStatus.Failure;
                result.ErrorMessage = $"Could not detect archery target in image. Found objects: {detectionSummary}. Ensure the image shows a clear archery target face.";
                return result;
            }

            // 3. Extract target center and radii
            result.TargetCenterX = targetDetection.X;
            result.TargetCenterY = targetDetection.Y;
            result.TargetRadius = targetDetection.Width / 2f;
            result.TargetRadiusY = targetDetection.Height / 2f;
            result.Status = AnalysisStatus.Success;

            // 4. Filter and process arrow detections
            var arrowDetections = detections
                .Where(d => !d.IsTargetFace && !d.IsMiss)  // Get actual arrow detections
                .OrderByDescending(d => d.Confidence)      // Sort by confidence
                .ToList();

            // 5. Populate DetectedArrows (already part of result)
            // 6. Score each arrow
            foreach (var detection in arrowDetections)
            {
                var arrowDetection = new ArrowDetection
                {
                    CenterX = detection.X,
                    CenterY = detection.Y,
                    Radius = Math.Min(detection.Width, detection.Height) / 2f,
                    Confidence = detection.Confidence
                };

                result.DetectedArrows.Add(arrowDetection);

                // Calculate elliptical normalized distance for perspective awareness
                float dx = arrowDetection.CenterX - result.TargetCenterX;
                float dy = arrowDetection.CenterY - result.TargetCenterY;
                
                // Normalized distance: r = sqrt((dx/rx)^2 + (dy/ry)^2)
                // This "flattens" the perspective distortion.
                float normX = dx / result.TargetRadius;
                float normY = dy / result.TargetRadiusY;
                float normalizedDistance = (float)Math.Sqrt(normX * normX + normY * normY);

                var geoPoints = this.CalculateScoreFromNormalizedDistance(normalizedDistance);
                var ring = this.DetermineRingFromNormalizedDistance(normalizedDistance);

                // SCORES PRIORITY: Trust the AI model's classification over geometry for 1-8.
                // The geometry can be finicky with perspective/radius, while the AI is trained to recognize the ring.
                // Exception: The model likely classifies Gold as '9'. We use geometry to upgrade 9 to 10.
                
                int finalPoints;
                
                // Parse class ID from detection (assuming ClassId maps directly to points for 0-9)
                // Config map: 0->0, ..., 9->9, 10->target, 11->10
                // Since model outputs 0-10, max point class is 9.
                int modelPoints = detection.ClassId;
                
                // Sanity check: if class is "target" (10) or weird, ignore model
                if (modelPoints >= 10) 
                {
                    finalPoints = geoPoints;
                }
                else
                {
                    finalPoints = modelPoints;
                    
                    // Allow geometry to upgrade a '9' to a '10' (Inner 10)
                    if (modelPoints == 9 && geoPoints == 10)
                    {
                        finalPoints = 10;
                    }
                    
                    // Fallback: If model says '0' (Miss) but geometry says hit? 
                    // Usually trust model (it might be a miss on the paper). 
                }

                result.ArrowScores.Add(new ArrowScore
                {
                    Detection = arrowDetection,
                    DistanceFromCenter = (float)Math.Sqrt(dx * dx + dy * dy), // Real pixel distance
                    Points = finalPoints,
                    Ring = finalPoints // Use points as ring for simplicity, or keep geo ring? User cares about points.
                });

                result.TotalScore += finalPoints;
            }
            return result;
        });
    }

    /// <summary>
    /// Calculates score based on distance from center using Olympic scoring (10 rings).
    /// Ring 1 (outermost) = 1 point, Ring 10 (innermost) = 10 points.
    /// </summary>
    public int CalculateArrowScore(float distanceFromCenter, float targetRadius)
    {
        if (targetRadius <= 0)
        {
            return 0;
        }

        // Normalize distance as a fraction of target radius (0.0 to 1.0+)
        var normalizedDistance = distanceFromCenter / targetRadius;

        // Outside target = 0 points
        if (normalizedDistance >= 1.0f)
        {
            return 0;
        }

        // Calculate score: 10 points for 0-0.1 radius, 9 for 0.1-0.2, etc.
        // Epsilon shift for "higher score on line" rule: touching the line = higher points.
        var score = 10 - (int)System.Math.Floor(System.Math.Max(0, normalizedDistance - 0.00001f) * 10);

        return System.Math.Max(0, score); // Ensure score is not negative
    }

    /// <summary>
    /// Determines which ring (1-10) an arrow hit, based on distance from center.
    /// 0 indicates a miss (outside the target).
    /// </summary>
    private int DetermineRing(float distanceFromCenter, float targetRadius)
    {
        if (targetRadius <= 0)
        {
            return 0;
        }

        var normalizedDistance = distanceFromCenter / targetRadius;

        if (normalizedDistance >= 1.0f)
        {
            return 0; // Miss
        }

        // Determine ring based on 0.1 segments, 10 being innermost, 1 outermost.
        // normalizedDistance 0.0-0.1 -> score 10 -> ring 10
        // normalizedDistance 0.1-0.2 -> score 9 -> ring 9
        // Epsilon shift for "higher score on line" rule.
        return 10 - (int)System.Math.Floor(System.Math.Max(0, normalizedDistance - 0.00001f) * 10);
    }

    /// <summary>
    /// Calculates score based on normalized distance (0.0 to 1.0) using Olympic scoring.
    /// </summary>
    public int CalculateScoreFromNormalizedDistance(float normalizedDistance)
    {
        if (normalizedDistance >= 1.0f) return 0;
        var score = 10 - (int)System.Math.Floor(System.Math.Max(0, normalizedDistance - 0.00001f) * 10);
        return System.Math.Max(0, score);
    }

    /// <summary>
    /// Determines which ring (1-10) an arrow hit based on normalized distance.
    /// </summary>
    public int DetermineRingFromNormalizedDistance(float normalizedDistance)
    {
        if (normalizedDistance >= 1.0f) return 0;
        return 10 - (int)System.Math.Floor(System.Math.Max(0, normalizedDistance - 0.00001f) * 10);
    }

    /// <summary>
    /// Calculates Euclidean distance between two points.
    /// </summary>
    private float CalculateDistance(float x1, float y1, float x2, float y2)
    {
        var dx = x2 - x1;
        var dy = y2 - y1;
        return (float)System.Math.Sqrt((dx * dx) + (dy * dy));
    }
}