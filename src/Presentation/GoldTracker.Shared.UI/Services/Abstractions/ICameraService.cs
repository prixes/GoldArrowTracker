using System.Collections.Generic;
using System.Threading.Tasks;

namespace GoldTracker.Shared.UI.Services.Abstractions
{
    /// <summary>
    /// Interface mirroring the Mobile CameraService to allow Web compatibility
    /// without rewriting Page logic.
    /// </summary>
    public interface ICameraService
    {
        Task<string?> CapturePhotoAsync();
        Task<string?> PickPhotoAsync();
        Task<string?> PickMediaAsync();
        Task<bool> RequestCameraPermissionAsync();
        Task<bool> RequestStorageReadPermissionAsync();
        Task<bool> RequestStorageWritePermissionAsync();
        IEnumerable<string> GetStoredImages();
        bool DeleteImage(string filePath);
        Task<string?> SaveImageAsync(byte[] imageData, string fileName, string subdirectory);
        Task<string> SaveInternalImageAsync(string fileName, byte[] bytes);
        void TriggerMediaScanner(string filePath);

        // Abstracted File IO methods to support Web "virtual" paths
        Task<byte[]> ReadFileBytesAsync(string path);
        Task WriteFileTextAsync(string path, string content);
    }
}
