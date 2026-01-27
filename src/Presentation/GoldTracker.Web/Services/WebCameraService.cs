using GoldTracker.Shared.UI.Services.Abstractions;
using Microsoft.JSInterop;

namespace GoldTracker.Web.Services;

public class WebCameraService : ICameraService
{
    private readonly IJSRuntime _js;
    // In-memory file cache for the session (simulates file system)
    private static readonly Dictionary<string, byte[]> _fileCache = new();

    public WebCameraService(IJSRuntime js)
    {
        _js = js;
    }

    public async Task<string?> CapturePhotoAsync() => await PickPhotoAsync();

    public async Task<string?> PickPhotoAsync()
    {
        try
        {
            // Pick file and get base64
            var base64 = await _js.InvokeAsync<string>("pickFile", "image/*", false);
            if (string.IsNullOrEmpty(base64)) return null;

            byte[] bytes = Convert.FromBase64String(base64);
            string key = $"captured_{Guid.NewGuid()}.jpg";
            _fileCache[key] = bytes;
            return key;
        }
        catch
        {
            return null;
        }
    }

    public async Task<string?> PickMediaAsync() => await PickPhotoAsync();

    // Permissions on web are handled by browser prompts usually
    public Task<bool> RequestCameraPermissionAsync() => Task.FromResult(true);
    public Task<bool> RequestStorageReadPermissionAsync() => Task.FromResult(true);
    public Task<bool> RequestStorageWritePermissionAsync() => Task.FromResult(true);

    public IEnumerable<string> GetStoredImages()
    {
        return _fileCache.Keys;
    }

    public bool DeleteImage(string filePath)
    {
        return _fileCache.Remove(filePath);
    }

    public async Task<string?> SaveImageAsync(byte[] imageData, string fileName, string subdirectory)
    {
        try
        {
            // Triggers browser download
            string base64 = Convert.ToBase64String(imageData);
            await _js.InvokeVoidAsync("downloadFile", fileName, base64);
            
            // Also cache it
            _fileCache[fileName] = imageData;
            return fileName; 
        }
        catch
        {
            return null;
        }
    }

    public Task<string> SaveInternalImageAsync(string fileName, byte[] bytes)
    {
        // Cache Only (Session persistence in memory)
        string key = fileName;
        _fileCache[key] = bytes;
        return Task.FromResult(key);
    }

    public void TriggerMediaScanner(string filePath) { /* No-op on web */ }

    public Task<byte[]> ReadFileBytesAsync(string path)
    {
        if (_fileCache.ContainsKey(path))
        {
            return Task.FromResult(_fileCache[path]);
        }
        return Task.FromResult(Array.Empty<byte>());
    }

    public Task WriteFileTextAsync(string path, string content)
    {
        // For web, "writing" text labels implies generic download if in export context
        // But if internal, just cache? 
        // Labels are usually exported.
        // We assume export context if path looks like export.
        
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(content);
        
        // Use download logic if we can infer filename
        string fileName = Path.GetFileName(path);
        
        // Trigger download
        string base64 = Convert.ToBase64String(bytes);
        // Fire and forget download
        _js.InvokeVoidAsync("downloadFile", fileName, base64);
        
        return Task.CompletedTask;
    }
}
