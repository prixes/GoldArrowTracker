using GoldTracker.Mobile.Services;
using Archery.Shared.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;
using GoldTracker.Mobile.Services.Sessions;

namespace GoldTracker.Mobile.Components.Pages.Sessions
{
    public partial class SessionEndDetail : IDisposable
    {
        [Inject] private ISessionService SessionService { get; set; } = default!;
        [Inject] private ISessionState SessionState { get; set; } = default!;
        [Inject] private NavigationManager Navigation { get; set; } = default!;
        [Inject] private ISnackbar Snackbar { get; set; } = default!;
        [Inject] private ImageProcessingService ImageProcessingService { get; set; } = default!;
        [Inject] private IJSRuntime JSRuntime { get; set; } = default!;
        [Inject] private IDialogService DialogService { get; set; } = default!;

        [Parameter] public Guid SessionId { get; set; }
        [Parameter] public int EndIndex { get; set; }

        private Session? _session;
        private SessionEnd? _end;
        private bool _isLoading = true;
        private string _annotatedImageBase64 = string.Empty;
        private ElementReference _photoImageElement;
        private ElementReference _photoCanvasElement;
        private DotNetObjectReference<SessionEndDetail>? _dotNetObjectReference;
        private bool _isResultsAnnotatorLoaded = false;
        private int _imgWidth = 1024;
        private int _imgHeight = 1024;

        protected override async Task OnParametersSetAsync()
        {
            await LoadData();
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (!string.IsNullOrEmpty(_annotatedImageBase64) && !_isResultsAnnotatorLoaded)
            {
                // Small delay to ensure image is rendered/sized
                await Task.Delay(300);
                if (_photoCanvasElement.Id == null) return; // Still not ready

                _isResultsAnnotatorLoaded = true;
                _dotNetObjectReference ??= DotNetObjectReference.Create(this);
                // Use standard init which supports magnifier/zoom
                await JSRuntime.InvokeVoidAsync("annotator.init", _photoCanvasElement, _photoImageElement, _dotNetObjectReference, true, false);
                
                // Load boxes (hidden) to enable magnifier on detections
                if (_end != null)
                {
                    var boxesForJs = _end.Arrows.Select(a => {
                        if (a.Detection == null) return null;
                        var halfW = a.Detection.Radius;
                        return new {
                            startX = (double)(a.Detection.CenterX - halfW) / _imgWidth,
                            startY = (double)(a.Detection.CenterY - halfW) / _imgHeight,
                            endX = (double)(a.Detection.CenterX + halfW) / _imgWidth,
                            endY = (double)(a.Detection.CenterY + halfW) / _imgHeight,
                            label = a.Points == 100 ? "X" : a.Points.ToString(),
                            color = GetArrowHexColor(a.Points),
                            hidden = true // Hide from main view, show in magnifier
                        };
                    }).ToList();

                    // Add target face box to allow tapping anywhere to zoom
                    if (_end.TargetRadius > 0)
                    {
                        var trX = _end.TargetRadius;
                        var trY = _end.TargetRadiusY > 0 ? _end.TargetRadiusY : _end.TargetRadius;
                        boxesForJs.Add(new {
                            startX = (double)(_end.TargetCenterX - trX) / _imgWidth,
                            startY = (double)(_end.TargetCenterY - trY) / _imgHeight,
                            endX = (double)(_end.TargetCenterX + trX) / _imgWidth,
                            endY = (double)(_end.TargetCenterY + trY) / _imgHeight,
                            label = "target",
                            color = "#2196F3",
                            hidden = true // Hide border but allow selection
                        });
                    }

                    await JSRuntime.InvokeVoidAsync("annotator.loadBoxes", "photo-results-canvas", (object)boxesForJs.Where(b => b != null).ToArray());
                }
            }
        }

        [JSInvokable]
        public void OnBoxSelected(int index)
        {
            // Read-only view doesn't need selection logic, 
            // but the JS side expects this method to exist.
        }



        private async Task LoadData()
        {
            _isLoading = true;
            _isResultsAnnotatorLoaded = false;
            try
            {
                // Try from active session first if it matches
                if (SessionState.CurrentSession?.Id == SessionId)
                {
                    _session = SessionState.CurrentSession;
                }
                else
                {
                    _session = await SessionService.GetSessionAsync(SessionId);
                }

                if (_session != null)
                {
                    _end = _session.Ends.FirstOrDefault(e => e.Index == EndIndex);
                    
                    if (_end != null && !string.IsNullOrEmpty(_end.ImagePath) && File.Exists(_end.ImagePath))
                    {
                        var rawBytes = await File.ReadAllBytesAsync(_end.ImagePath);
                        var dims = await ImageProcessingService.GetImageDimensionsAsync(rawBytes);
                        _imgWidth = dims.Width;
                        _imgHeight = dims.Height;

                        // Resize for display
                        var displayBytes = await ImageProcessingService.ResizeImageAsync(rawBytes, 1024, 80, _end.ImagePath);
                        
                        // Reconstruct analysis result for drawing
                        var analysisResult = new TargetAnalysisResult
                        {
                            Status = AnalysisStatus.Success,
                            TargetCenterX = _end.TargetCenterX,
                            TargetCenterY = _end.TargetCenterY,
                            TargetRadius = _end.TargetRadius,
                            TargetRadiusY = _end.TargetRadiusY,
                            Detections = _end.Arrows.Select(a => a.Detection != null ? new ObjectDetectionResult
                            {
                                ClassId = (a.Points == 10 || a.Points == 100) ? 11 : a.Points,
                                ClassName = a.Points == 100 ? "X" : a.Points.ToString(),
                                Confidence = a.Detection.Confidence,
                                X = a.Detection.CenterX,
                                Y = a.Detection.CenterY,
                                Width = a.Detection.Radius * 2,
                                Height = a.Detection.Radius * 2
                            } : null).Where(d => d != null).ToList()!,
                            ArrowScores = _end.Arrows.Select(a => a.Detection != null ? new ArrowScore
                            {
                                Points = a.Points,
                                Ring = a.Ring,
                                DistanceFromCenter = a.DistanceFromCenter,
                                Detection = new ArrowDetection
                                {
                                    CenterX = a.Detection.CenterX,
                                    CenterY = a.Detection.CenterY,
                                    Confidence = a.Detection.Confidence,
                                    Radius = a.Detection.Radius
                                }
                            } : null).Where(s => s != null).ToList()!
                        };

                        // Add target face if metadata exists
                        if (_end.TargetRadius > 0)
                        {
                            analysisResult.Detections.Add(new ObjectDetectionResult
                            {
                                ClassId = 10, // "target" (matches TargetCapture mapping)
                                ClassName = "target",
                                Confidence = 1.0f,
                                X = _end.TargetCenterX,
                                Y = _end.TargetCenterY,
                                Width = _end.TargetRadius * 2,
                                Height = (_end.TargetRadiusY > 0 ? _end.TargetRadiusY : _end.TargetRadius) * 2
                            });
                        }

                        _annotatedImageBase64 = await ImageProcessingService.DrawDetectionsOnImageAsync(displayBytes, analysisResult, _imgWidth, _imgHeight);
                    }
                }
            }
            finally
            {
                _isLoading = false;
                StateHasChanged();
            }
        }

        private void GoBack()
        {
            // If it's the active session, go to live view
            if (SessionState.CurrentSession?.Id == SessionId)
            {
                Navigation.NavigateTo("/session-live");
            }
            else
            {
                Navigation.NavigateTo($"/session/{SessionId}");
            }
        }

        private void EnterEditMode()
        {
            Navigation.NavigateTo($"/target-capture?sessionId={SessionId}&endIndex={EndIndex}&editMode=review");
        }

        private async Task ConfirmDelete()
        {
            bool? result = await DialogService.ShowMessageBox(
                "Delete End", 
                $"Are you sure you want to delete End {EndIndex}? This will re-index remaining ends in this session.", 
                yesText: "Delete", cancelText: "Cancel");

            if (result == true)
            {
                await SessionState.DeleteEndAsync(SessionId, EndIndex);
                Snackbar.Add("End deleted", Severity.Success);
                
                // Navigate back to the session view
                if (SessionState.CurrentSession?.Id == SessionId)
                {
                    Navigation.NavigateTo("/session-live");
                }
                else
                {
                    Navigation.NavigateTo($"/session/{SessionId}");
                }
            }
        }

        private (float x, float y) GetNormalizedArrowPosition(ArrowScore arrow)
        {
            if (arrow.Detection == null || _end == null) return (0, 0);

            if (_end.TargetRadius > 0)
            {
                // Calculate offset from target center in pixels
                float dx = arrow.Detection.CenterX - _end.TargetCenterX;
                float dy = arrow.Detection.CenterY - _end.TargetCenterY;

                // Normalize by target radius. 
                // Result will be -1 to 1 regardless of tilt
                float trX = _end.TargetRadius;
                float trY = _end.TargetRadiusY > 0 ? _end.TargetRadiusY : _end.TargetRadius;
                
                float normX = dx / trX;
                float normY = dy / trY;

                return (normX, normY); // Reverting to plain for now to re-assess with user
            }
            
            // Fallback to deterministic spread based on score and distance if metadata is missing
            double normDist = (11 - arrow.Ring) * 0.1;
            if (arrow.Points == 100) normDist = 0.05;
            
            var seed = arrow.Points + (int)arrow.DistanceFromCenter + arrow.GetHashCode();
            var random = new Random(seed);
            double angle = random.NextDouble() * 2 * Math.PI;

            return ((float)(Math.Cos(angle) * normDist), (float)(Math.Sin(angle) * normDist));
        }

        private string GetArrowHexColor(int points)
        {
            return points switch
            {
                10 or 9 => "#FFD700",
                8 or 7 => "#FF0000",
                6 or 5 => "#2196F3",
                4 or 3 => "#333333",
                _ => "#9E9E9E"
            };
        }

        private string GetImageSrc(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    var bytes = File.ReadAllBytes(path);
                    return $"data:image/jpeg;base64,{Convert.ToBase64String(bytes)}";
                }
            }
            catch { }
            return "";
        }

        private MudBlazor.Color GetArrowColor(int points)
        {
            return points switch
            {
                10 or 9 => MudBlazor.Color.Warning,
                8 or 7 => MudBlazor.Color.Error,
                6 or 5 => MudBlazor.Color.Info,
                4 or 3 => MudBlazor.Color.Dark,
                _ => MudBlazor.Color.Default
            };
        }
        public void Dispose()
        {
            _dotNetObjectReference?.Dispose();
            if (_isResultsAnnotatorLoaded)
            {
                JSRuntime.InvokeVoidAsync("annotator.destroy", "photo-results-canvas");
            }
        }
    }
}
