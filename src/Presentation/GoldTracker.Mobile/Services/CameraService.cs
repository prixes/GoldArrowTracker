// Copyright (c) GoldArrowTracker. Licensed under the GNU AFFERO GENERAL PUBLIC LICENSE v3.0

using GoldTracker.Shared.UI.Services.Abstractions;

namespace GoldTracker.Mobile.Services;

/// <summary>
/// Service for capturing images from camera or photo library using MAUI MediaPicker.
/// </summary>
public class CameraService : ICameraService
{
    /// <summary>
    /// Captures a photo from the device camera.
    /// </summary>
    /// <returns>Path to the captured image, or null if cancelled</returns>
    public async Task<string?> CapturePhotoAsync()
    {
        try
        {
            // Check if media picker is supported
            if (!MediaPicker.Default.IsCaptureSupported)
            {
                return null;
            }

            var photo = await MediaPicker.Default.CapturePhotoAsync(
                new MediaPickerOptions
                {
                    Title = "Capture Archery Target"
                }
            );

            if (photo == null)
                return null;

            // Copy to app data directory for processing
            var appDataPath = FileSystem.AppDataDirectory;
            var fileName = $"target_{DateTime.Now:yyyyMMdd_HHmmss}.jpg";
            var localFilePath = Path.Combine(appDataPath, fileName);

            using var sourceStream = await photo.OpenReadAsync();
            using var localFile = File.Create(localFilePath);
            await sourceStream.CopyToAsync(localFile);

            return localFilePath;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Picks a photo from the device photo library.
    /// </summary>
    /// <returns>Path to the selected image, or null if cancelled</returns>
    public async Task<string?> PickPhotoAsync()
    {
        try
        {
            // Request read permissions
            var hasPermission = await RequestStorageReadPermissionAsync();
            if (!hasPermission)
            {
                return null;
            }

            var photo = (await MediaPicker.Default.PickPhotosAsync(
                new MediaPickerOptions
                {
                    Title = "Select Archery Target Photo"
                }
            ))?.FirstOrDefault();

            if (photo == null)
                return null;

            // Copy to app data directory for processing
            var appDataPath = FileSystem.AppDataDirectory;
            var fileName = $"target_{DateTime.Now:yyyyMMdd_HHmmss}_{Path.GetFileNameWithoutExtension(photo.FileName)}.jpg";
            var localFilePath = Path.Combine(appDataPath, fileName);

            using var sourceStream = await photo.OpenReadAsync();
            using var localFile = File.Create(localFilePath);
            await sourceStream.CopyToAsync(localFile);

            return localFilePath;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Picks a video or image from the device media library.
    /// </summary>
    /// <returns>Path to the selected media, or null if cancelled</returns>
    public async Task<string?> PickMediaAsync()
    {
        try
        {
            // Request read permissions
            var hasPermission = await RequestStorageReadPermissionAsync();
            if (!hasPermission)
            {
                return null;
            }

            var media = await FilePicker.Default.PickAsync(
                new PickOptions
                {
                    FileTypes = FilePickerFileType.Images,
                    PickerTitle = "Select an Archery Target Image"
                }
            );

            if (media == null)
                return null;

            // Copy to app data directory for processing
            var appDataPath = FileSystem.AppDataDirectory;
            var fileName = $"target_{DateTime.Now:yyyyMMdd_HHmmss}_{Path.GetFileNameWithoutExtension(media.FileName)}.jpg";
            var localFilePath = Path.Combine(appDataPath, fileName);

            using var sourceStream = await media.OpenReadAsync();
            using var localFile = File.Create(localFilePath);
            await sourceStream.CopyToAsync(localFile);

            return localFilePath;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Requests camera permission from the user.
    /// </summary>
    public async Task<bool> RequestCameraPermissionAsync()
    {
        try
        {
            var status = await Permissions.CheckStatusAsync<Permissions.Camera>();

            if (status != PermissionStatus.Granted)
            {
                status = await Permissions.RequestAsync<Permissions.Camera>();
            }

            return status == PermissionStatus.Granted;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Requests read storage permission from the user.
    /// </summary>
    public async Task<bool> RequestStorageReadPermissionAsync()
    {
        try
        {
            var status = await Permissions.CheckStatusAsync<Permissions.StorageRead>();
            if (status != PermissionStatus.Granted)
            {
                status = await Permissions.RequestAsync<Permissions.StorageRead>();
            }

            if (status != PermissionStatus.Granted) return false;

            var writeStatus = await Permissions.CheckStatusAsync<Permissions.StorageWrite>();
            if (writeStatus != PermissionStatus.Granted)
            {
                writeStatus = await Permissions.RequestAsync<Permissions.StorageWrite>();
            }

            return writeStatus == PermissionStatus.Granted;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Requests write storage permission from the user.
    /// </summary>
    public async Task<bool> RequestStorageWritePermissionAsync()
    {
        try
        {
            var status = await Permissions.CheckStatusAsync<Permissions.StorageWrite>();
            if (status != PermissionStatus.Granted)
            {
                status = await Permissions.RequestAsync<Permissions.StorageWrite>();
            }
            return status == PermissionStatus.Granted;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets a list of recently captured or imported target images.
    /// </summary>
    /// <returns>List of image file paths in app data directory</returns>
    public IEnumerable<string> GetStoredImages()
    {
        try
        {
            var appDataPath = FileSystem.AppDataDirectory;
            if (!Directory.Exists(appDataPath))
                return Enumerable.Empty<string>();

            var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif" };
            var images = Directory
                .GetFiles(appDataPath)
                .Where(f => imageExtensions.Contains(Path.GetExtension(f).ToLower()))
                .OrderByDescending(f => new FileInfo(f).CreationTime)
                .ToList();

            return images;
        }
        catch
        {
            return Enumerable.Empty<string>();
        }
    }

    /// <summary>
    /// Deletes an image file from app storage.
    /// </summary>
    public bool DeleteImage(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Saves an image to a specified subdirectory in a public folder.
    /// </summary>
    /// <param name="imageData">The image data as a byte array.</param>
    /// <param name="fileName">The name of the file to save.</param>
    /// <param name="subdirectory">The subdirectory to save the file in.</param>
    /// <returns>The full path to the saved file, or null if saving fails.</returns>
    public async Task<string?> SaveImageAsync(byte[] imageData, string fileName, string subdirectory)
    {
        try
        {
#if ANDROID
            var downloadsPath = Android.OS.Environment.GetExternalStoragePublicDirectory(Android.OS.Environment.DirectoryDownloads);
            var documentsPath = downloadsPath?.AbsolutePath ?? "/storage/emulated/0/Download";
            var subfolderPath = Path.Combine(documentsPath, subdirectory);
#else
            var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var subfolderPath = Path.Combine(documentsPath, subdirectory);
#endif

            if (!Directory.Exists(subfolderPath))
            {
                Console.WriteLine($"Creating directory: {subfolderPath}");
                Directory.CreateDirectory(subfolderPath);
            }

            var localFilePath = Path.Combine(subfolderPath, fileName);
            Console.WriteLine($"Writing file to: {localFilePath}");
            await File.WriteAllBytesAsync(localFilePath, imageData);
            Console.WriteLine($"Successfully wrote file to: {localFilePath}");
            
            TriggerMediaScanner(localFilePath);
            
            return localFilePath;
        }
        catch(Exception ex)
        {
            Console.WriteLine($"Error saving image: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Notifies the OS that a new file has been created so it appears in gallery/file explorers.
    /// </summary>
    public void TriggerMediaScanner(string filePath)
    {
        try
        {
#if ANDROID
            var context = Android.App.Application.Context;
            Android.Media.MediaScannerConnection.ScanFile(context, new string[] { filePath }, null, null);
#endif
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error triggering media scanner: {ex.Message}");
        }
    }

    public async Task<byte[]> ReadFileBytesAsync(string path)
    {
        if (!File.Exists(path)) return Array.Empty<byte>();
        return await File.ReadAllBytesAsync(path);
    }

    public async Task WriteFileTextAsync(string path, string content)
    {
        await File.WriteAllTextAsync(path, content);
    }

    public async Task<string> SaveInternalImageAsync(string fileName, byte[] bytes)
    {
        var folder = FileSystem.AppDataDirectory;
        if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
        // User session logic used "session_images" subdirectory sometimes?
        // Step 1289 used "session_images".
        // My implementation here assumes root AppData?
        // Wait, TargetCapture calls SaveInternalImageAsync.
        // It passes just filename.
        // If I want to match User Logic (session_images), I should subdirectory it.
        // But the previous "SaveInternalImageAsync" usage in TargetCapture I wrote (Step 1352) passed just filename.
        // I will change this implementation to Put it in "session_images" automagically?
        // Or leave it in root AppData (Step 1265 captured images go to root AppData).
        // Let's keep it root for simplicity unless conflict?
        // TargetCapture (User code) used "session_images".
        // I'll stick to root AppData for "SaveInternalImageAsync" to match "CapturePhotoAsync" behavior.
        
        var path = Path.Combine(folder, fileName);
        await File.WriteAllBytesAsync(path, bytes);
        return path;
    }
}