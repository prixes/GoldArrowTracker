using System.Collections.Generic;
using System.Linq;
using Archery.Shared.Models;
using GoldTracker.Mobile.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;

namespace GoldTracker.Mobile.Components.Pages
{
    public partial class TargetCapture : IDisposable
    {
        [Inject] private CameraService CameraService { get; set; } = default!;
        [Inject] private ImageProcessingService ImageProcessingService { get; set; } = default!;
        [Inject] private NavigationManager Navigation { get; set; } = default!;
        [Inject] private ISnackbar Snackbar { get; set; } = default!;
        [Inject] private IJSRuntime JSRuntime { get; set; } = default!;

        private string? _originalImagePath;
        private byte[]? _originalImageBytes;
        private string _originalImageBase64 = string.Empty;
        private string _annotatedImageBase64 = string.Empty;

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
            await LoadStoredImagesAsync();
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
                var (imgWidth, imgHeight) = await GetImageDimensionsAsync();
                
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
                            label = d.ClassId == 10 ? "target" : (points == 0 ? "Miss" : points.ToString()),
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
                            label = c.CorrectedClassId == 10 ? "target" : (points == 0 ? "Miss" : points.ToString()),
                            color = GetScoreColor(points)
                        };
                    }).ToArray();
                
                // Load the boxes into the annotator
                await JSRuntime.InvokeVoidAsync("annotator.loadBoxes", "correction-canvas", (object)boxesForJs);
                
                Console.WriteLine($"[TargetCapture] Loaded {boxesForJs.Length} boxes into correction-canvas");
            }
        }

        private async Task<(int Width, int Height)> GetImageDimensionsAsync()
        {
            if (_originalImageBytes == null) return (1024, 1024);
            try
            {
                return await Task.Run(() => {
                    var info = SixLabors.ImageSharp.Image.Identify(_originalImageBytes);
                    return info != null ? (info.Width, info.Height) : (1024, 1024);
                });
            }
            catch { return (1024, 1024); }
        }

        private async Task LoadStoredImagesAsync()
        {
            try
            {
                _storedImages = CameraService.GetStoredImages().ToList();
                foreach (var imagePath in _storedImages.Take(4))
                {
                    try
                    {
                        var thumbnail = await ImageProcessingService.ResizeImageToBase64Async(imagePath, 200);
                        if (!string.IsNullOrEmpty(thumbnail))
                        {
                            _storedImageThumbnails[imagePath] = thumbnail;
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
                var rawBytes = await System.IO.File.ReadAllBytesAsync(imagePath);
                
                // Resize to max 1024px for faster processing and display
                _originalImageBytes = await ImageProcessingService.ResizeImageAsync(rawBytes, 1024);
                
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
                _analysisResult = await ImageProcessingService.AnalyzeTargetFromBytesAsync(_originalImageBytes);

                // Always attempt to draw detections if original image bytes exist and analysisResult is available
                if (_originalImageBytes != null && _analysisResult != null)
                {
                    _annotatedImageBase64 = await ImageProcessingService.DrawDetectionsOnImageAsync(_originalImageBytes, _analysisResult);
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
                // TODO: Save to database or local storage
                Snackbar.Add("Result saved!", Severity.Success);
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
        private void EnterReviewMode()
        {
            if (_analysisResult == null || _originalImageBytes == null) return;

            // Get image dimensions to normalize coordinates
            int imgWidth = 1024; // Default if identification fails
            int imgHeight = 1024;
            try 
            {
                var info = SixLabors.ImageSharp.Image.Identify(_originalImageBytes);
                if (info != null)
                {
                    imgWidth = info.Width;
                    imgHeight = info.Height;
                }
            } catch { }

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
                label = points == 100 ? "Target" : (points == 0 ? "Miss" : points.ToString()),
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

            // Get image dimensions for coordinate conversion
            var (imgWidth, imgHeight) = await GetImageDimensionsAsync();

            // Clear existing data
            _analysisResult.ArrowScores.Clear();
            _analysisResult.Detections.Clear();
            _analysisResult.TotalScore = 0;

            foreach (var correction in _corrections.Where(c => !c.IsDeleted))
            {
                // 1. Update Detections (for visual display on results screen)
                var widthNorm = correction.EndX - correction.StartX;
                var heightNorm = correction.EndY - correction.StartY;
                
                var detection = new ObjectDetectionResult
                {
                    ClassId = correction.CorrectedClassId,
                    ClassName = correction.CorrectedClassId == 10 ? "target" : MapClassIdToPoints(correction.CorrectedClassId).ToString(),
                    Confidence = 1.0f,
                    X = (float)((correction.StartX + correction.EndX) / 2.0 * imgWidth),
                    Y = (float)((correction.StartY + correction.EndY) / 2.0 * imgHeight),
                    Width = (float)(widthNorm * imgWidth),
                    Height = (float)(heightNorm * imgHeight)
                };
                _analysisResult.Detections.Add(detection);

                // 2. Update Scores & Target Center
                int points = MapClassIdToPoints(correction.CorrectedClassId);
                if (points == 100) // Target Face
                {
                    _analysisResult.TargetCenter = (detection.X, detection.Y);
                    _analysisResult.TargetRadius = detection.Width / 2.0f;
                }
                else if (points > 0 && points <= 10)
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
                            Confidence = detection.Confidence
                        }
                    };
                    _analysisResult.ArrowScores.Add(arrowScore);
                    _analysisResult.TotalScore += points;
                }
            }

            // Redraw the annotated image with corrections
            if (_originalImageBytes != null && _analysisResult != null)
            {
                _annotatedImageBase64 = await ImageProcessingService.DrawDetectionsOnImageAsync(_originalImageBytes, _analysisResult);
            }
        }

        /// <summary>
        /// Maps current model class ID to points.
        /// Current model: 0-9 = score, 10 = target, 11 = 10 points
        /// </summary>
        private int MapClassIdToPoints(int classId)
        {
            return classId switch
            {
                0 => 0,  // Miss
                1 => 1,
                2 => 2,
                3 => 3,
                4 => 4,
                5 => 5,
                6 => 6,
                7 => 7,
                8 => 8,
                9 => 9,
                11 => 10, // 10 points (class 11 in current model)
                10 => 100, // Target face (internal 100)
                _ => 0
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
                _ => "#9E9E9E"        // White/Other
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
        private void SelectNewScore(int score)
        {
            _selectedNewScore = score;

            if (_selectedCorrectionIndex >= 0 && _selectedCorrectionIndex < _corrections.Count)
            {
                SaveToHistory();
                // Map score to class ID (current model format for internal use)
                int classId = score switch
                {
                    100 => 10, // Target face
                    10 => 11,  // 10 points
                    _ => score
                };
                _corrections[_selectedCorrectionIndex].CorrectedClassId = classId;
                
                // Update the box color in JS
                UpdateCorrectionBoxInJs(_selectedCorrectionIndex);
            }
            StateHasChanged();
        }

        /// <summary>
        /// Deletes the currently selected correction.
        /// </summary>
        private void DeleteSelectedCorrection()
        {
            if (_selectedCorrectionIndex >= 0 && _selectedCorrectionIndex < _corrections.Count)
            {
                SaveToHistory();
                _corrections[_selectedCorrectionIndex].IsDeleted = true;
                JSRuntime.InvokeVoidAsync("annotator.removeBox", "correction-canvas", _selectedCorrectionIndex);
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
            var label = points == 100 ? "Target" : (points == 0 ? "Miss" : points.ToString());

            JSRuntime.InvokeVoidAsync("annotator.updateBoxStyle", "correction-canvas", index, color, label);
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
        private async Task ExportToYoloDatasetAsync()
        {
            if (_originalImageBytes == null) return;

            try
            {
                var hasPermission = await CameraService.RequestStorageWritePermissionAsync();
                if (!hasPermission) return;

                var timestamp = $"{DateTime.Now:yyyyMMdd_HHmmssfff}";
                var microImageDir = Path.Combine("Export", "Micro_Model", "images");
                var microLabelDir = Path.Combine("Export", "Micro_Model", "labels");

                // Save micro model image
                var microImagePath = await CameraService.SaveImageAsync(_originalImageBytes, $"{timestamp}.jpg", microImageDir);
                if (string.IsNullOrEmpty(microImagePath)) return;

                // Build label strings with new dataset class mapping
                var labelStrings = new List<string>();
                foreach (var correction in _corrections.Where(c => !c.IsDeleted))
                {
                    var x_center = (correction.StartX + correction.EndX) / 2.0;
                    var y_center = (correction.StartY + correction.EndY) / 2.0;
                    var width = correction.EndX - correction.StartX;
                    var height = correction.EndY - correction.StartY;

                    // Map to new dataset class IDs (swap 10 and 11)
                    var correctionPoints = MapClassIdToPoints(correction.CorrectedClassId);
                    int newClassId = MapPointsToNewDatasetClassId(correctionPoints);

                    labelStrings.Add($"{newClassId} {x_center:F6} {y_center:F6} {width:F6} {height:F6}");
                }

                if (labelStrings.Any())
                {
                    var microLabelPath = Path.Combine(Path.GetDirectoryName(microImagePath)!.Replace("images", "labels"), $"{timestamp}.txt");
                    Directory.CreateDirectory(Path.GetDirectoryName(microLabelPath)!);
                    await File.WriteAllTextAsync(microLabelPath, string.Join("\n", labelStrings));
                    CameraService.TriggerMediaScanner(microLabelPath);
                }

                Console.WriteLine($"[TargetCapture] Exported corrections to YOLO dataset: {timestamp}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TargetCapture] Export error: {ex.Message}");
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
                        label = points == 100 ? "Target" : (points == 0 ? "Miss" : points.ToString()),
                        color = GetScoreColor(points)
                    };
                }).ToArray();

            await JSRuntime.InvokeVoidAsync("annotator.loadBoxes", "correction-canvas", (object)boxesForJs);
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

