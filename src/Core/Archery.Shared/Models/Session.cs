using System;
using System.Collections.Generic;
using System.Linq;

namespace Archery.Shared.Models
{
    public class Session
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        
        public DateTime StartTime { get; set; } = DateTime.Now;
        
        public DateTime? EndTime { get; set; }
        
        /// <summary>
        /// Optional topic or type of practice (e.g. "70m Ranking Round", "Blank Bale").
        /// </summary>
        public string? Topic { get; set; }
        
        /// <summary>
        /// Optional notes about the session.
        /// </summary>
        public string? Note { get; set; }
        
        /// <summary>
        /// List of ends shot during this session.
        /// </summary>
        public List<SessionEnd> Ends { get; set; } = new List<SessionEnd>();
        
        /// <summary>
        /// Total score accumulated across all ends.
        /// </summary>
        public int TotalScore => Ends.Sum(e => e.Score);
        
        /// <summary>
        /// Total number of arrows shot.
        /// </summary>
        public int TotalArrows => Ends.Sum(e => e.ArrowCount);

        /// <summary>
        /// Average points per arrow.
        /// </summary>
        public double ArrowAverage => TotalArrows > 0 ? (double)TotalScore / TotalArrows : 0;
    }
}
