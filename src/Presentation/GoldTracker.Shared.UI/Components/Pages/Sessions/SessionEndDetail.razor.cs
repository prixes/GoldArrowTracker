using GoldTracker.Shared.UI.Services.Abstractions;
using Archery.Shared.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;
using Archery.Shared.Services;

namespace GoldTracker.Shared.UI.Components.Pages.Sessions
{
    public partial class SessionEndDetail : IDisposable
    {
        [Inject] private ISessionService SessionService { get; set; } = default!;
        [Inject] private ISessionState SessionState { get; set; } = default!;
        [Inject] private NavigationManager Navigation { get; set; } = default!;
        [Inject] private ISnackbar Snackbar { get; set; } = default!;
        [Inject] private IPlatformImageService ImageProcessingService { get; set; } = default!;
        [Inject] private IJSRuntime JSRuntime { get; set; } = default!;
        [Inject] private IPlatformProvider PlatformProvider { get; set; } = default!;
        [Inject] private IDialogService DialogService { get; set; } = default!;

        [Parameter] public Guid SessionId { get; set; }
        [Parameter] public int EndIndex { get; set; }

        private Session? _session;
        private SessionEnd? _end;
        private bool _isLoading = true;
        private string _imageSrc = string.Empty;
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
            if (!string.IsNullOrEmpty(_imageSrc) && !_isResultsAnnotatorLoaded)
            {
                // Small delay to ensure image is rendered/sized
                await Task.Delay(300);
                if (_photoCanvasElement.Id == null) return; // Still not ready

                _isResultsAnnotatorLoaded = true;
                _dotNetObjectReference ??= DotNetObjectReference.Create(this);
                // Use standard init which supports magnifier/zoom
                // showBoxes=true on Detail view so we see the red boxes from the canvas instead of burned-in
                await JSRuntime.InvokeVoidAsync("annotator.init", _photoCanvasElement, _photoImageElement, _dotNetObjectReference, true, true);
                
                // Load boxes to display them
                if (_end != null)
                {
                    var boxesForJs = _end.Arrows.Select(a => {
                        if (a.Detection == null) return null;
                        var halfR = a.Detection.Radius;
                        return new {
                            startX = (double)(a.Detection.CenterX - halfR) / _imgWidth,
                            startY = (double)(a.Detection.CenterY - halfR) / _imgHeight,
                            endX = (double)(a.Detection.CenterX + halfR) / _imgWidth,
                            endY = (double)(a.Detection.CenterY + halfR) / _imgHeight,
                            label = a.Points == 100 ? "X" : a.Points.ToString(),
                            color = GetArrowHexColor(a.Points),
                            hidden = false
                        };
                    }).ToList();

                    if (_end.TargetRadius > 0)
                    {
                        boxesForJs.Add(new {
                            startX = (double)(_end.TargetCenterX - _end.TargetRadius) / _imgWidth,
                            startY = (double)(_end.TargetCenterY - (_end.TargetRadiusY > 0 ? _end.TargetRadiusY : _end.TargetRadius)) / _imgHeight,
                            endX = (double)(_end.TargetCenterX + _end.TargetRadius) / _imgWidth,
                            endY = (double)(_end.TargetCenterY + (_end.TargetRadiusY > 0 ? _end.TargetRadiusY : _end.TargetRadius)) / _imgHeight,
                            label = "target",
                            color = "#FF4081",
                            hidden = false
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
                    
                    if (_end != null && !string.IsNullOrEmpty(_end.ImagePath))
                    {
                        var rawBytes = await ImageProcessingService.LoadImageBytesAsync(_end.ImagePath, SessionId);
                        if (rawBytes.Length > 0)
                        {
                            var dims = await ImageProcessingService.GetImageDimensionsAsync(rawBytes);
                        _imgWidth = dims.Width;
                        _imgHeight = dims.Height;

                        var analysisResult = new TargetAnalysisResult
                        {
                            Status = AnalysisStatus.Success,
                            TargetCenterX = _end.TargetCenterX,
                            TargetCenterY = _end.TargetCenterY,
                            TargetRadius = _end.TargetRadius,
                            TargetRadiusY = _end.TargetRadiusY,
                            Detections = _end.AllDetections?.ToList() ?? new List<ObjectDetectionResult>()
                        };

                        _imageSrc = await ImageProcessingService.GetImageDisplaySourceAsync(rawBytes, analysisResult);
                    }
                }
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
