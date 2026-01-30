using Archery.Shared.Models;
using GoldTracker.Shared.UI.Services.Abstractions;

namespace GoldTracker.Shared.UI.Services
{
    public class DatasetExportService : IDatasetExportService
    {
        private readonly ICameraService _cameraService;
        private readonly IPlatformImageService _imageProcessingService;

        private const string ImagesDirName = "images";
        private const string LabelsDirName = "labels";

        public DatasetExportService(ICameraService cameraService, IPlatformImageService imageProcessingService)
        {
            _cameraService = cameraService;
            _imageProcessingService = imageProcessingService;
        }

        public async Task ExportDatasetAsync(byte[] originalImageBytes, List<DatasetAnnotation> annotations, string? filePath = null)
        {
            if (originalImageBytes == null || annotations == null || !annotations.Any())
            {
                return;
            }

            var hasPermission = await _cameraService.RequestStorageWritePermissionAsync();
            if (!hasPermission)
            {
                throw new UnauthorizedAccessException("Storage write permission denied.");
            }

            var timestamp = $"{DateTime.Now:yyyyMMdd_HHmmssfff}";

            // --- 1. Macro Model (Targets) ---
            var macroImageDir = Path.Combine("Export", "Macro_Model", ImagesDirName);
            
            // Filter for targets using INTERNAL ID (10)
            // Application Internal: 10 = Target, 11 = 10pts
            // Dataset Standard: 11 = Target, 10 = 10pts
            var targets = annotations.Where(a => a.ClassId == 10).ToList();
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
                var macroImagePath = await _cameraService.SaveImageAsync(originalImageBytes, $"{timestamp}.jpg", macroImageDir);
                if (!string.IsNullOrEmpty(macroImagePath))
                {
                    string macroLabelPath = GetLabelPath(macroImagePath, timestamp);
                    await _cameraService.WriteFileTextAsync(macroLabelPath, string.Join("\n", targetStrings));
                    _cameraService.TriggerMediaScanner(macroLabelPath);
                }
            }

            // --- 2. Micro Model (Arrows + Target Refinement) ---
            if (targets.Any())
            {
                 // Using Task.Run to offload heavy image processing (cropping)
                 await Task.Run(async () =>
                 {
                    // Full path to image directory for micro model
                    var microImageDir = Path.Combine("Export", "Micro_Model", ImagesDirName);
                    
                    double padding = 0.05; // 5% padding around target

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

                        byte[] cropBytes = await _imageProcessingService.CropImageAsync(
                            originalImageBytes,
                            cropStartX, cropStartY,
                            cropW, cropH,
                            filePath); // PASS filePath!

                        if (cropBytes == null || cropBytes.Length == 0) continue;

                        var microImagePath = await _cameraService.SaveImageAsync(cropBytes, $"{cropFileName}.jpg", microImageDir).ConfigureAwait(false);
                        if (string.IsNullOrEmpty(microImagePath)) continue;

                        var microStrings = new List<string>();
                        
                        // Add target itself relative to crop
                        // Mapping: Internal 10 (Target) -> Dataset 11
                        var tCenterX = (t.StartX + t.EndX) / 2.0;
                        var tCenterY = (t.StartY + t.EndY) / 2.0;
                        var tLocalX = (tCenterX - cropStartX) / cropW;
                        var tLocalY = (tCenterY - cropStartY) / cropH;
                        var tLocalW = tW_raw / cropW;
                        var tLocalH = tH_raw / cropH;
                        
                        microStrings.Add($"11 {tLocalX:F6} {tLocalY:F6} {tLocalW:F6} {tLocalH:F6}");

                        // Find arrows inside this crop
                        // Valid arrows are NOT Class 10 (Target)
                        var validArrows = annotations
                            .Where(a => a.ClassId != 10)
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
                            
                            // Mapping: Internal 11 (10pts) -> Dataset 10
                            // Others (9,8,7...) remain same
                            int datasetClassId = a.ClassId == 11 ? 10 : a.ClassId;
                            
                            microStrings.Add($"{datasetClassId} {x_local:F6} {y_local:F6} {w_local:F6} {h_local:F6}");
                        }

                        if (microStrings.Any())
                        {
                            string microLabelPath = GetLabelPath(microImagePath, cropFileName);
                            await _cameraService.WriteFileTextAsync(microLabelPath, string.Join("\n", microStrings));
                            _cameraService.TriggerMediaScanner(microLabelPath);
                        }
                    }
                 });
            }
        }

        private string GetLabelPath(string imagePath, string fileNameWithoutExt)
        {
            var directoryName = Path.GetDirectoryName(imagePath);
            if (directoryName != null && directoryName.EndsWith(ImagesDirName, StringComparison.OrdinalIgnoreCase))
            {
                 // Standard structure
                 var labelDirFull = directoryName.Replace(ImagesDirName, LabelsDirName, StringComparison.OrdinalIgnoreCase);
                 if (!Directory.Exists(labelDirFull))
                 {
                     Directory.CreateDirectory(labelDirFull);
                 }
                 return Path.Combine(labelDirFull, $"{fileNameWithoutExt}.txt");
            }
            
            // Fallback/Legacy structure
            var legacyPath = imagePath.Replace(".jpg", ".txt").Replace(ImagesDirName, LabelsDirName);
            
            // Ensure directory exists for label if we just computed it
            var checkDir = Path.GetDirectoryName(legacyPath);
            if (!string.IsNullOrEmpty(checkDir) && !Directory.Exists(checkDir))
            {
                 Directory.CreateDirectory(checkDir);
            }
            return legacyPath;
        }
    }
}
