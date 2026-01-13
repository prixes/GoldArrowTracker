using Archery.Shared.Models;
using GoldTracker.Mobile.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace GoldTracker.Mobile.Components.Pages
{
    public partial class TargetCapture
    {
        [Inject] private CameraService CameraService { get; set; } = default!;
        [Inject] private ImageProcessingService ImageProcessingService { get; set; } = default!;
        [Inject] private NavigationManager Navigation { get; set; } = default!;
        [Inject] private ISnackbar Snackbar { get; set; } = default!;

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

        protected override async Task OnInitializedAsync()
        {
            await LoadStoredImagesAsync();
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
                        var thumbnail = await ImageProcessingService.ImageToBase64Async(imagePath);
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
                _originalImageBytes = await System.IO.File.ReadAllBytesAsync(imagePath);
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
    }
}
