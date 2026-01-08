// Copyright (c) GoldArrowTracker. Licensed under the GNU AFFERO GENERAL PUBLIC LICENSE v3.0

namespace GoldTracker.Mobile.Services;

/// <summary>
/// Service for capturing images from camera or photo library using MAUI MediaPicker.
/// </summary>
public class CameraService
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
            // For Android 13+ (API 33+), MEDIA_IMAGES permission
            // For older versions, READ_EXTERNAL_STORAGE
            var status = await Permissions.CheckStatusAsync<Permissions.StorageRead>();

            if (status != PermissionStatus.Granted)
            {
                status = await Permissions.RequestAsync<Permissions.StorageRead>();
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
}
