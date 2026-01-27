using System;
using System.Collections.Generic;
using System.Linq;

namespace Archery.Shared.Models
{
    public class SessionEnd
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        
        /// <summary>
        /// Order of this end in the session (1-indexed).
        /// </summary>
        public int Index { get; set; }
        
        public DateTime Timestamp { get; set; } = DateTime.Now;
        
        /// <summary>
        /// Path to the image of the target face for this end (if any).
        /// </summary>
        public string? ImagePath { get; set; }

        public float TargetRadius { get; set; }
        public float TargetRadiusY { get; set; }
        public float TargetCenterX { get; set; }
        public float TargetCenterY { get; set; }
        
        /// <summary>
        /// List of arrows shot in this end.
        /// </summary>
        public List<ArrowScore> Arrows { get; set; } = new List<ArrowScore>();

        /// <summary>
        /// List of all detections (including extra target faces) for persistent editing.
        /// </summary>
        public List<ObjectDetectionResult> AllDetections { get; set; } = new List<ObjectDetectionResult>();
        
        /// <summary>
        /// Total score for this end.
        /// </summary>
        public int Score => Arrows.Sum(a => a.Points);

        /// <summary>
        /// Number of arrows in this end.
        /// </summary>
        public int ArrowCount => Arrows.Count;
    }
}
