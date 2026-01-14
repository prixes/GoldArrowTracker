// Copyright (c) GoldArrowTracker. Licensed under the GNU AFFERO GENERAL PUBLIC LICENSE v3.0

using AndroidGraphics = global::Android.Graphics;
using AndroidMedia = global::Android.Media;
using Archery.Shared.Models;

namespace GoldTracker.Mobile.Platforms.Android;

/// <summary>
/// Fast Android-native image processing using hardware-accelerated Bitmap APIs.
/// This is MUCH faster than ImageSharp on Android (10-20x faster).
/// </summary>
public static class AndroidImageProcessor
{
    /// <summary>
    /// Loads an image from bytes and returns a Bitmap.
    /// Uses Android's native BitmapFactory which is hardware-accelerated.
    /// Applies EXIF rotation to correct the physically rotated JPEG data.
    /// </summary>
    public static async Task<AndroidGraphics.Bitmap?> LoadBitmapAsync(byte[] imageBytes, string? filePath = null)
    {
        return await Task.Run(() =>
        {
            try
            {
                // Decode RAW bitmap
                var options = new AndroidGraphics.BitmapFactory.Options
                {
                    InPreferredConfig = AndroidGraphics.Bitmap.Config.Argb8888
                };

                var bitmap = AndroidGraphics.BitmapFactory.DecodeByteArray(imageBytes, 0, imageBytes.Length, options);
                
                if (bitmap == null)
                {
                    Console.WriteLine($"[AndroidImageProcessor] Failed to decode bitmap");
                    return null;
                }
                
                // Apply EXIF rotation
                bitmap = ApplyExifRotation(bitmap, imageBytes, filePath);
                
                return bitmap;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AndroidImageProcessor] Error loading bitmap: {ex.Message}");
                return null;
            }
        });
    }

    /// <summary>
    /// Apply EXIF rotation to correct physically rotated JPEG pixel data.
    /// </summary>
    private static AndroidGraphics.Bitmap ApplyExifRotation(AndroidGraphics.Bitmap bitmap, byte[] imageBytes, string? filePath = null)
    {
        try
        {
            AndroidMedia.ExifInterface? exif = null;
            
            if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
            {
                exif = new AndroidMedia.ExifInterface(filePath);
            }
            else
            {
                using var stream = new MemoryStream(imageBytes);
                exif = new AndroidMedia.ExifInterface(stream);
            }

            using (exif)
            {
                var orientation = exif.GetAttributeInt(
                    AndroidMedia.ExifInterface.TagOrientation,
                    (int)AndroidMedia.Orientation.Normal);

                if (orientation == (int)AndroidMedia.Orientation.Normal || orientation == (int)AndroidMedia.Orientation.Undefined || orientation == 0)
                {
                    return bitmap;
                }

                Console.WriteLine($"[AndroidImageProcessor] Correcting image orientation (Tag: {orientation})");

                var matrix = new AndroidGraphics.Matrix();
                
                switch (orientation)
                {
                    case (int)AndroidMedia.Orientation.Rotate90: // 6
                        matrix.PostRotate(90);
                        break;
                    case (int)AndroidMedia.Orientation.Rotate180: // 3
                        matrix.PostRotate(180);
                        break;
                    case (int)AndroidMedia.Orientation.Rotate270: // 8
                        matrix.PostRotate(270);
                        break;
                    case (int)AndroidMedia.Orientation.FlipHorizontal: // 2
                        matrix.PreScale(-1, 1);
                        break;
                    case (int)AndroidMedia.Orientation.FlipVertical: // 4
                        matrix.PreScale(1, -1);
                        break;
                    default:
                        return bitmap;
                }

                var rotatedBitmap = AndroidGraphics.Bitmap.CreateBitmap(bitmap, 0, 0, bitmap.Width, bitmap.Height, matrix, true);
                
                if (rotatedBitmap != null && rotatedBitmap != bitmap)
                {
                    bitmap.Dispose();
                    return rotatedBitmap;
                }
            }
            
            return bitmap;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AndroidImageProcessor] Error reading EXIF: {ex.Message}");
            return bitmap;
        }
    }

    /// <summary>
    /// Crops a bitmap based on normalized coordinates (0-1).
    /// Uses hardware-accelerated Canvas operations.
    /// </summary>
    public static async Task<byte[]> CropBitmapAsync(AndroidGraphics.Bitmap sourceBitmap, double startXNorm, double startYNorm, double widthNorm, double heightNorm, int quality = 60)
    {
        return await Task.Run(() =>
        {
            try
            {
                // Calculate pixel coordinates
                int x = (int)(startXNorm * sourceBitmap.Width);
                int y = (int)(startYNorm * sourceBitmap.Height);
                int w = (int)(widthNorm * sourceBitmap.Width);
                int h = (int)(heightNorm * sourceBitmap.Height);

                // Ensure bounds
                x = Math.Max(0, x);
                y = Math.Max(0, y);
                w = Math.Min(w, sourceBitmap.Width - x);
                h = Math.Min(h, sourceBitmap.Height - y);

                if (w <= 0 || h <= 0)
                {
                    Console.WriteLine($"[AndroidImageProcessor] Invalid crop dimensions: w={w}, h={h}");
                    return Array.Empty<byte>();
                }

                // Create cropped bitmap using CreateBitmap (very fast, hardware-accelerated)
                using var croppedBitmap = AndroidGraphics.Bitmap.CreateBitmap(sourceBitmap, x, y, w, h);
                
                if (croppedBitmap == null)
                {
                    Console.WriteLine($"[AndroidImageProcessor] Failed to create cropped bitmap");
                    return Array.Empty<byte>();
                }

                // Encode to JPEG
                using var stream = new MemoryStream();
                var success = croppedBitmap.Compress(AndroidGraphics.Bitmap.CompressFormat.Jpeg!, quality, stream);
                
                if (!success)
                {
                    Console.WriteLine($"[AndroidImageProcessor] Failed to compress bitmap");
                    return Array.Empty<byte>();
                }

                return stream.ToArray();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AndroidImageProcessor] Error cropping bitmap: {ex.Message}");
                return Array.Empty<byte>();
            }
        });
    }

    /// <summary>
    /// Resizes an image to fit within the specified maximum dimension while maintaining aspect ratio.
    /// Uses hardware-accelerated Bitmap scaling.
    /// </summary>
    public static async Task<byte[]> ResizeImageAsync(byte[] imageBytes, int maxDimension, int quality = 80)
    {
        return await Task.Run(() =>
        {
            try
            {
                // 1. Decode bounds only first to check dimensions (optimization)
                var options = new AndroidGraphics.BitmapFactory.Options
                {
                    InJustDecodeBounds = true
                };
                AndroidGraphics.BitmapFactory.DecodeByteArray(imageBytes, 0, imageBytes.Length, options);
                
                int originalWidth = options.OutWidth;
                int originalHeight = options.OutHeight;
                
                // 2. Calculate target dimensions
                if (originalWidth <= 0 || originalHeight <= 0)
                {
                    Console.WriteLine($"[AndroidImageProcessor] Invalid image dimensions: {originalWidth}x{originalHeight}");
                    return Array.Empty<byte>();
                }

                float ratio = Math.Min((float)maxDimension / originalWidth, (float)maxDimension / originalHeight);
                
                // If image is already smaller than max dimension, we still proceed to ensure rotation is fixed
                // and to avoid issues with different byte formats returned to the UI.
                
                int newWidth = (int)(originalWidth * ratio);
                int newHeight = (int)(originalHeight * ratio);
                
                if (ratio >= 1.0f) 
                {
                    newWidth = originalWidth;
                    newHeight = originalHeight;
                }
                
                // Ensure at least 1px
                newWidth = Math.Max(1, newWidth);
                newHeight = Math.Max(1, newHeight);
                
                // 3. Load full bitmap (with subsampling if possible for memory efficiency)
                // Calculate InSampleSize to reduce memory usage during decode
                options.InJustDecodeBounds = false;
                options.InSampleSize = CalculateInSampleSize(options, newWidth, newHeight);
                options.InPreferredConfig = AndroidGraphics.Bitmap.Config.Argb8888;
                
                var originalBitmap = AndroidGraphics.BitmapFactory.DecodeByteArray(imageBytes, 0, imageBytes.Length, options);
                
                if (originalBitmap == null) return Array.Empty<byte>();

                // Handle Rotation
                originalBitmap = ApplyExifRotation(originalBitmap, imageBytes);

                // Recalculate dimensions after rotation because width/height might have swapped
                // If we rotated 90 degrees, Width/Height swapped.
                if (originalBitmap.Width > maxDimension || originalBitmap.Height > maxDimension)
                {
                   float ratioW = (float)maxDimension / originalBitmap.Width;
                   float ratioH = (float)maxDimension / originalBitmap.Height;
                   ratio = Math.Min(ratioW, ratioH);
                   
                   newWidth = (int)(originalBitmap.Width * ratio);
                   newHeight = (int)(originalBitmap.Height * ratio);
                }
                else
                {
                   newWidth = originalBitmap.Width;
                   newHeight = originalBitmap.Height;
                }

                // 4. Create scaled bitmap
                // Filter=true for better quality
                var scaledBitmap = AndroidGraphics.Bitmap.CreateScaledBitmap(originalBitmap, newWidth, newHeight, true);
                
                // Dispose original if it's different
                if (originalBitmap != scaledBitmap && originalBitmap != null)
                {
                    originalBitmap.Recycle();
                    originalBitmap.Dispose();
                }
                
                if (scaledBitmap == null) return Array.Empty<byte>();

                // 5. Compress to Byte Array
                using var stream = new MemoryStream();
                // We are already inside Task.Run from line 188, so we can call synchronous Compress directly.
                scaledBitmap.Compress(AndroidGraphics.Bitmap.CompressFormat.Jpeg, quality, stream);
                
                if (scaledBitmap != null)
                {
                    scaledBitmap.Recycle();
                    scaledBitmap.Dispose();
                }
                
                return stream.ToArray();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AndroidImageProcessor] Error resizing image: {ex.Message}");
                return Array.Empty<byte>();
            }
        });
    }

    /// <summary>
    /// Draws analysis detections onto an image using native Android Canvas.
    /// This is EXTREMELY fast compared to ImageSharp.
    /// </summary>
    public static async Task<byte[]> DrawDetectionsAsync(byte[] imageBytes, TargetAnalysisResult analysisResult, int quality = 80)
    {
        return await Task.Run(() =>
        {
            try
            {
                var options = new AndroidGraphics.BitmapFactory.Options
                {
                    InMutable = true, // We want to draw on it
                    InPreferredConfig = AndroidGraphics.Bitmap.Config.Argb8888
                };

                var bitmap = AndroidGraphics.BitmapFactory.DecodeByteArray(imageBytes, 0, imageBytes.Length, options);
                if (bitmap == null) return imageBytes;

                // Ensure bitmap is mutable (DecodeByteArray might return immutable depending on options)
                AndroidGraphics.Bitmap mutableBitmap;
                if (!bitmap.IsMutable)
                {
                    mutableBitmap = bitmap.Copy(AndroidGraphics.Bitmap.Config.Argb8888!, true)!;
                    bitmap.Recycle();
                    bitmap.Dispose();
                }
                else
                {
                    mutableBitmap = bitmap;
                }

                using var canvas = new AndroidGraphics.Canvas(mutableBitmap);
                
                // Pains for drawing
                using var arrowPaint = new AndroidGraphics.Paint { Color = AndroidGraphics.Color.Green, StrokeWidth = 3 };
                arrowPaint.SetStyle(AndroidGraphics.Paint.Style.Stroke);
                
                using var targetPaint = new AndroidGraphics.Paint { Color = AndroidGraphics.Color.Cyan, StrokeWidth = 4 };
                targetPaint.SetStyle(AndroidGraphics.Paint.Style.Stroke);
                
                using var pointPaint = new AndroidGraphics.Paint { Color = AndroidGraphics.Color.Red, StrokeWidth = 2 };
                pointPaint.SetStyle(AndroidGraphics.Paint.Style.Stroke);

                using var shadowPaint = new AndroidGraphics.Paint { Color = AndroidGraphics.Color.Black, StrokeWidth = 2 };
                shadowPaint.SetStyle(AndroidGraphics.Paint.Style.Stroke);
                shadowPaint.Alpha = 150;
                
                using var textPaint = new AndroidGraphics.Paint { Color = AndroidGraphics.Color.White, TextSize = 28, FakeBoldText = true };
                textPaint.SetShadowLayer(2, 1, 1, AndroidGraphics.Color.Black);

                // 1. Draw raw detections (rings only, no text for arrows as we'll draw it with the score)
                foreach (var detection in analysisResult.Detections)
                {
                    if (detection.IsTargetFace) continue; // Don't draw raw target detection

                    // Ellipse for detection
                    var rect = new AndroidGraphics.RectF(
                        detection.X - detection.Width / 2, 
                        detection.Y - detection.Height / 2, 
                        detection.X + detection.Width / 2, 
                        detection.Y + detection.Height / 2);
                    
                    canvas.DrawOval(rect, shadowPaint);
                    canvas.DrawOval(rect, arrowPaint);
                }

                // 2. Draw calculated target center/radius
                if (analysisResult.TargetRadius > 0)
                {
                    // Center crosshair
                    canvas.DrawCircle(analysisResult.TargetCenter.X, analysisResult.TargetCenter.Y, 15, targetPaint);
                    canvas.DrawLine(analysisResult.TargetCenter.X - 20, analysisResult.TargetCenter.Y, analysisResult.TargetCenter.X + 20, analysisResult.TargetCenter.Y, targetPaint);
                    canvas.DrawLine(analysisResult.TargetCenter.X, analysisResult.TargetCenter.Y - 20, analysisResult.TargetCenter.X, analysisResult.TargetCenter.Y + 20, targetPaint);
                    
                    // Outer circle
                    canvas.DrawCircle(analysisResult.TargetCenter.X, analysisResult.TargetCenter.Y, analysisResult.TargetRadius, targetPaint);
                }

                // 3. Draw arrows and scores
                foreach (var arrow in analysisResult.ArrowScores)
                {
                    // Draw a thin red plus sign at the arrow center
                    float x = arrow.Detection.CenterX;
                    float y = arrow.Detection.CenterY;
                    float size = 12; // Larger for visibility
                    
                    // Draw with a very thin shadow for contrast
                    canvas.DrawLine(x - size - 1, y, x + size + 1, y, shadowPaint);
                    canvas.DrawLine(x, y - size - 1, x, y + size + 1, shadowPaint);
                    
                    canvas.DrawLine(x - size, y, x + size, y, pointPaint);
                    canvas.DrawLine(x, y - size, x, y + size, pointPaint);
                    
                    var scoreText = $"{arrow.Points} ({arrow.Detection.Confidence:P0})";
                    canvas.DrawText(scoreText, arrow.Detection.CenterX + 25, arrow.Detection.CenterY - 25, textPaint);
                }

                // Compress back to bytes
                using var stream = new MemoryStream();
                mutableBitmap.Compress(AndroidGraphics.Bitmap.CompressFormat.Jpeg!, quality, stream);
                
                mutableBitmap.Recycle();
                mutableBitmap.Dispose();

                return stream.ToArray();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AndroidImageProcessor] Error drawing detections: {ex.Message}");
                return imageBytes;
            }
        });
    }

    private static int CalculateInSampleSize(AndroidGraphics.BitmapFactory.Options options, int reqWidth, int reqHeight)
    {
        // Raw height and width of image
        int height = options.OutHeight;
        int width = options.OutWidth;
        int inSampleSize = 1;

        if (height > reqHeight || width > reqWidth)
        {
            int halfHeight = height / 2;
            int halfWidth = width / 2;

            // Calculate the largest inSampleSize value that is a power of 2 and keeps both
            // height and width larger than the requested height and width.
            while ((halfHeight / inSampleSize) >= reqHeight && (halfWidth / inSampleSize) >= reqWidth)
            {
                inSampleSize *= 2;
            }
        }

        return inSampleSize;
    }
}
