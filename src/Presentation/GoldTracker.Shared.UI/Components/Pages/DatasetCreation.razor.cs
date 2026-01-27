using GoldTracker.Shared.UI.Services.Abstractions;
using Archery.Shared.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;

namespace GoldTracker.Shared.UI.Components.Pages
{
    public partial class DatasetCreation : IDisposable
    {
        [Inject] private ICameraService CameraService { get; set; } = default!;
        [Inject] private IDatasetExportService DatasetExportService { get; set; } = default!;
        [Inject] private IJSRuntime JSRuntime { get; set; } = default!;
        [Inject] private NavigationManager Navigation { get; set; } = default!;
        [Inject] private ISnackbar Snackbar { get; set; } = default!;
        [Inject] private IPlatformImageService ImageProcessingService { get; set; } = default!;

        private string? _originalImagePath;
        private byte[]? _originalImageBytes;
        private string _originalImageBase64 = string.Empty;

        private bool _isLoading = false;
        private bool _isLoaded = false;

        private ElementReference _imageElement;
        private ElementReference _canvasElement;
        private DotNetObjectReference<DatasetCreation> _dotNetObjectReference = null!;

        private string _selectedMode = "target";
        private int _selectedArrowScore = 10;
        private List<DatasetAnnotation> _annotations = new();
        private string[] _loadedClassLabels = Array.Empty<string>();
        private string[] _loadedClassColors = Array.Empty<string>();



        protected override void OnInitialized()
        {
            _dotNetObjectReference = DotNetObjectReference.Create(this);

            // Populate _loadedClassLabels and _loadedClassColors from ImageProcessingService.ObjectDetectionConfig.ClassLabels
            var classLabelsDict = ImageProcessingService.ObjectDetectionConfig.ClassLabels;
            var maxClassId = classLabelsDict.Keys.Any() ? classLabelsDict.Keys.Max() : -1;
            _loadedClassLabels = new string[maxClassId + 1];
            _loadedClassColors = new string[maxClassId + 1];

            for (int i = 0; i <= maxClassId; i++)
            {
                if (classLabelsDict.TryGetValue(i, out var label))
                {
                    _loadedClassLabels[i] = label;
                }
                else
                {
                    _loadedClassLabels[i] = $"unknown_{i}";
                }
                _loadedClassColors[i] = GetHexColor(i);
            }
        }

        private string GetHexColor(int classId)
        {
            return classId switch
            {
                11 => "#AA00FF", // Target - Purple
                10 or 9 => "#FFD700", // Gold
                8 or 7 => "#FF0000", // Red
                6 or 5 => "#2196F3", // Blue
                4 or 3 => "#333333", // Black
                _ => "#9E9E9E" // White/Other
            };
        }

        private MudBlazor.Color GetScoreButtonColor(int score)
        {
            return score switch
            {
                10 or 9 => MudBlazor.Color.Warning,
                8 or 7 => MudBlazor.Color.Error,
                6 or 5 => MudBlazor.Color.Info,
                4 or 3 => MudBlazor.Color.Dark,
                _ => MudBlazor.Color.Surface
            };
        }

        private async Task SetSelectedMode(string mode)
        {
            _selectedMode = mode;
            await JSRuntime.InvokeVoidAsync("annotator.deselect", "annotation-canvas");
            StateHasChanged();
        }

        private async Task SetSelectedScore(int score)
        {
            _selectedMode = "arrow";
            _selectedArrowScore = score;
            await JSRuntime.InvokeVoidAsync("annotator.deselect", "annotation-canvas");
            StateHasChanged();
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (!string.IsNullOrEmpty(_originalImageBase64) && !_isLoaded)
            {
                _isLoaded = true;
                Console.WriteLine("[DatasetCreation] Initializing annotator with canvas and image elements.");
                await JSRuntime.InvokeVoidAsync("annotator.init", _canvasElement, _imageElement, _dotNetObjectReference);
            }
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

                await LoadImageAsync(imagePath);
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

                await LoadImageAsync(imagePath);
            }
            finally { _isLoading = false; }
        }

        private async Task BrowseFilesAsync()
        {
            if (_isLoading) return;
            _isLoading = true;
            try
            {
                var imagePath = await CameraService.PickMediaAsync();
                if (string.IsNullOrEmpty(imagePath)) { Snackbar.Add("Cancelled", Severity.Info); return; }

                await LoadImageAsync(imagePath);
            }
            finally { _isLoading = false; }
        }

        private async Task LoadImageAsync(string imagePath)
        {
            try
            {
                _originalImagePath = imagePath;
                _originalImageBytes = await CameraService.ReadFileBytesAsync(imagePath);
                _originalImageBase64 = Convert.ToBase64String(_originalImageBytes);
            }
            catch (Exception ex)
            {
                Snackbar.Add($"Error loading image: {ex.Message}", Severity.Error);
                _originalImagePath = null;
                _originalImageBytes = null;
            }
        }

        [JSInvokable]
        public async Task AddBox(double startX, double startY, double endX, double endY)
        {
            int classId = _selectedMode == "target" ? 11 : _selectedArrowScore;

            var annotation = new DatasetAnnotation
            {
                ClassId = classId,
                StartX = startX,
                StartY = startY,
                EndX = endX,
                EndY = endY
            };
            _annotations.Add(annotation);

            var boxJs = new
            {
                startX = startX,
                startY = startY,
                endX = endX,
                endY = endY,
                label = _loadedClassLabels[classId],
                color = _loadedClassColors[classId]
            };

            await JSRuntime.InvokeVoidAsync("annotator.addBox", "annotation-canvas", boxJs);
            StateHasChanged();
        }

        [JSInvokable]
        public void UpdateBox(int index, double startX, double startY, double endX, double endY)
        {
            if (index >= 0 && index < _annotations.Count)
            {
                _annotations[index].StartX = startX;
                _annotations[index].StartY = startY;
                _annotations[index].EndX = endX;
                _annotations[index].EndY = endY;
                StateHasChanged();
            }
        }

        private async Task SaveAnnotationsAsync()
        {
            if (_originalImageBytes == null || !_annotations.Any())
            {
                Snackbar.Add("No annotations to save.", Severity.Warning);
                return;
            }

            try
            {
                 // Show processing message
                Snackbar.Add("Processing and saving dataset...", Severity.Info);

                await DatasetExportService.ExportDatasetAsync(_originalImageBytes, _annotations);

                Snackbar.Add($"Saved hierarchical dataset to Export/", Severity.Success);
                Reset();
            }
            catch (Exception ex)
            {
                Snackbar.Add($"Error saving annotations: {ex.Message}", Severity.Error);
                Console.WriteLine($"[DatasetCreation] Error: {ex}");
            }
        }

        private void UndoLastAnnotation()
        {
            if (_annotations.Any())
            {
                _annotations.RemoveAt(_annotations.Count - 1);
                JSRuntime.InvokeVoidAsync("annotator.removeLastBox", "annotation-canvas");
                StateHasChanged();
            }
        }

        private void ClearAnnotations()
        {
            _annotations.Clear();
            JSRuntime.InvokeVoidAsync("annotator.clear", "annotation-canvas");
            StateHasChanged();
        }

        private void Reset()
        {
            _originalImagePath = null;
            _originalImageBase64 = string.Empty;
            _originalImageBytes = null;
            _annotations.Clear();
            _isLoaded = false;
            StateHasChanged();
        }

        public void Dispose()
        {
            _dotNetObjectReference?.Dispose();
            if (_isLoaded)
            {
                JSRuntime.InvokeVoidAsync("annotator.destroy", "annotation-canvas");
            }
        }
    }
}
