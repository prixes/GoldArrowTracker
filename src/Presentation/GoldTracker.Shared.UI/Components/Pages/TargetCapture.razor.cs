using Archery.Shared.Models;
using GoldTracker.Shared.UI.Services.Abstractions;
using Archery.Shared.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;

namespace GoldTracker.Shared.UI.Components.Pages
{
    public partial class TargetCapture : IDisposable
    {
        [Inject] private ICameraService CameraService { get; set; } = default!;
        [Inject] private IPlatformImageService ImageProcessingService { get; set; } = default!;
        [Inject] private NavigationManager Navigation { get; set; } = default!;
        [Inject] private ISnackbar Snackbar { get; set; } = default!;
        [Inject] private IJSRuntime JSRuntime { get; set; } = default!;
        [Inject] private ISessionState SessionState { get; set; } = default!;
        [Inject] private ISessionService SessionService { get; set; } = default!;
        [Inject] private IDatasetExportService DatasetExportService { get; set; } = default!;

        [SupplyParameterFromQuery] public Guid? sessionId { get; set; }
        [SupplyParameterFromQuery] public int? endIndex { get; set; }
        [SupplyParameterFromQuery] public string? editMode { get; set; }

        private bool IsEditingExistingEnd => sessionId.HasValue && endIndex.HasValue;

        private Guid? _existingEndId;
        private string? _originalImagePath;
        private byte[]? _originalImageBytes;
        private string _originalImageBase64 = string.Empty;
        private string _annotatedImageBase64 = string.Empty;
        private int _originalWidth = 1024;
        private int _originalHeight = 1024;

        private bool _isLoading = false;
        private bool _analysisComplete = false;

        private TargetAnalysisResult? _analysisResult;

        private string? _thumbHover = null;

        private List<string> _storedImages = new();
        private Dictionary<string, string> _storedImageThumbnails = new();

        // Review Mode State
        private bool _isReviewMode = false;
        private bool _isAnnotatorLoaded = false;
        private bool _isResultsAnnotatorLoaded = false;
        private ElementReference _reviewImageElement;
        private ElementReference _reviewCanvasElement;
        private ElementReference _resultImageElement;
        private ElementReference _resultCanvasElement;
        private DotNetObjectReference<TargetCapture>? _dotNetObjectReference;
        private List<CorrectionAnnotation> _corrections = new();
        private Stack<List<CorrectionAnnotation>> _history = new();
        private int _selectedCorrectionIndex = -1;
        private int? _selectedNewScore = null;

        /// <summary>
        /// Represents an editable correction annotation for a detection.
        /// </summary>
        public class CorrectionAnnotation
        {
            public int OriginalClassId { get; set; }
            public int CorrectedClassId { get; set; }
            public double StartX { get; set; }
            public double StartY { get; set; }
            public double EndX { get; set; }
            public double EndY { get; set; }
            public bool IsDeleted { get; set; }
            public bool IsNew { get; set; }
        }


        protected override async Task OnInitializedAsync()
        {
            // Fire and forget thumbnail loading so page transition is instant
            _ = Task.Run(async () => await LoadStoredImagesAsync());

            if (IsEditingExistingEnd)
            {
                await LoadExistingEndAsync();
            }
        }

        private async Task LoadExistingEndAsync()
        {
            _isLoading = true;
            try
            {
                Session? session;
                if (SessionState.CurrentSession?.Id == sessionId)
                {
                    session = SessionState.CurrentSession;
                }
                else
                {
                    session = await SessionService.GetSessionAsync(sessionId!.Value);
                }

                if (session == null) return;

                var end = session.Ends.FirstOrDefault(e => e.Index == endIndex);
                if (end == null || string.IsNullOrEmpty(end.ImagePath)) return;

                _existingEndId = end.Id;

                _originalImagePath = end.ImagePath;
                var rawBytes = await System.IO.File.ReadAllBytesAsync(end.ImagePath);
                
                // Keep original dimensions for coordinate scaling
                var dims = await ImageProcessingService.GetImageDimensionsAsync(rawBytes);
                _originalWidth = dims.Width;
                _originalHeight = dims.Height;

                _originalImageBytes = await ImageProcessingService.ResizeImageAsync(rawBytes, 1024, 80, end.ImagePath);
                _originalImageBase64 = Convert.ToBase64String(_originalImageBytes);

                // Reconstruct Analysis Result from End
                _analysisResult = new TargetAnalysisResult
                {
                    Status = AnalysisStatus.Success,
                    TargetCenterX = end.TargetCenterX,
                    TargetCenterY = end.TargetCenterY,
                    TargetRadius = end.TargetRadius,
                    TargetRadiusY = end.TargetRadiusY,
                    Detections = end.Arrows.Select(a => a.Detection != null ? new ObjectDetectionResult
                    {
                        ClassId = MapPointsToClassId(a.Points),
                        ClassName = a.Points.ToString(),
                        Confidence = a.Detection.Confidence,
                        X = a.Detection.CenterX,
                        Y = a.Detection.CenterY,
                        Width = a.Detection.Radius * 2,
                        Height = a.Detection.Radius * 2
                    } : null).Where(d => d != null).ToList()!
                };

                // Add synthetic target detection for review mode if metadata exists
                if (end.TargetRadius > 0)
                {
                    _analysisResult.Detections.Add(new ObjectDetectionResult
                    {
                        ClassId = 10, // Target Face
                        ClassName = "target",
                        Confidence = 1.0f,
                        X = end.TargetCenterX,
                        Y = end.TargetCenterY,
                        Width = end.TargetRadius * 2,
                        Height = (end.TargetRadiusY > 0 ? end.TargetRadiusY : end.TargetRadius) * 2
                    });
                }

                // Add target detection if present (we might need to store it better, but for now let's try to find it)
                // Actually, if it's missing, the user will have to add it.

                _annotatedImageBase64 = await ImageProcessingService.DrawDetectionsOnImageAsync(_originalImageBytes, _analysisResult, _originalWidth, _originalHeight);
                _analysisComplete = true;

                if (editMode == "review")
                {
                    await EnterReviewMode();
                }
            }
            catch (Exception ex)
            {
                Snackbar.Add($"Error loading end: {ex.Message}", Severity.Error);
            }
            finally
            {
                _isLoading = false;
                StateHasChanged();
            }
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            // 1. Initialize result magnifier (read-only) when analysis is complete
            if (_analysisComplete && !_isReviewMode && !_isResultsAnnotatorLoaded && _analysisResult != null)
            {
                _isResultsAnnotatorLoaded = true;
                _dotNetObjectReference ??= DotNetObjectReference.Create(this);

                // Initialize as read-only and hide boxes initially
                await JSRuntime.InvokeVoidAsync("annotator.init", _resultCanvasElement, _resultImageElement, _dotNetObjectReference, true, false);

                // Get dimensions for normalization
                var imgWidth = _originalWidth;
                var imgHeight = _originalHeight;
                
                // Load existing detections as non-editable boxes
                var boxesForJs = _analysisResult.Detections
                    .Select(d => {
                        var halfW = d.Width / 2;
                        var halfH = d.Height / 2;
                        var points = MapClassIdToPoints(d.ClassId);
                        return new {
                            startX = (double)(d.X - halfW) / imgWidth,
                            startY = (double)(d.Y - halfH) / imgHeight,
                            endX = (double)(d.X + halfW) / imgWidth,
                            endY = (double)(d.Y + halfH) / imgHeight,
                            label = d.ClassId == 10 ? "TARGET" : (points == 0 ? "M" : points.ToString()),
                            color = GetScoreColor(points),
                            hidden = true
                        };
                    }).ToArray();

                await JSRuntime.InvokeVoidAsync("annotator.loadBoxes", "results-canvas", (object)boxesForJs);
            }

            // 2. Initialize review annotator when entering review mode
            if (_isReviewMode && !_isAnnotatorLoaded && _corrections.Any())
            {
                _isAnnotatorLoaded = true;
                _dotNetObjectReference ??= DotNetObjectReference.Create(this);
                
                // Initialize the annotator with canvas and image
                await JSRuntime.InvokeVoidAsync("annotator.init", _reviewCanvasElement, _reviewImageElement, _dotNetObjectReference);
                
                // Build boxes array for JS
                var boxesForJs = _corrections
                    .Where(c => !c.IsDeleted)
                    .Select(c => {
                        var points = MapClassIdToPoints(c.CorrectedClassId);
                        return new {
                            startX = c.StartX,
                            startY = c.StartY,
                            endX = c.EndX,
                            endY = c.EndY,
                            label = c.CorrectedClassId == 10 ? "TARGET" : (points == 0 ? "M" : points.ToString()),
                            color = GetScoreColor(points)
                        };
                    }).ToArray();
                
                // Load the boxes into the annotator
                await JSRuntime.InvokeVoidAsync("annotator.loadBoxes", "correction-canvas", (object)boxesForJs);
                
                System.Diagnostics.Debug.WriteLine($"[TargetCapture] Loaded {boxesForJs.Length} boxes into correction-canvas");
            }
        }

        private async Task LoadStoredImagesAsync()
        {
            try
            {
                _storedImages = CameraService.GetStoredImages().ToList();
                
                // Update UI once with the list of paths (placeholders can show)
                await InvokeAsync(StateHasChanged);

                foreach (var imagePath in _storedImages.Take(4))
                {
                    try
                    {
                        var thumbnail = await ImageProcessingService.ResizeImageToBase64Async(imagePath, 200);
                        if (!string.IsNullOrEmpty(thumbnail))
                        {
                            _storedImageThumbnails[imagePath] = thumbnail;
                            
                            // Update UI progressively as each thumbnail loads
                            await InvokeAsync(StateHasChanged);
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        private async Task CapturePhotoAsync()
        {
            if (_isLoading) return;
            _isLoading = true;
            try
            {
                var hasPermission = await CameraService.RequestCameraPermissionAsync();
                if (!hasPermission) { Snackbar.Add("Camera permission denied", Severity.Error); return; }

                var imagePath = await CameraService.CapturePhotoAsync();
                if (string.IsNullOrEmpty(imagePath)) { Snackbar.Add("Cancelled", Severity.Info); return; }

                await LoadAndAnalyzeImageAsync(imagePath);
                await LoadStoredImagesAsync();
            }
            finally { _isLoading = false; }
        }

        private async Task PickPhotoAsync()
        {
            if (_isLoading) return;
            _isLoading = true;
            try
            {
                var imagePath = await CameraService.PickPhotoAsync();
                if (string.IsNullOrEmpty(imagePath)) { Snackbar.Add("Cancelled", Severity.Info); return; }

                await LoadAndAnalyzeImageAsync(imagePath);
                await LoadStoredImagesAsync();
            }
            finally { _isLoading = false; }
        }

        private async Task BrowseStorageAsync()
        {
            if (_isLoading) return;
            _isLoading = true;
            try
            {
                var imagePath = await CameraService.PickMediaAsync();
                if (string.IsNullOrEmpty(imagePath)) { Snackbar.Add("Cancelled", Severity.Info); return; }

                await LoadAndAnalyzeImageAsync(imagePath);
                await LoadStoredImagesAsync();
            }
            finally { _isLoading = false; }
        }

        private async Task SelectStoredImageAsync(string imagePath)
        {
            if (_isLoading) return;
            _isLoading = true;
            try
            {
                await LoadAndAnalyzeImageAsync(imagePath);
            }
            finally { _isLoading = false; }
        }

        private async Task LoadAndAnalyzeImageAsync(string imagePath)
        {
            // 1. Load image
            try
            {
                _originalImagePath = imagePath;
                var rawBytes = await CameraService.ReadFileBytesAsync(imagePath);
                
                // Keep original dimensions for coordinate scaling
                var dims = await ImageProcessingService.GetImageDimensionsAsync(rawBytes);
                _originalWidth = dims.Width;
                _originalHeight = dims.Height;

                // Resize to max 1024px for faster processing and display
                _originalImageBytes = await ImageProcessingService.ResizeImageAsync(rawBytes, 1024, 80, imagePath);
                
                _originalImageBase64 = Convert.ToBase64String(_originalImageBytes);
                _analysisComplete = false;
                _analysisResult = null;
            }
            catch (Exception ex)
            {
                Snackbar.Add($"Error loading image: {ex.Message}", Severity.Error);
                _originalImagePath = null;
                _originalImageBytes = null;
                return;
            }

            // 2. Analyze image
            if (!ImageProcessingService.IsModelAvailable)
            {
                Snackbar.Add("Object Detection model not available", Severity.Error);
                _analysisComplete = true; // Show result screen with error
                _annotatedImageBase64 = _originalImageBase64;
                return;
            }

            try
            {
                _analysisResult = await ImageProcessingService.AnalyzeTargetFromBytesAsync(_originalImageBytes, null);

                // Scale result up to original dimensions because analysis was on 1024 preview
                if (_analysisResult != null && _originalImageBytes != null)
                {
                    var previewDims = await ImageProcessingService.GetImageDimensionsAsync(_originalImageBytes);
                    ScaleResult(_analysisResult, _originalWidth, _originalHeight, previewDims.Width, previewDims.Height);
                }

                // Always attempt to draw detections if original image bytes exist and analysisResult is available
                if (_originalImageBytes != null && _analysisResult != null)
                {
                    _annotatedImageBase64 = await ImageProcessingService.DrawDetectionsOnImageAsync(_originalImageBytes, _analysisResult, _originalWidth, _originalHeight);
                }
                else
                {
                    _annotatedImageBase64 = _originalImageBase64; // Fallback if no analysis result or original image
                }

                if (_analysisResult?.Status == AnalysisStatus.Success)
                {
                    Snackbar.Add("Analysis complete!", Severity.Success);
                }
                else
                {
                    if (_analysisResult != null && !string.IsNullOrEmpty(_analysisResult.ErrorMessage))
                    {
                        Snackbar.Add(_analysisResult.ErrorMessage, Severity.Warning);
                    }
                    else
                    {
                        Snackbar.Add("Analysis failed for an unknown reason.", Severity.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                _annotatedImageBase64 = _originalImageBase64;
                Snackbar.Add($"Analysis failed: {ex.Message}", Severity.Error);
            }
            finally
            {
                _analysisComplete = true; // Go to results screen
                StateHasChanged();
            }
        }

        private async Task SaveResultAsync()
        {
            if (_analysisResult == null)
            {
                Snackbar.Add("No results to save", Severity.Warning);
                return;
            }

            try
            {
                // Create list of all detections (mapped from analysis result)
                var allDetections = _analysisResult.Detections.Select(d => new ObjectDetectionResult
                {
                    ClassId = d.ClassId,
                    ClassName = d.ClassName,
                    Confidence = d.Confidence,
                    X = d.X,
                    Y = d.Y,
                    Width = d.Width,
                    Height = d.Height
                }).ToList();

                if (IsEditingExistingEnd)
                {
                    var updatedEnd = new SessionEnd
                    {
                         Id = _existingEndId ?? Guid.NewGuid(),
                         Timestamp = DateTime.Now,
                         ImagePath = _originalImagePath,
                         TargetRadius = _analysisResult.TargetRadius,
                         TargetRadiusY = _analysisResult.TargetRadiusY > 0 ? _analysisResult.TargetRadiusY : _analysisResult.TargetRadius,
                         TargetCenterX = _analysisResult.TargetCenterX,
                         TargetCenterY = _analysisResult.TargetCenterY,
                         AllDetections = allDetections, // Persist all detections
                         Arrows = _analysisResult.ArrowScores.Select(a => new ArrowScore
                         {
                             Points = a.Points,
                             Ring = a.Ring,
                             DistanceFromCenter = a.DistanceFromCenter,
                             Detection = a.Detection
                         }).ToList()
                    };

                    await SessionState.UpdateEndAsync(sessionId!.Value, endIndex!.Value, updatedEnd);
                    Snackbar.Add("End updated!", Severity.Success);
                    Navigation.NavigateTo($"/session-end/{sessionId}/{endIndex}");
                    return;
                }

                if (SessionState.IsSessionActive)
                {
                    // Create SessionEnd from Analysis Result
                    var end = new SessionEnd
                    {
                        Timestamp = DateTime.Now,
                        ImagePath = _originalImagePath,
                        TargetRadius = _analysisResult.TargetRadius,
                        TargetRadiusY = _analysisResult.TargetRadiusY > 0 ? _analysisResult.TargetRadiusY : _analysisResult.TargetRadius,
                        TargetCenterX = _analysisResult.TargetCenterX,
                        TargetCenterY = _analysisResult.TargetCenterY,
                        AllDetections = allDetections, // Persist all detections
                        Arrows = _analysisResult.ArrowScores.Select(a => new ArrowScore 
                        { 
                            Points = a.Points,
                            Ring = a.Ring,
                            DistanceFromCenter = a.DistanceFromCenter,
                            Detection = a.Detection 
                        }).ToList()
                    };
                    
                     // If image path is null (e.g. fresh capture not saved to gallery), we should save it to app data
                     if (string.IsNullOrEmpty(end.ImagePath) && _originalImageBytes != null)
                     {
                          var fileName = $"{Guid.NewGuid()}.jpg";
                          end.ImagePath = await CameraService.SaveInternalImageAsync(fileName, _originalImageBytes);
                     }

                    await SessionState.AddEndAsync(end);
                    Snackbar.Add($"Added end to session!", Severity.Success);
                    Navigation.NavigateTo("/session-live");
                }
                else
                {
                     Snackbar.Add("Result saved! (Standalone)", Severity.Success);
                     Navigation.NavigateTo("/sessions");
                }
            }
            catch (Exception ex)
            {
                Snackbar.Add($"Save failed: {ex.Message}", Severity.Error);
            }
        }

        private async Task ResetAsync()
        {
            _originalImagePath = null;
            _originalImageBase64 = string.Empty;
            _annotatedImageBase64 = string.Empty;
            _originalImageBytes = null;
            _analysisComplete = false;
            _analysisResult = null;
            StateHasChanged();
        }

        private string GetArrowStyle(ArrowScore arrow)
        {
            var borderColor = arrow.Points switch
            {
                10 => "#FFD700", // Gold
                9 => "#DAA520",  // Gold
                8 => "#FF0000",  // Red
                7 => "#CD5C5C",  // Red
                6 => "#0000FF",  // Blue
                5 => "#4169E1",  // Blue
                4 => "#000000",  // Black
                3 => "#36454F",  // Black
                2 => "#FFFFFF",  // White
                1 => "#F5F5DC",  // Off-white
                _ => "#f44336"   // Default to red for unknown/0 points
            };
            return $"background: #f9f9f9; border-left: 4px solid {borderColor}; border-radius: 8px;";
        }

        #region Review Mode

        /// <summary>
        /// Enters review mode, converting detections to editable annotations.
        /// </summary>
        private async Task EnterReviewMode()
        {
            if (_analysisResult == null || _originalImageBytes == null) return;

            // Get image dimensions to normalize coordinates
            var imgWidth = _originalWidth;
            var imgHeight = _originalHeight;

            _corrections.Clear();
            _dotNetObjectReference = DotNetObjectReference.Create(this);

            // Convert detections to correction annotations (normalized 0-1)
            foreach (var detection in _analysisResult.Detections)
            {
                var halfW = detection.Width / 2;
                var halfH = detection.Height / 2;

                _corrections.Add(new CorrectionAnnotation
                {
                    OriginalClassId = detection.ClassId,
                    CorrectedClassId = detection.ClassId,
                    StartX = (detection.X - halfW) / imgWidth,
                    StartY = (detection.Y - halfH) / imgHeight,
                    EndX = (detection.X + halfW) / imgWidth,
                    EndY = (detection.Y + halfH) / imgHeight,
                    IsDeleted = false,
                    IsNew = false
                });
            }

            _isReviewMode = true;
            _isAnnotatorLoaded = false;
            _selectedCorrectionIndex = -1;
            _selectedNewScore = null;
            StateHasChanged();
        }

        /// <summary>
        /// Called from JS when a box is selected in the annotator.
        /// </summary>
        [JSInvokable]
        public void OnBoxSelected(int index)
        {
            if (index == -1)
            {
                _selectedCorrectionIndex = -1;
                // Keep _selectedNewScore as is for "drawing mode"
                StateHasChanged();
                return;
            }

            // Map JS box index to correction index (accounting for deleted items)
            var nonDeletedIndices = _corrections
                .Select((c, i) => new { c, i })
                .Where(x => !x.c.IsDeleted)
                .Select(x => x.i)
                .ToList();
            
            if (index >= 0 && index < nonDeletedIndices.Count)
            {
                _selectedCorrectionIndex = nonDeletedIndices[index];
                var correction = _corrections[_selectedCorrectionIndex];
                _selectedNewScore = MapClassIdToPoints(correction.CorrectedClassId);
                StateHasChanged();
            }
        }

        /// <summary>
        /// Called from JS when a box is moved/resized in the annotator.
        /// </summary>
        [JSInvokable]
        public void UpdateBox(int index, double startX, double startY, double endX, double endY)
        {
            var nonDeletedIndices = _corrections
                .Select((c, i) => new { c, i })
                .Where(x => !x.c.IsDeleted)
                .Select(x => x.i)
                .ToList();
            
            if (index >= 0 && index < nonDeletedIndices.Count)
            {
                SaveToHistory();
                var correctionIndex = nonDeletedIndices[index];
                _corrections[correctionIndex].StartX = startX;
                _corrections[correctionIndex].StartY = startY;
                _corrections[correctionIndex].EndX = endX;
                _corrections[correctionIndex].EndY = endY;
                StateHasChanged();
            }
        }

        /// <summary>
        /// Called from JS when a new box is drawn in the annotator.
        /// </summary>
        [JSInvokable]
        public async Task AddBox(double startX, double startY, double endX, double endY)
        {
            SaveToHistory();
            // Add as a new detection with the currently selected score
            int points = _selectedNewScore ?? 10;
            int classId = points switch
            {
                100 => 10, // Target face
                10 => 11,  // 10 points is class 11
                _ => points
            };

            var newCorrection = new CorrectionAnnotation
            {
                OriginalClassId = classId,
                CorrectedClassId = classId,
                StartX = startX,
                StartY = startY,
                EndX = endX,
                EndY = endY,
                IsDeleted = false,
                IsNew = true
            };
            _corrections.Add(newCorrection);

            // Add box to JS annotator
            points = MapClassIdToPoints(classId);
            var boxJs = new
            {
                startX = startX,
                startY = startY,
                endX = endX,
                endY = endY,
                label = points == 100 ? "TARGET" : (points == 0 ? "M" : points.ToString()),
                color = GetScoreColor(points)
            };
            await JSRuntime.InvokeVoidAsync("annotator.addBox", "correction-canvas", boxJs);

            // Select the new box
            _selectedCorrectionIndex = _corrections.Count - 1;
            _selectedNewScore = points;
            StateHasChanged();
        }


        /// <summary>
        /// Exits review mode, optionally applying corrections.
        /// </summary>
        private async Task ExitReviewModeAsync(bool applyCorrections)
        {
            if (applyCorrections && _analysisResult != null)
            {
                // Apply corrections to analysis result
                await ApplyCorrectionsToResultAsync();
                
                // Export to YOLO dataset (automatic, underneath)
                await ExportToYoloDatasetAsync();

                if (IsEditingExistingEnd)
                {
                    await SaveResultAsync();
                    return;
                }
            }

            // Cleanup annotator
            if (_isAnnotatorLoaded)
            {
                await JSRuntime.InvokeVoidAsync("annotator.destroy", "correction-canvas");
            }

            _isReviewMode = false;
            _isAnnotatorLoaded = false;
            _isResultsAnnotatorLoaded = false; // Reset to force re-initialization with corrections
            _corrections.Clear();
            StateHasChanged();
        }

        /// <summary>
        /// Applies the corrections to the analysis result and recalculates score.
        /// </summary>
        private async Task ApplyCorrectionsToResultAsync()
        {
            if (_analysisResult == null) return;
            
            // Use stored original dimensions for coordinate conversion
            var imgWidth = _originalWidth;
            var imgHeight = _originalHeight;

            // Clear existing data
            _analysisResult.ArrowScores.Clear();
            _analysisResult.Detections.Clear();
            _analysisResult.TotalScore = 0;
            
            System.Diagnostics.Debug.WriteLine($"[ApplyCorrections] Applying {_corrections.Count} corrections ({_corrections.Count(c => c.IsDeleted)} deleted)");

            // Reset primary target metrics (will be set by the first '100' class correction)
            _analysisResult.TargetRadius = 0;

            foreach (var correction in _corrections.Where(c => !c.IsDeleted))
            {
                // 1. Update Detections (for persistent editing and display)
                var widthNorm = correction.EndX - correction.StartX;
                var heightNorm = correction.EndY - correction.StartY;
                
                int points = MapClassIdToPoints(correction.CorrectedClassId);
                var detection = new ObjectDetectionResult
                {
                    ClassId = correction.CorrectedClassId,
                    ClassName = points == 100 ? "target" : points.ToString(),
                    Confidence = 1.0f,
                    X = (float)((correction.StartX + correction.EndX) / 2.0 * imgWidth),
                    Y = (float)((correction.StartY + correction.EndY) / 2.0 * imgHeight),
                    Width = (float)(widthNorm * imgWidth),
                    Height = (float)(heightNorm * imgHeight)
                };
                _analysisResult.Detections.Add(detection);

                // 2. Update Scores & Targets
                if (points == 100)
                {
                    // Primary target selection (first one found sets the scoring reference)
                    if (_analysisResult.TargetRadius <= 0)
                    {
                        _analysisResult.TargetCenterX = detection.X;
                        _analysisResult.TargetCenterY = detection.Y;
                        _analysisResult.TargetRadius = detection.Width / 2.0f;
                        _analysisResult.TargetRadiusY = detection.Height / 2.0f;
                    }
                    // Even if it's not primary, it's already in _analysisResult.Detections
                }
                else if (points >= 0 && points <= 10)
                {
                    var arrowScore = new ArrowScore
                    {
                        Points = points,
                        Ring = points,
                        DistanceFromCenter = 0,
                        Detection = new ArrowDetection
                        {
                            CenterX = detection.X,
                            CenterY = detection.Y,
                            Radius = detection.Width / 2.0f,
                            Confidence = 1.0f
                        }
                    };
                    _analysisResult.ArrowScores.Add(arrowScore);
                    _analysisResult.TotalScore += points;
                }
            }

            // Redraw the annotated image with ALL corrections
            if (_originalImageBytes != null && _analysisResult != null)
            {
                _annotatedImageBase64 = await ImageProcessingService.DrawDetectionsOnImageAsync(_originalImageBytes, _analysisResult, _originalWidth, _originalHeight);
            }
        }

        /// <summary>
        /// Maps current model class ID to points.
        /// Current model: 0-9 = score, 10 = target, 11 = 10 points
        /// </summary>
        private int MapClassIdToPoints(int classId)
        {
            // Based on object_detection_config.json:
            // 0-9 => Points 0-9
            // 10 => "target" (Face) -> Mapped to 100 internally
            // 11 => "10" (Inner 10) -> Mapped to 10
            return classId switch
            {
                0 => 0,
                1 => 1,
                2 => 2,
                3 => 3,
                4 => 4,
                5 => 5,
                6 => 6,
                7 => 7,
                8 => 8,
                9 => 9,
                10 => 100, // Config says 10 is "target"
                11 => 10,  // Config says 11 is "10"
                _ => 0
            };
        }

        private int MapPointsToClassId(int points)
        {
            return points switch
            {
                100 => 10, // Target face
                10 => 11,  // 10 points
                _ => points
            };
        }

        /// <summary>
        /// Gets hex color for a score value.
        /// </summary>
        private string GetScoreColor(int score)
        {
            return score switch
            {
                100 => "#FF4081", // Target marker color
                10 or 9 => "#FFD700", // Gold
                8 or 7 => "#FF0000",  // Red
                6 or 5 => "#2196F3",  // Blue
                4 or 3 => "#333333",  // Black
                2 or 1 => "#FFFFFF",  // White
                _ => "#9E9E9E"        // Miss/Other
            };
        }

        /// <summary>
        /// Gets MudBlazor color for a score button.
        /// </summary>
        private MudBlazor.Color GetScoreButtonMudColor(int score)
        {
            return score switch
            {
                100 => MudBlazor.Color.Secondary,
                10 or 9 => MudBlazor.Color.Warning,
                8 or 7 => MudBlazor.Color.Error,
                6 or 5 => MudBlazor.Color.Info,
                4 or 3 => MudBlazor.Color.Dark,
                _ => MudBlazor.Color.Surface
            };
        }

        /// <summary>
        /// Selects a new score for the currently selected detection.
        /// </summary>
        private async Task SelectNewScore(int points)
        {
            _selectedNewScore = points;
            
            // Toggle square enforcement: Target (100) can be non-square (ellipse), arrows must be square
            bool squareEnforced = points != 100;
            if (_isAnnotatorLoaded)
            {
                await JSRuntime.InvokeVoidAsync("annotator.setSquareEnforced", "correction-canvas", squareEnforced);
            }

            if (_selectedCorrectionIndex >= 0 && _selectedCorrectionIndex < _corrections.Count)
            {
                SaveToHistory();
                var correction = _corrections[_selectedCorrectionIndex];
                correction.CorrectedClassId = MapPointsToClassId(points);
                
                var jsIndex = GetJsIndex(_selectedCorrectionIndex);
                if (jsIndex != -1)
                {
                    await JSRuntime.InvokeVoidAsync("annotator.updateBoxStyle", "correction-canvas", jsIndex, GetScoreColor(points), points == 100 ? "TARGET" : (points == 0 ? "M" : points.ToString()));
                }
                StateHasChanged();
            }
        }

        /// <summary>
        /// Deletes the currently selected correction.
        /// </summary>
        private void DeleteSelectedCorrection()
        {
            if (_selectedCorrectionIndex >= 0 && _selectedCorrectionIndex < _corrections.Count)
            {
                SaveToHistory();
                
                // Important: Calculate JS index BEFORE marking as deleted, otherwise GetJsIndex returns -1
                var jsIndex = GetJsIndex(_selectedCorrectionIndex);
                
                _corrections[_selectedCorrectionIndex].IsDeleted = true;
                
                if (jsIndex != -1)
                {
                    JSRuntime.InvokeVoidAsync("annotator.removeBox", "correction-canvas", jsIndex);
                }
                
                _selectedCorrectionIndex = -1;
                _selectedNewScore = null;
                StateHasChanged();
            }
        }

        /// <summary>
        /// Updates a correction box color in JavaScript after score change.
        /// </summary>
        private void UpdateCorrectionBoxInJs(int index)
        {
            if (index < 0 || index >= _corrections.Count) return;
            var correction = _corrections[index];
            var points = MapClassIdToPoints(correction.CorrectedClassId);
            var color = GetScoreColor(points);
            var label = points == 100 ? "TARGET" : (points == 0 ? "M" : points.ToString());

            var jsIndex = GetJsIndex(index);
            if (jsIndex != -1)
            {
                JSRuntime.InvokeVoidAsync("annotator.updateBoxStyle", "correction-canvas", jsIndex, color, label);
            }
        }

        /// <summary>
        /// Maps points to new dataset class ID.
        /// New dataset: 0-10 = score, 11 = target
        /// </summary>
        private int MapPointsToNewDatasetClassId(int points)
        {
            if (points == 100) return 11; // Target face is class 11 in NEW format
            if (points == 10) return 10;  // 10 points is class 10 in NEW format
            return points;
        }

        /// <summary>
        /// Exports corrected data to YOLO dataset format.
        /// </summary>
        /// <summary>
        /// Exports corrected data to YOLO dataset format using the shared service.
        /// </summary>
        private async Task ExportToYoloDatasetAsync()
        {
            if (_originalImageBytes == null) return;

                try
                {
                    var datasetAnnotations = new List<DatasetAnnotation>();

                    foreach (var correction in _corrections.Where(c => !c.IsDeleted))
                    {
                        datasetAnnotations.Add(new DatasetAnnotation
                        {
                            ClassId = correction.CorrectedClassId, // Pass pure internal ID (10=Target, 11=10pts)
                            StartX = correction.StartX,
                            StartY = correction.StartY,
                            EndX = correction.EndX,
                            EndY = correction.EndY
                        });
                    }

                    // Delegate to the shared service which handles Macro (Target extraction) and Micro (Cropping) exports
                    await DatasetExportService.ExportDatasetAsync(_originalImageBytes, datasetAnnotations);
                    Console.WriteLine($"[TargetCapture] Exported {datasetAnnotations.Count} corrections via DatasetExportService.");
                }
                catch (Exception ex)
            {
                Console.WriteLine($"[TargetCapture] Export error: {ex.Message}");
                Snackbar.Add($"Export failed: {ex.Message}", Severity.Error);
            }
        }
        
        #endregion

        #region Helpers

        private void SaveToHistory()
        {
            var snapshot = _corrections.Select(c => new CorrectionAnnotation
            {
                OriginalClassId = c.OriginalClassId,
                CorrectedClassId = c.CorrectedClassId,
                StartX = c.StartX,
                StartY = c.StartY,
                EndX = c.EndX,
                EndY = c.EndY,
                IsDeleted = c.IsDeleted,
                IsNew = c.IsNew
            }).ToList();
            _history.Push(snapshot);
        }

        private async Task UndoCorrection()
        {
            if (_history.TryPop(out var prev))
            {
                _corrections = prev;
                _selectedCorrectionIndex = -1;
                _selectedNewScore = null;
                await ReloadAnnotatorBoxesAsync();
                StateHasChanged();
            }
        }

        private async Task ReloadAnnotatorBoxesAsync()
        {
            if (!_isAnnotatorLoaded) return;

            var boxesForJs = _corrections
                .Where(c => !c.IsDeleted)
                .Select(c => {
                    var points = MapClassIdToPoints(c.CorrectedClassId);
                    return new {
                        startX = c.StartX,
                        startY = c.StartY,
                        endX = c.EndX,
                        endY = c.EndY,
                        label = points == 100 ? "TARGET" : (points == 0 ? "M" : points.ToString()),
                        color = GetScoreColor(points)
                    };
                }).ToArray();

            await JSRuntime.InvokeVoidAsync("annotator.loadBoxes", "correction-canvas", (object)boxesForJs);
        }

        private (float x, float y) GetNormalizedArrowPosition(ArrowScore arrow)
        {
            if (_analysisResult == null || _analysisResult.TargetRadius <= 0 || _analysisResult.TargetRadiusY <= 0) 
                return (0, 0);

            // Calculate offset from target center in pixels
            float dx = arrow.Detection.CenterX - _analysisResult.TargetCenterX;
            float dy = arrow.Detection.CenterY - _analysisResult.TargetCenterY;

            // Rotation Fix: The user reports 90 degree rotation issues. 
            // Reverting to plain to re-assess with user.
            return (dx / _analysisResult.TargetRadius, dy / _analysisResult.TargetRadiusY);
        }

        private void ScaleResult(TargetAnalysisResult result, int targetW, int targetH, int sourceW, int sourceH)
        {
            if (sourceW <= 0 || sourceH <= 0) return;

            float scaleX = (float)targetW / sourceW;
            float scaleY = (float)targetH / sourceH;

            // Scale Target Face
            result.TargetCenterX *= scaleX;
            result.TargetCenterY *= scaleY;
            result.TargetRadius *= scaleX;
            result.TargetRadiusY *= scaleY;

            // Scale All Detections
            foreach (var d in result.Detections)
            {
                d.X *= scaleX;
                d.Y *= scaleY;
                d.Width *= scaleX;
                d.Height *= scaleY;
            }

            // Scale Arrow Scores
            foreach (var a in result.ArrowScores)
            {
                if (a.Detection != null)
                {
                    a.Detection.CenterX *= scaleX;
                    a.Detection.CenterY *= scaleY;
                    a.Detection.Radius *= scaleX; // Approx
                }
                a.DistanceFromCenter *= scaleX; // Approx
            }
        }

        private void GoBack()
        {
            if (IsEditingExistingEnd)
            {
                Navigation.NavigateTo($"/session-end/{sessionId}/{endIndex}");
            }
            else
            {
                Navigation.NavigateTo("/capture");
            }
        }

        private int GetJsIndex(int cSharpIndex)
        {
            if (cSharpIndex < 0 || cSharpIndex >= _corrections.Count) return -1;
            
            // If the item itself is deleted, it doesn't have a JS index
            if (_corrections[cSharpIndex].IsDeleted) return -1;

            // Count how many non-deleted items are before this index
            int count = 0;
            for (int i = 0; i < cSharpIndex; i++)
            {
                if (!_corrections[i].IsDeleted)
                {
                    count++;
                }
            }
            return count;
        }

        #endregion

        public void Dispose()
        {
            _dotNetObjectReference?.Dispose();
            if (_isAnnotatorLoaded)
            {
                JSRuntime.InvokeVoidAsync("annotator.destroy", "correction-canvas");
            }
            if (_isResultsAnnotatorLoaded)
            {
                JSRuntime.InvokeVoidAsync("annotator.destroy", "results-canvas");
            }
        }
    }
}

