using Archery.Shared.Models;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using Archery.Shared.Services;

namespace GoldTracker.Shared.UI.Components.Pages.Sessions
{
    public partial class SessionComprehensiveDetail
    {
        [Inject] private ISessionService SessionService { get; set; } = default!;
        [Inject] private NavigationManager Navigation { get; set; } = default!;

        [Parameter] public Guid SessionId { get; set; }

        private Session? _session;
        private bool _isLoading = true;
        
        // Target Visualization Data
        private List<ArrowPoint> _allArrowPoints = new();
        private double _centroidX = 50;
        private double _centroidY = 50;
        private double _groupRadius = 0;
        private double _groupAverage = 0;
        private bool _showEllipse = true; // Still using this name but drawing a circle
        
        private int _confidencePercentage = 80;
        private ChartOptions _lineChartOptions = new() 
        { 
            YAxisLines = true, 
            XAxisLines = true, 
            InterpolationOption = InterpolationOption.NaturalSpline,
            MaxNumYAxisTicks = 10,
            YAxisTicks = 5
        };

        // Analysis Data
        private Dictionary<int, int> _scoreDistribution = new();
        private List<ZoneHit> _zoneHits = new();
        
        // Chart Data
        private List<ChartSeries> _endsEvolutionSeries = new();
        private string[] _endsEvolutionLabels = Array.Empty<string>();

        private record ArrowPoint(float X, float Y, int Score, int EndIndex, int ArrowIndex)
        {
            public bool IsInGroup { get; set; } = true;
            public double DistanceFromCentroid { get; set; }
        }
        private record ZoneHit(string Label, int Count, double Percentage, string Color);

        public int ConfidencePercentage
        {
             get => _confidencePercentage;
             set
             {
                 if (_confidencePercentage != value)
                 {
                     _confidencePercentage = value;
                     CalculateGroupStatistics();
                     StateHasChanged();
                 }
             }
        }

        protected override async Task OnInitializedAsync()
        {
            await LoadSession();
        }

        private async Task LoadSession()
        {
            _isLoading = true;
            try
            {
                _session = await SessionService.GetSessionAsync(SessionId);
                if (_session != null)
                {
                    ProcessSessionData();
                }
            }
            finally
            {
                _isLoading = false;
                StateHasChanged();
            }
        }

        // Custom Evolution Chart Data
        private double _evolMin = 0;
        private double _evolMax = 30;
        private string _evolPath = "";
        private List<double> _evolGridY = new();
        private List<(double x, string label)> _evolGridX = new();

        private void ProcessSessionData()
        {
            _allArrowPoints.Clear();
            _scoreDistribution.Clear();
            _zoneHits.Clear();

            if (_session == null) return;

            var sortedEnds = _session.Ends.OrderBy(e => e.Index).ToList();
            
            var validScores = new[] { 100, 10, 9, 8, 7, 6, 0 };
            foreach (var s in validScores) _scoreDistribution[s] = 0;

            List<double> endScores = new();
            List<string> endLabels = new();

            foreach (var end in sortedEnds)
            {
                endScores.Add(end.Score);
                endLabels.Add(end.Index.ToString());

                int arrowIdx = 0;
                foreach (var arrow in end.Arrows)
                {
                    int key = arrow.Points; 
                    if (_scoreDistribution.ContainsKey(key))
                        _scoreDistribution[key]++;
                    else
                         _scoreDistribution[0]++;

                    var (x, y) = GetNormalizedArrowPosition(arrow, end);
                    _allArrowPoints.Add(new ArrowPoint(x, y, arrow.Points, end.Index, arrowIdx++));
                }
            }

            CalculateZoneHits();

            // Custom Evolution Chart Logic (Zoomed scaling)
            if (endScores.Any())
            {
                double rawMin = endScores.Min();
                double rawMax = endScores.Max();
                
                // Archery floor is always 0
                _evolMin = Math.Max(0, Math.Floor(rawMin - 1));
                _evolMax = Math.Ceiling(rawMax + 1);
                
                // Ensure a reasonable spread for the "zoom" effect
                if (_evolMax - _evolMin < 4) 
                {
                    _evolMax = _evolMin + 5;
                }
                
                // SVG is 100x40
                double width = 100;
                double height = 40;
                double xStep = width / Math.Max(1, endScores.Count - 1);
                
                var points = new List<string>();
                _evolGridX.Clear();
                for (int i = 0; i < endScores.Count; i++)
                {
                    double x = i * xStep;
                    double y = height - ((endScores[i] - _evolMin) / (_evolMax - _evolMin) * height);
                    points.Add($"{x:F2},{y:F2}"); // More precision for smoother lines
                    _evolGridX.Add((x, endLabels[i]));
                }
                _evolPath = string.Join(" ", points);

                _evolGridY.Clear();
                double range = _evolMax - _evolMin;
                double step = range > 10 ? 5 : (range > 5 ? 2 : 1);
                
                // Grid lines should be clean integers
                for (double v = _evolMin; v <= _evolMax; v += step)
                {
                    if (v >= 0) _evolGridY.Add(v);
                }
            }

            CalculateGroupStatistics();
        }

        private void CalculateZoneHits()
        {
            if (_session == null || _session.TotalArrows == 0) return;
            int total = _session.TotalArrows;

            int xCount = _scoreDistribution[100];
            _zoneHits.Add(new ZoneHit("X", xCount, (double)xCount / total * 100, "#FFD600"));

            int tenPlus = xCount + _scoreDistribution[10];
            _zoneHits.Add(new ZoneHit("10+", tenPlus, (double)tenPlus / total * 100, "#FFEB3B"));

            int ninePlus = tenPlus + _scoreDistribution[9];
            _zoneHits.Add(new ZoneHit("9+", ninePlus, (double)ninePlus / total * 100, "#FFEE58"));

            int eightPlus = ninePlus + _scoreDistribution[8];
            _zoneHits.Add(new ZoneHit("8+", eightPlus, (double)eightPlus / total * 100, "#F44336"));

            int sevenPlus = eightPlus + _scoreDistribution[7];
            _zoneHits.Add(new ZoneHit("7+", sevenPlus, (double)sevenPlus / total * 100, "#E53935"));
        }

        private void CalculateGroupStatistics()
        {
            if (_allArrowPoints.Count == 0)
            {
                _showEllipse = false;
                return;
            }

            // 1. Calculate Centroid
            double sumX = 0, sumY = 0;
            foreach (var point in _allArrowPoints)
            {
                sumX += point.X;
                sumY += point.Y;
            }
            double meanX = sumX / _allArrowPoints.Count;
            double meanY = sumY / _allArrowPoints.Count;

            _centroidX = 50 + meanX * 50;
            _centroidY = 50 + meanY * 50;

            // 2. Calculate Distances and Sort
            foreach (var point in _allArrowPoints)
            {
                double dx = point.X - meanX;
                double dy = point.Y - meanY;
                point.DistanceFromCentroid = Math.Sqrt(dx * dx + dy * dy);
            }

            var sortedPoints = _allArrowPoints.OrderBy(p => p.DistanceFromCentroid).ToList();
            
            // 3. Find Radius for target percentage
            int countThreshold = (int)Math.Ceiling(sortedPoints.Count * (_confidencePercentage / 100.0));
            countThreshold = Math.Clamp(countThreshold, 1, sortedPoints.Count);

            double radiusInNormalizedCoords = sortedPoints[countThreshold - 1].DistanceFromCentroid;
            _groupRadius = radiusInNormalizedCoords * 50; // Scale to SVG 0-100 space

            // 4. Mark points and calculate Group Average
            double scoreSum = 0;
            int inGroupCount = 0;
            foreach (var point in _allArrowPoints)
            {
                point.IsInGroup = point.DistanceFromCentroid <= radiusInNormalizedCoords + 0.001;
                if (point.IsInGroup)
                {
                    // Special handling for X (100 -> 10 for avg)
                    int scoreVal = point.Score == 100 ? 10 : point.Score;
                    scoreSum += scoreVal;
                    inGroupCount++;
                }
            }

            _groupAverage = inGroupCount > 0 ? scoreSum / inGroupCount : 0;
            _showEllipse = true;
        }

        private (float x, float y) GetNormalizedArrowPosition(ArrowScore arrow, SessionEnd end)
        {
            if (arrow.Detection == null) return (0, 0);

            if (end.TargetRadius > 0)
            {
                float dx = arrow.Detection.CenterX - end.TargetCenterX;
                float dy = arrow.Detection.CenterY - end.TargetCenterY;
                float trX = end.TargetRadius;
                float trY = end.TargetRadiusY > 0 ? end.TargetRadiusY : end.TargetRadius;
                return (dx / trX, dy / trY);
            }
            
             double normDist = (11 - arrow.Ring) * 0.1;
            if (arrow.Points == 100) normDist = 0.05;
            var seed = arrow.Points + (int)arrow.DistanceFromCenter + arrow.GetHashCode();
            var random = new Random(seed);
            double angle = random.NextDouble() * 2 * Math.PI;
            return ((float)(Math.Cos(angle) * normDist), (float)(Math.Sin(angle) * normDist));
        }

        private void GoBack()
        {
            Navigation.NavigateTo($"/session/{SessionId}");
        }

        private MudBlazor.Color GetArrowColor(int points)
        {
            return points switch
            {
                100 or 10 or 9 => MudBlazor.Color.Warning,
                8 or 7 => MudBlazor.Color.Error,
                6 or 5 => MudBlazor.Color.Info,
                4 or 3 => MudBlazor.Color.Dark,
                _ => MudBlazor.Color.Default
            };
        }
    }
}
