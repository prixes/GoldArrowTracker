using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace GoldTracker.Mobile.Services.Sessions;

public interface IDatasetSyncService
{
    Task<int> SyncExportedDatasetsAsync();
}

public class DatasetSyncService : IDatasetSyncService
{
    private readonly IServerAuthService _authService;
    private readonly HttpClient _httpClient;
    private readonly string _exportRootPath;

    public DatasetSyncService(IServerAuthService authService, IConfiguration config)
    {
        _authService = authService;
        var serverUrl = config["Settings:ServerUrl"] ?? "http://localhost:5000";
        _httpClient = new HttpClient { BaseAddress = new Uri(serverUrl) };

        // Determine Export Root (Matching DatasetExportService)
#if ANDROID
        var downloadsPath = Android.OS.Environment.GetExternalStoragePublicDirectory(Android.OS.Environment.DirectoryDownloads);
        var baseDir = downloadsPath?.AbsolutePath ?? "/storage/emulated/0/Download";
        _exportRootPath = Path.Combine(baseDir, "Export");
#else
        var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        _exportRootPath = Path.Combine(documentsPath, "Export");
#endif
    }

    public async Task<int> SyncExportedDatasetsAsync()
    {
        if (!_authService.IsAuthenticated) return 0;
        var token = await _authService.GetAccessTokenAsync();
        if (string.IsNullOrEmpty(token)) return 0;

        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        int successCount = 0;

        try
        {
            if (!Directory.Exists(_exportRootPath)) return 0;

            // Recurse all files
            var files = Directory.GetFiles(_exportRootPath, "*.*", SearchOption.AllDirectories);
            
            foreach (var filePath in files)
            {
                // Calculate relative path for server
                // e.g. /storage/.../Export/Macro_Model/images/x.jpg -> Macro_Model/images/x.jpg
                // We want: relativePath = Macro_Model/images/x.jpg
                
                // _exportRootPath ends in "Export".
                // filePath starts with _exportRootPath.
                
                // Ensure proper relative path calculation including path separators
                var relativePath = filePath.Substring(_exportRootPath.Length).Replace("\\", "/").TrimStart('/');
                
                // Upload
                var success = await UploadFileAsync(filePath, relativePath);
                if (success) successCount++;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Dataset Sync Error: {ex.Message}");
        }

        return successCount;
    }

    private async Task<bool> UploadFileAsync(string localPath, string relativePath)
    {
        try
        {
            var fileName = Path.GetFileName(localPath);
            using var fileStream = File.OpenRead(localPath);
            using var content = new MultipartFormDataContent();
            content.Add(new StreamContent(fileStream), "file", fileName);

            // POST /api/datasets/upload?relativePath=...
            var url = $"/api/datasets/upload?relativePath={System.Net.WebUtility.UrlEncode(relativePath)}";
            
            var response = await _httpClient.PostAsync(url, content);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"File Upload Error ({relativePath}): {ex.Message}");
            return false;
        }
    }
}
