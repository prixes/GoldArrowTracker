using GoldTracker.Mobile.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace GoldTracker.Mobile.Components.Pages
{
    public partial class DatasetCreation : IDisposable
    {
        [Inject] private CameraService CameraService { get; set; } = default!;
        [Inject] private IJSRuntime JSRuntime { get; set; } = default!;
        [Inject] private NavigationManager Navigation { get; set; } = default!;
        [Inject] private ISnackbar Snackbar { get; set; } = default!;
        [Inject] private ImageProcessingService ImageProcessingService { get; set; } = default!;

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
        private List<Annotation> _annotations = new();
        private string[] _loadedClassLabels = Array.Empty<string>();
        private string[] _loadedClassColors = Array.Empty<string>();

        public class Annotation
        {
            public int ClassId { get; set; }
            public double StartX { get; set; }
            public double StartY { get; set; }
            public double EndX { get; set; }
            public double EndY { get; set; }
        }

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
            await JSRuntime.InvokeVoidAsync("annotator.deselect");
            StateHasChanged();
        }

        private async Task SetSelectedScore(int score)
        {
            _selectedMode = "arrow";
            _selectedArrowScore = score;
            await JSRuntime.InvokeVoidAsync("annotator.deselect");
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
                _originalImageBytes = await System.IO.File.ReadAllBytesAsync(imagePath);
                _originalImageBase64 = Convert.ToBase64String(_originalImageBytes);
                Console.WriteLine($"[DatasetCreation] Image loaded. Base64 length: {_originalImageBase64.Length}");
            }
            catch (Exception ex)
            {
                Snackbar.Add($"Error loading image: {ex.Message}", Severity.Error);
                _originalImagePath = null;
                _originalImageBytes = null;
                Console.WriteLine($"[DatasetCreation] Error loading image: {ex.Message}");
            }
        }

        [JSInvokable]
        public async Task AddBox(double startX, double startY, double endX, double endY)
        {
            int classId = _selectedMode == "target" ? 11 : _selectedArrowScore;

            var annotation = new Annotation
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

            await JSRuntime.InvokeVoidAsync("annotator.addBox", boxJs);
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
                var hasPermission = await CameraService.RequestStorageWritePermissionAsync();
                if (!hasPermission)
                {
                    Snackbar.Add("Storage permission denied.", Severity.Error);
                    return;
                }

                // Show processing message
                Snackbar.Add("Processing images... This may take a moment.", Severity.Info);

                var timestamp = $"{DateTime.Now:yyyyMMdd_HHmmssfff}";

                // --- 1. Macro Model (Targets) ---
                var macroImageDir = Path.Combine("Export", "Macro_Model", "images");
                var macroLabelDir = Path.Combine("Export", "Macro_Model", "labels");

                // Filter for targets (Class 11)
                var targets = _annotations.Where(a => a.ClassId == 11).ToList();
                var targetStrings = new List<string>();
                foreach (var t in targets)
                {
                    var x_center = (t.StartX + t.EndX) / 2.0;
                    var y_center = (t.StartY + t.EndY) / 2.0;
                    var width = t.EndX - t.StartX;
                    var height = t.EndY - t.StartY;
                    targetStrings.Add($"0 {x_center:F6} {y_center:F6} {width:F6} {height:F6}");
                }

                if (targetStrings.Any())
                {
                    var macroImagePath = await CameraService.SaveImageAsync(_originalImageBytes, $"{timestamp}.jpg", macroImageDir);
                    if (!string.IsNullOrEmpty(macroImagePath))
                    {
                        var macroLabelPath = Path.Combine(Path.GetDirectoryName(macroImagePath)!.Replace("images", "labels"), $"{timestamp}.txt");
                        Directory.CreateDirectory(Path.GetDirectoryName(macroLabelPath)!);
                        await File.WriteAllTextAsync(macroLabelPath, string.Join("\n", targetStrings));
                        CameraService.TriggerMediaScanner(macroLabelPath);
                    }
                }

                // --- 2. Micro Model (Arrows + Target Refinement) ---
                if (targets.Any())
                {
                    await Task.Run(async () =>
                    {
                        try
                        {
                            Console.WriteLine($"[DatasetCreation] Starting micro model processing for {targets.Count} target(s)...");

                            var microImageDir = Path.Combine("Export", "Micro_Model", "images");
                            var microLabelDir = Path.Combine("Export", "Micro_Model", "labels");

                            double padding = 0.05; // 5% padding

#if ANDROID
                            using var sourceBitmap = await GoldTracker.Mobile.Platforms.Android.AndroidImageProcessor.LoadBitmapAsync(_originalImageBytes, _originalImagePath);
                            if (sourceBitmap == null) return;
#else
                            using var sourceImage = SixLabors.ImageSharp.Image.Load<Rgba32>(_originalImageBytes);
                            sourceImage.Mutate(x => x.AutoOrient());
#endif

                            int cropIndex = 0;
                            foreach (var t in targets)
                            {
                                cropIndex++;
                                var cropFileName = $"{timestamp}_crop{cropIndex}";

                                double tW_raw = t.EndX - t.StartX;
                                double tH_raw = t.EndY - t.StartY;

                                double padX = tW_raw * padding;
                                double padY = tH_raw * padding;

                                double cropStartX = Math.Max(0, t.StartX - padX);
                                double cropStartY = Math.Max(0, t.StartY - padY);
                                double cropEndX = Math.Min(1, t.EndX + padX);
                                double cropEndY = Math.Min(1, t.EndY + padY);

                                double cropW = cropEndX - cropStartX;
                                double cropH = cropEndY - cropStartY;

                                byte[] cropBytes;
#if ANDROID
                                cropBytes = await GoldTracker.Mobile.Platforms.Android.AndroidImageProcessor.CropBitmapAsync(
                                    sourceBitmap,
                                    cropStartX, cropStartY,
                                    cropW, cropH,
                                    quality: 100);
#else
                                cropBytes = await ImageProcessingService.CropImageAsync(
                                    sourceImage,
                                    cropStartX, cropStartY,
                                    cropW, cropH).ConfigureAwait(false);
#endif

                                if (cropBytes.Length == 0) continue;

                                var microImagePath = await CameraService.SaveImageAsync(cropBytes, $"{cropFileName}.jpg", microImageDir).ConfigureAwait(false);
                                if (string.IsNullOrEmpty(microImagePath)) continue;

                                var microStrings = new List<string>();
                                var tCenterX = (t.StartX + t.EndX) / 2.0;
                                var tCenterY = (t.StartY + t.EndY) / 2.0;
                                var tLocalX = (tCenterX - cropStartX) / cropW;
                                var tLocalY = (tCenterY - cropStartY) / cropH;
                                var tLocalW = tW_raw / cropW;
                                var tLocalH = tH_raw / cropH;
                                microStrings.Add($"11 {tLocalX:F6} {tLocalY:F6} {tLocalW:F6} {tLocalH:F6}");

                                var validArrows = _annotations
                                    .Where(a => a.ClassId != 11)
                                    .Where(a =>
                                    {
                                        var cX = (a.StartX + a.EndX) / 2.0;
                                        var cY = (a.StartY + a.EndY) / 2.0;
                                        return cX >= cropStartX && cX <= cropEndX && cY >= cropStartY && cY <= cropEndY;
                                    });

                                foreach (var a in validArrows)
                                {
                                    var aCenterX = (a.StartX + a.EndX) / 2.0;
                                    var aCenterY = (a.StartY + a.EndY) / 2.0;
                                    var aW = a.EndX - a.StartX;
                                    var aH = a.EndY - a.StartY;
                                    var x_local = (aCenterX - cropStartX) / cropW;
                                    var y_local = (aCenterY - cropStartY) / cropH;
                                    var w_local = aW / cropW;
                                    var h_local = aH / cropH;
                                    microStrings.Add($"{a.ClassId} {x_local:F6} {y_local:F6} {w_local:F6} {h_local:F6}");
                                }

                                if (microStrings.Any())
                                {
                                    var microLabelPath = Path.Combine(Path.GetDirectoryName(microImagePath)!.Replace("images", "labels"), $"{cropFileName}.txt");
                                    Directory.CreateDirectory(Path.GetDirectoryName(microLabelPath)!);
                                    await File.WriteAllTextAsync(microLabelPath, string.Join("\n", microStrings)).ConfigureAwait(false);
                                    CameraService.TriggerMediaScanner(microLabelPath);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[DatasetCreation] ERROR in micro processing: {ex.Message}");
                            throw;
                        }
                    });
                }

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
                JSRuntime.InvokeVoidAsync("annotator.removeLastBox");
                StateHasChanged();
            }
        }

        private void ClearAnnotations()
        {
            _annotations.Clear();
            JSRuntime.InvokeVoidAsync("annotator.clear");
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
                JSRuntime.InvokeVoidAsync("annotator.destroy");
            }
        }
    }
}
