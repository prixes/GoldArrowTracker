
using Archery.Shared.Models;
using GoldTracker.Shared.UI.Services.Abstractions;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System.IO;
using System;

namespace GoldTracker.Shared.UI.Services
{
    public class DatasetExportService : IDatasetExportService
    {
        private readonly ICameraService _cameraService;
        private readonly IPlatformImageService _imageProcessingService;

        public DatasetExportService(ICameraService cameraService, IPlatformImageService imageProcessingService)
        {
            _cameraService = cameraService;
            _imageProcessingService = imageProcessingService;
        }

        public async Task ExportDatasetAsync(byte[] originalImageBytes, List<DatasetAnnotation> annotations)
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
            var macroImageDir = Path.Combine("Export", "Macro_Model", "images");
            
            // Filter for targets (Class 11 represents Target Face)
            var targets = annotations.Where(a => a.ClassId == 11).ToList();
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
                    // Calculate label path based on image path (standard YOLO structure)
                    // Assumption: SaveImageAsync creates the directory structure
                    // On Mobile: storage/Export/Macro_Model/images/file.jpg -> storage/Export/Macro_Model/labels/file.txt
                    // On Web: This logic might be tricky as paths are virtual/downloads.
                    // But CameraService.WriteFileTextAsync should handle it if path is provided.
                    // However, we rely on string replacement which implies knowledge of the structure returned by SaveImageAsync.
                    // SaveImageAsync returns full path.
                    
                    var labelDir = Path.Combine("Export", "Macro_Model", "labels");
                    // We can't easily deduce the full label path if we don't know the root.
                    // But SaveImageAsync returns the *full local path*.
                    // So we can try to replace "images" with "labels".
                    
                    string macroLabelPath;
                    var directoryName = Path.GetDirectoryName(macroImagePath);
                    if (directoryName != null && directoryName.EndsWith("images", StringComparison.OrdinalIgnoreCase))
                    {
                         // Standard structure
                         var labelDirFull = directoryName.Replace("images", "labels", StringComparison.OrdinalIgnoreCase);
                         if (!Directory.Exists(labelDirFull))
                         {
                             try { Directory.CreateDirectory(labelDirFull); } catch {} // Try create if local
                         }
                         macroLabelPath = Path.Combine(labelDirFull, $"{timestamp}.txt");
                    }
                    else
                    {
                        // Fallback: just put it in a separate folder relative to whatever root we can guess?
                        // Or just invoke WriteFileTextAsync with our relative intended path?
                        // CameraService.WriteFileTextAsync logic:
                        // Web: ignores path directory usually, just downloads "filename".
                        // Mobile: WriteFileTextAsync implementation simply calls File.WriteAllTextAsync(path, content).
                        // So we MUST provide a valid full path for Mobile.
                        
                        // If directoryName didn't end in images, maybe we just swap the last part manually?
                        // Let's rely on the replace method for now as we enforced the creation path above.
                        macroLabelPath = macroImagePath.Replace(".jpg", ".txt").Replace("images", "labels");
                        
                        // Ensure directory exists for label if we just computed it
                        var checkDir = Path.GetDirectoryName(macroLabelPath);
                         if (!Directory.Exists(checkDir))
                         {
                             try { Directory.CreateDirectory(checkDir); } catch {} 
                         }
                    }

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
                    var microImageDir = Path.Combine("Export", "Micro_Model", "images");
                    
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
                            cropW, cropH);

                        if (cropBytes == null || cropBytes.Length == 0) continue;

                        var microImagePath = await _cameraService.SaveImageAsync(cropBytes, $"{cropFileName}.jpg", microImageDir).ConfigureAwait(false);
                        if (string.IsNullOrEmpty(microImagePath)) continue;

                        var microStrings = new List<string>();
                        
                        // Add target itself (class 11) relative to crop
                        var tCenterX = (t.StartX + t.EndX) / 2.0;
                        var tCenterY = (t.StartY + t.EndY) / 2.0;
                        var tLocalX = (tCenterX - cropStartX) / cropW;
                        var tLocalY = (tCenterY - cropStartY) / cropH;
                        var tLocalW = tW_raw / cropW;
                        var tLocalH = tH_raw / cropH;
                        
                        // 11 is Target Face class
                        microStrings.Add($"11 {tLocalX:F6} {tLocalY:F6} {tLocalW:F6} {tLocalH:F6}");

                        // Find arrows inside this crop
                        var validArrows = annotations
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
                            // Calculate label path similar to above
                            string microLabelPath;
                            var dirName = Path.GetDirectoryName(microImagePath);
                             if (dirName != null && dirName.EndsWith("images", StringComparison.OrdinalIgnoreCase))
                            {
                                 var labelDirFull = dirName.Replace("images", "labels", StringComparison.OrdinalIgnoreCase);
                                 if (!Directory.Exists(labelDirFull)) try { Directory.CreateDirectory(labelDirFull); } catch {}
                                 microLabelPath = Path.Combine(labelDirFull, $"{cropFileName}.txt");
                            }
                            else
                            {
                                microLabelPath = microImagePath.Replace(".jpg", ".txt").Replace("images", "labels");
                                var checkDir = Path.GetDirectoryName(microLabelPath);
                                if (!Directory.Exists(checkDir)) try { Directory.CreateDirectory(checkDir); } catch {} 
                            }
                            
                            await _cameraService.WriteFileTextAsync(microLabelPath, string.Join("\n", microStrings));
                            _cameraService.TriggerMediaScanner(microLabelPath);
                        }
                    }
                 });
            }
        }
    }
}
