// Copyright (c) GoldArrowTracker. Licensed under the GNU AFFERO GENERAL PUBLIC LICENSE v3.0

namespace GoldTracker.Mobile.Services;

/// <summary>
/// Quick reference for CameraService methods
/// </summary>
public static class CameraServiceReference
{
    /*
     * PHOTO CAPTURE EXAMPLES
     * ======================
     * 
     * 1. Capture from Camera
     *    ==================
     *    var imagePath = await cameraService.CapturePhotoAsync();
     *    if (!string.IsNullOrEmpty(imagePath))
     *    {
     *        // Process image at imagePath
     *    }
     * 
     * 
     * 2. Pick from Photo Library
     *    =======================
     *    var imagePath = await cameraService.PickPhotoAsync();
     *    if (!string.IsNullOrEmpty(imagePath))
     *    {
     *        // Process image at imagePath
     *    }
     * 
     * 
     * 3. Browse Device Storage
     *    =====================
     *    var imagePath = await cameraService.PickMediaAsync();
     *    if (!string.IsNullOrEmpty(imagePath))
     *    {
     *        // Process image at imagePath
     *    }
     * 
     * 
     * PERMISSION HANDLING
     * ===================
     * 
     * 1. Check Camera Permission
     *    =======================
     *    bool hasCameraAccess = await cameraService.RequestCameraPermissionAsync();
     *    if (!hasCameraAccess)
     *    {
     *        // Show error to user
     *    }
     * 
     * 
     * 2. Check Storage Permission
     *    ========================
     *    bool hasStorageAccess = await cameraService.RequestStorageReadPermissionAsync();
     *    if (!hasStorageAccess)
     *    {
     *        // Show error to user
     *    }
     * 
     * 
     * IMAGE MANAGEMENT
     * ================
     * 
     * 1. Get Stored Images
     *    =================
     *    var images = cameraService.GetStoredImages();
     *    foreach (var imagePath in images)
     *    {
     *        // Use imagePath
     *    }
     * 
     * 
     * 2. Delete Image
     *    ============
     *    bool deleted = cameraService.DeleteImage(imagePath);
     *    if (deleted)
     *    {
     *        // Image was successfully deleted
     *    }
     * 
     * 
     * RETURN VALUES
     * =============
     * 
     * CapturePhotoAsync()           : Task<string?>      (null = cancelled/failed)
     * PickPhotoAsync()              : Task<string?>      (null = cancelled/failed)
     * PickMediaAsync()              : Task<string?>      (null = cancelled/failed)
     * RequestCameraPermissionAsync()  : Task<bool>       (true = granted)
     * RequestStorageReadPermissionAsync() : Task<bool>   (true = granted)
     * GetStoredImages()             : IEnumerable<string> (ordered by creation time)
     * DeleteImage(path)             : bool               (true = success)
     * 
     * 
     * INTEGRATION IN BLAZOR COMPONENTS
     * =================================
     * 
     * @inject CameraService CameraService
     * @inject ISnackbar Snackbar
     * 
     * @code {
     *     private async Task SelectImageAsync()
     *     {
     *         var imagePath = await CameraService.PickPhotoAsync();
     *         if (string.IsNullOrEmpty(imagePath))
     *         {
     *             Snackbar.Add("Image selection cancelled", Severity.Info);
     *             return;
     *         }
     *         
     *         Snackbar.Add("Image loaded successfully!", Severity.Success);
     *         // Process the image...
     *     }
     * }
     * 
     * 
     * SUPPORTED IMAGE FORMATS
     * =======================
     * - JPEG (.jpg, .jpeg)
     * - PNG (.png)
     * - BMP (.bmp)
     * - GIF (.gif)
     * 
     * 
     * STORAGE LOCATION
     * ================
     * All images are stored in: FileSystem.AppDataDirectory
     * This ensures app-specific isolated storage that's:
     * - Secure from other apps
     * - Automatically cleaned up on app uninstall
     * - Accessible only to the app
     * 
     * 
     * ERROR HANDLING
     * ==============
     * All methods handle errors gracefully:
     * - Return null/false on error
     * - Don't throw exceptions
     * - Safe for all platforms
     */
}
