// Copyright (c) GoldArrowTracker. Licensed under the GNU AFFERO GENERAL PUBLIC LICENSE v3.0
﻿
﻿namespace Archery.Shared.Services;
﻿
﻿using Archery.Shared.Models;
﻿
﻿/// <summary>
﻿/// On-device implementation of target scoring using YOLO object detection.
﻿/// Detects arrows and target face, then calculates archery scores.
﻿/// </summary>
﻿public class TargetScoringService : ITargetScoringService
﻿{
﻿        private readonly IYoloInferenceService _yoloService;
﻿    
﻿        /// <summary>
﻿        /// Initializes a new instance of the TargetScoringService.
﻿        /// </summary>
﻿        /// <param name="yoloService">The YOLO inference service implementation.</param>
﻿        public TargetScoringService(IYoloInferenceService yoloService)
﻿        {
﻿            _yoloService = yoloService;
﻿        }﻿
﻿    /// <summary>
﻿    /// Analyzes an archery target image using YOLO object detection.
﻿    /// Detects the target face and all arrows, then scores each arrow.
﻿    /// </summary>
﻿    public async Task<TargetAnalysisResult> AnalyzeTargetImageAsync(byte[] imageData)
﻿    {
﻿        if (imageData == null || imageData.Length == 0)
﻿        {
﻿            return new TargetAnalysisResult 
﻿            { 
﻿                Status = AnalysisStatus.Failure, 
﻿                ErrorMessage = "Image data cannot be null or empty." 
﻿            };
﻿        }
﻿
﻿        return await Task.Run(() =>
﻿        {
﻿                                                // 1. Run YOLO inference
﻿                                                var detections = _yoloService.Predict(imageData);
﻿                                                
﻿                                                var result = new TargetAnalysisResult 
﻿                                                {
﻿                                                    Detections = detections.ToList() // Populate all detections regardless of success
﻿                                                };
﻿                                                
﻿                                                if (detections.Count == 0)
﻿                                                {
﻿                                                    result.Status = AnalysisStatus.Failure;
﻿                                                    result.ErrorMessage = "YOLO model did not detect any objects in the image. Ensure the image contains a clear archery target.";
﻿                                                    return result;
﻿                                                }
﻿                                    
﻿                                                // 2. Find target face detection
﻿                                                var targetDetection = detections.FirstOrDefault(d => d.IsTargetFace);
﻿                                                if (targetDetection == null)
﻿                                                {
﻿                                                    var detectionSummary = string.Join(", ", detections.Select(d => $"{d.ClassName}({d.Confidence:P})"));
﻿                                                    result.Status = AnalysisStatus.Failure;
﻿                                                    result.ErrorMessage = $"Could not detect archery target in image. Found objects: {detectionSummary}. Ensure the image shows a clear archery target face.";
﻿                                                    return result;
﻿                                                }
﻿                                    
﻿                                                // 3. Extract target center and radius
﻿                                                result.TargetCenter = (targetDetection.X, targetDetection.Y);
﻿                                                result.TargetRadius = Math.Max(targetDetection.Width, targetDetection.Height) / 2f;
﻿                                                result.Status = AnalysisStatus.Success; // Explicitly set success if target found
﻿                                    
﻿                                                // 4. Filter and process arrow detections
﻿                                                var arrowDetections = detections
﻿                                                    .Where(d => !d.IsTargetFace && !d.IsMiss)  // Get actual arrow detections
﻿                                                    .OrderByDescending(d => d.Confidence)      // Sort by confidence
﻿                                                    .ToList();
﻿                                    
﻿                                                // 5. Populate DetectedArrows (already part of result)
﻿                                                // 6. Score each arrow
﻿                                                foreach (var yoloDet in arrowDetections)
﻿                                                {
﻿                                                    var arrowDetection = new ArrowDetection
﻿                                                    {
﻿                                                        CenterX = yoloDet.X,
﻿                                                        CenterY = yoloDet.Y,
﻿                                                        Radius = Math.Min(yoloDet.Width, yoloDet.Height) / 2f,
﻿                                                        Confidence = yoloDet.Confidence
﻿                                                    };
﻿                                    
﻿                                                    result.DetectedArrows.Add(arrowDetection);
﻿                                    
﻿                                                    var distFromCenter = this.CalculateDistance(
﻿                                                        arrowDetection.CenterX, arrowDetection.CenterY,
﻿                                                        result.TargetCenter.X, result.TargetCenter.Y); // Use result.TargetCenter
﻿                                    
﻿                                                    var points = this.CalculateArrowScore(distFromCenter, result.TargetRadius); // Use result.TargetRadius
﻿                                                    var ring = this.DetermineRing(distFromCenter, result.TargetRadius); // Use result.TargetRadius
﻿                                    
﻿                                                    result.ArrowScores.Add(new ArrowScore
﻿                                                    {
﻿                                                        Detection = arrowDetection,
﻿                                                        DistanceFromCenter = distFromCenter,
﻿                                                        Points = points,
﻿                                                        Ring = ring
﻿                                                    });
﻿                                    
﻿                                                    result.TotalScore += points;
﻿                                                }
﻿                                                return result;﻿        });
﻿    }
﻿
﻿    /// <summary>
﻿    /// Calculates score based on distance from center using Olympic scoring (10 rings).
﻿    /// Ring 1 (outermost) = 1 point, Ring 10 (innermost) = 10 points.
﻿    /// </summary>
﻿    public int CalculateArrowScore(float distanceFromCenter, float targetRadius)
﻿    {
﻿        if (targetRadius <= 0)
﻿        {
﻿            return 0;
﻿        }
﻿
﻿                // Normalize distance as a fraction of target radius (0.0 to 1.0+)
﻿                var normalizedDistance = distanceFromCenter / targetRadius;
﻿        
﻿                // Outside target = 0 points
﻿                if (normalizedDistance >= 1.0f)
﻿                {
﻿                    return 0;
﻿                }
﻿        
﻿                // Calculate score: 10 points for 0-0.1 radius, 9 for 0.1-0.2, etc.
﻿                // This effectively assigns a score based on which 0.1-segment of the radius the arrow falls into.
﻿                var score = 10 - (int)System.Math.Floor(normalizedDistance * 10);
﻿        
﻿                return System.Math.Max(0, score); // Ensure score is not negative
﻿            }
﻿        
﻿            /// <summary>
﻿            /// Determines which ring (1-10) an arrow hit, based on distance from center.
﻿            /// 0 indicates a miss (outside the target).
﻿            /// </summary>
﻿            private int DetermineRing(float distanceFromCenter, float targetRadius)
﻿            {
﻿                if (targetRadius <= 0)
﻿                {
﻿                    return 0;
﻿                }
﻿        
﻿                var normalizedDistance = distanceFromCenter / targetRadius;
﻿        
﻿                if (normalizedDistance >= 1.0f)
﻿                {
﻿                    return 0; // Miss
﻿                }
﻿        
﻿                // Determine ring based on 0.1 segments, 10 being innermost, 1 outermost.
﻿                // normalizedDistance 0.0-0.1 -> score 10 -> ring 10
﻿                // normalizedDistance 0.1-0.2 -> score 9 -> ring 9
﻿                // ...
﻿                // normalizedDistance 0.9-1.0 -> score 1 -> ring 1
﻿                return 10 - (int)System.Math.Floor(normalizedDistance * 10);﻿    }
﻿
﻿    /// <summary>
﻿    /// Calculates Euclidean distance between two points.
﻿    /// </summary>
﻿    private float CalculateDistance(float x1, float y1, float x2, float y2)
﻿    {
﻿        var dx = x2 - x1;
﻿        var dy = y2 - y1;
﻿        return (float)System.Math.Sqrt((dx * dx) + (dy * dy));
﻿    }
﻿}
﻿