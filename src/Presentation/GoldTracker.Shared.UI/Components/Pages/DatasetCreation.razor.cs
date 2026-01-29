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
        private string _displayImageSrc = string.Empty;

        private bool _isLoading = false;
        private bool _isLoaded = false;

        private ElementReference _imageElement;
        private ElementReference _canvasElement;
        private DotNetObjectReference<DatasetCreation> _dotNetObjectReference = null!;

        private string _selectedMode = "target";
        private int _selectedArrowScore = 10;
        private List<DatasetAnnotation> _annotations = new();
        private int _selectedAnnotationIndex = -1; // Track selected box
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
            // Sync with TargetCapture logic (Internal IDs)
            return classId switch
            {
                10 => "#00FFFF", // Target - Cyan
                11 => "#FFFF00", // 10 - Yellow
                9 => "#FFD700",  // 9 - Gold
                8 => "#FF0000",  // 8 - Red
                7 => "#CD5C5C",  // 7 - Light Red
                6 => "#0000FF",  // 6 - Blue
                5 => "#4169E1",  // 5 - Light Blue
                4 => "#000000",  // 4 - Black
                3 => "#36454F",  // 3 - Charcoal
                2 => "#FFFFFF",  // 2 - White
                1 => "#F5F5DC",  // 1 - Beige
                _ => "#808080"   // Miss/Other
            };
        }

        private MudBlazor.Color GetScoreButtonColor(int score)
        {
            return score switch
            {
                10 => MudBlazor.Color.Warning, // Yellow ish
                9 => MudBlazor.Color.Warning,
                8 or 7 => MudBlazor.Color.Error,
                6 or 5 => MudBlazor.Color.Info,
                4 or 3 => MudBlazor.Color.Dark,
                2 or 1 => MudBlazor.Color.Default,
                _ => MudBlazor.Color.Surface
            };
        }

        private async Task SetSelectedMode(string mode)
        {
            if (_selectedAnnotationIndex != -1)
            {
                // Update existing item
                await UpdateSelectedAnnotationType(10); // Target mode -> class 10
                _selectedMode = mode;
                // Important: Keep selection? Or deselect? Usually keeping is nice.
                await JSRuntime.InvokeVoidAsync("annotator.setSquareEnforced", "annotation-canvas", true);
            }
            else
            {
                _selectedMode = mode;
                await JSRuntime.InvokeVoidAsync("annotator.deselect", "annotation-canvas");
                // Target (mode='target') is Square Enforced
                if (_isLoaded) await JSRuntime.InvokeVoidAsync("annotator.setSquareEnforced", "annotation-canvas", true);
            }
            StateHasChanged();
        }

        private async Task SetSelectedScore(int score)
        {
            if (_selectedAnnotationIndex != -1)
            {
                 // Update existing item
                 int classId = score == 10 ? 11 : score;
                 await UpdateSelectedAnnotationType(classId);
                 
                 _selectedMode = "arrow";
                 _selectedArrowScore = score;
                 await JSRuntime.InvokeVoidAsync("annotator.setSquareEnforced", "annotation-canvas", true);
            }
            else
            {
                _selectedMode = "arrow";
                _selectedArrowScore = score;
                await JSRuntime.InvokeVoidAsync("annotator.deselect", "annotation-canvas");
                // Arrows are Square Enforced
                if (_isLoaded) await JSRuntime.InvokeVoidAsync("annotator.setSquareEnforced", "annotation-canvas", true);
            }
            StateHasChanged();
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (!string.IsNullOrEmpty(_displayImageSrc) && !_isLoaded)
            {
                _isLoaded = true;
                Console.WriteLine("[DatasetCreation] Initializing annotator with canvas and image elements.");
                await JSRuntime.InvokeVoidAsync("annotator.init", _canvasElement, _imageElement, _dotNetObjectReference);
                
                // Always enforce square
                await JSRuntime.InvokeVoidAsync("annotator.setSquareEnforced", "annotation-canvas", true);
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
                var rawBytes = await CameraService.ReadFileBytesAsync(imagePath);
                
                // resize and orient image immediately. Using 2048 to preserve quality for dataset while ensuring correct orientation.
                _originalImageBytes = await ImageProcessingService.ResizeImageAsync(rawBytes, 2048, 90, imagePath);
                
                // Display the PROCESSED (oriented) bytes
                _displayImageSrc = $"data:image/jpeg;base64,{Convert.ToBase64String(_originalImageBytes)}";
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
            int classId;
            if (_selectedMode == "target")
            {
                classId = 10; // Target Face is Class 10
            }
            else
            {
                // Mapping: Score 10 -> Class 11, others match score
                classId = _selectedArrowScore == 10 ? 11 : _selectedArrowScore;
            }

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
                color = GetHexColor(classId) // Force correct color
            };

            await JSRuntime.InvokeVoidAsync("annotator.addBox", "annotation-canvas", boxJs);
            
            // Set selection to new box
            _selectedAnnotationIndex = _annotations.Count - 1;
            
            // Enforce logic based on type (Always Square)
            bool squareEnforced = true;
            
            await JSRuntime.InvokeVoidAsync("annotator.setSquareEnforced", "annotation-canvas", squareEnforced);
            
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
                _selectedAnnotationIndex = index; // Keep track
                StateHasChanged();
            }
        }

        private void ClearAnnotations()
        {
            _annotations.Clear();
            JSRuntime.InvokeVoidAsync("annotator.clear", "annotation-canvas");
            _selectedAnnotationIndex = -1;
            StateHasChanged();
        }

        [JSInvokable]
        public void OnBoxSelected(int index)
        {
            _selectedAnnotationIndex = index;
            // When selecting, update the mode/score to match the selected item?
            // Optional, but might be helpful. For now, just tracking index for deletion.
            
            // Also need to set square enforcement based on the selected item's type
            if (index >= 0 && index < _annotations.Count)
            {
               var ann = _annotations[index];
               bool squareEnforced = true;
               JSRuntime.InvokeVoidAsync("annotator.setSquareEnforced", "annotation-canvas", squareEnforced);
            }
            
            StateHasChanged();
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

        private async Task UpdateSelectedAnnotationType(int newClassId)
        {
             if (_selectedAnnotationIndex >= 0 && _selectedAnnotationIndex < _annotations.Count)
             {
                 var ann = _annotations[_selectedAnnotationIndex];
                 ann.ClassId = newClassId;
                 
                 string color = GetHexColor(newClassId);
                 string label = _loadedClassLabels.Length > newClassId ? _loadedClassLabels[newClassId] : newClassId.ToString();
                 
                 if (newClassId == 10) label = "Target"; // Manual override for nice label
                 if (newClassId == 11) label = "10";
                 
                 await JSRuntime.InvokeVoidAsync("annotator.updateBoxStyle", "annotation-canvas", _selectedAnnotationIndex, color, label);
             }
        }

        private async Task UndoLastAnnotation()
        {
            if (_annotations.Any())
            {
                _annotations.RemoveAt(_annotations.Count - 1);
                await JSRuntime.InvokeVoidAsync("annotator.removeLastBox", "annotation-canvas");
                
                // If the removed one was selected, reset
                if (_selectedAnnotationIndex >= _annotations.Count) _selectedAnnotationIndex = -1;
                
                StateHasChanged();
            }
        }

        private async Task DeleteSelectedOrClear()
        {
            if (_selectedAnnotationIndex >= 0 && _selectedAnnotationIndex < _annotations.Count)
            {
                // Delete specific
                _annotations.RemoveAt(_selectedAnnotationIndex);
                await JSRuntime.InvokeVoidAsync("annotator.removeBox", "annotation-canvas", _selectedAnnotationIndex);
                _selectedAnnotationIndex = -1;
            }
            else
            {
                // Clear all
                _annotations.Clear();
                await JSRuntime.InvokeVoidAsync("annotator.clear", "annotation-canvas");
               _selectedAnnotationIndex = -1;
            }
            StateHasChanged();
        }

        private void Reset()
        {
            _originalImagePath = null;
            _displayImageSrc = string.Empty;
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
