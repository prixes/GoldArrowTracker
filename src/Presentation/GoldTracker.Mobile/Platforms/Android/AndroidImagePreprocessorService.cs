// Copyright (c) GoldArrowTracker. Licensed under the GNU AFFERO GENERAL PUBLIC LICENSE v3.0

using AndroidGraphics = global::Android.Graphics;
using Microsoft.ML.OnnxRuntime.Tensors;
using Archery.Shared.Services;

namespace GoldTracker.Mobile.Platforms.Android;

/// <summary>
/// High-performance Android implementation of IImagePreprocessor using native Bitmap APIs.
/// Provides 10-20x faster preprocessing than ImageSharp on Android devices.
/// </summary>
public class AndroidImagePreprocessorService : Archery.Shared.Services.IImagePreprocessor
{
    public PreprocessingResult Preprocess(byte[] imageBytes, int inputSize, string? filePath = null)
    {
        // 1. Decode bounds to get original dimensions and calculate sample size
        var options = new AndroidGraphics.BitmapFactory.Options { InJustDecodeBounds = true };
        AndroidGraphics.BitmapFactory.DecodeByteArray(imageBytes, 0, imageBytes.Length, options);
        
        int originalWidth = options.OutWidth;
        int originalHeight = options.OutHeight;

        if (originalWidth <= 0 || originalHeight <= 0)
        {
            throw new InvalidOperationException($"Invalid image dimensions: {originalWidth}x{originalHeight}");
        }

        // 2. Decode full bitmap with power-of-2 downsampling (extremely fast)
        options.InJustDecodeBounds = false;
        options.InSampleSize = CalculateInSampleSize(originalWidth, originalHeight, inputSize, inputSize);
        options.InPreferredConfig = AndroidGraphics.Bitmap.Config.Argb8888;
        
        var decodedBitmap = AndroidGraphics.BitmapFactory.DecodeByteArray(imageBytes, 0, imageBytes.Length, options);
        if (decodedBitmap == null) throw new InvalidOperationException("Failed to decode image on Android.");

        AndroidGraphics.Bitmap? rotatedBitmap = null;
        AndroidGraphics.Bitmap? inputBitmap = null;

        try
        {
            // 3. Handle EXIF rotation
            // NOTE: ApplyExifRotation disposes decodedBitmap if it creates a rotated copy!
            rotatedBitmap = GoldTracker.Mobile.Platforms.Android.AndroidImageProcessor.ApplyExifRotation(decodedBitmap, imageBytes, filePath);
            
            // If it returned a DIFFERENT bitmap, the original was disposed. Null it out.
            if (rotatedBitmap != decodedBitmap)
            {
                decodedBitmap = null; 
            }

            // Capture these metadata values BEFORE any recycling occurs
            int rotatedWidth = rotatedBitmap.Width;
            int rotatedHeight = rotatedBitmap.Height;

            // 4. Scaled to exact input size (e.g. 640x640) with letterboxing/padding
            inputBitmap = CreatePaddedBitmap(rotatedBitmap, inputSize);

            // 5. Extract pixels directly (fast)
            int[] pixels = new int[inputSize * inputSize];
            inputBitmap.GetPixels(pixels, 0, inputSize, 0, 0, inputSize, inputSize);

            // 6. Fill tensor and normalize
            var tensor = new DenseTensor<float>(new[] { 1, 3, inputSize, inputSize });
            
            // Calculate scales relative to the rotated/active image area (Logic must match CreatePaddedBitmap)
            float ratio = Math.Min((float)inputSize / rotatedWidth, (float)inputSize / rotatedHeight);
            float scale = 1.0f / ratio;

            int scaledWidth = (int)(rotatedWidth * ratio);
            int scaledHeight = (int)(rotatedHeight * ratio);
            
            float padX = (inputSize - scaledWidth) / 2.0f;
            float padY = (inputSize - scaledHeight) / 2.0f;

            for (int i = 0; i < pixels.Length; i++)
            {
                int color = pixels[i];
                int y = i / inputSize;
                int x = i % inputSize;

                // Android pixels are packed ARGB (0xAARRGGBB)
                tensor[0, 0, y, x] = ((color >> 16) & 0xFF) / 255.0f; // R
                tensor[0, 1, y, x] = ((color >> 8) & 0xFF) / 255.0f;  // G
                tensor[0, 2, y, x] = (color & 0xFF) / 255.0f;         // B
            }

            return new PreprocessingResult(tensor, rotatedWidth, rotatedHeight, scale, padX, padY);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AndroidImagePreprocessor] Error: {ex.Message}");
            throw;
        }
        finally
        {
            // 7. Safe Cleanup - Always recycle in finally block
            if (inputBitmap != null && !inputBitmap.IsRecycled) inputBitmap.Recycle();
            
            // If rotatedBitmap is a new instance (not decodedBitmap), recycle it.
            // If ApplyExifRotation replaced decodedBitmap, rotatedBitmap is that new instance.
            if (rotatedBitmap != null && rotatedBitmap != decodedBitmap && !rotatedBitmap.IsRecycled)
            {
                rotatedBitmap.Recycle();
            }

            // Only recycle decodedBitmap if it wasn't already recycled by ApplyExifRotation
            if (decodedBitmap != null && !decodedBitmap.IsRecycled)
            {
                decodedBitmap.Recycle();
            }
        }
    }

    private AndroidGraphics.Bitmap CreatePaddedBitmap(AndroidGraphics.Bitmap source, int targetSize)
    {
        // One-pass scale and pad using Canvas (Hardware Accelerated)
        var result = AndroidGraphics.Bitmap.CreateBitmap(targetSize, targetSize, AndroidGraphics.Bitmap.Config.Argb8888!);
        using var canvas = new AndroidGraphics.Canvas(result);
        canvas.DrawColor(global::Android.Graphics.Color.Black);

        float ratio = Math.Min((float)targetSize / source.Width, (float)targetSize / source.Height);
        int newWidth = (int)(source.Width * ratio);
        int newHeight = (int)(source.Height * ratio);

        float dx = (targetSize - newWidth) / 2f;
        float dy = (targetSize - newHeight) / 2f;
        
        var destRect = new AndroidGraphics.RectF(dx, dy, dx + newWidth, dy + newHeight);
        using var paint = new AndroidGraphics.Paint { FilterBitmap = true, AntiAlias = true };
        
        canvas.DrawBitmap(source, null, destRect, paint);
        return result;
    }

    private int CalculateInSampleSize(int width, int height, int reqWidth, int reqHeight)
    {
        int inSampleSize = 1;
        if (height > reqHeight || width > reqWidth)
        {
            int halfHeight = height / 2;
            int halfWidth = width / 2;
            while ((halfHeight / inSampleSize) >= reqHeight && (halfWidth / inSampleSize) >= reqWidth)
            {
                inSampleSize *= 2;
            }
        }
        return inSampleSize;
    }
}
