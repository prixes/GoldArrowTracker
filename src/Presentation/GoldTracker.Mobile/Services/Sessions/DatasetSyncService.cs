using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using GoldTracker.Shared.UI.Models;
using GoldTracker.Shared.UI.Services.Abstractions;

namespace GoldTracker.Mobile.Services.Sessions;

public interface IDatasetSyncService
{
    Task<int> SyncExportedDatasetsAsync(IProgress<SyncProgress>? progress = null);
}

public class DatasetSyncService : IDatasetSyncService
{
    private readonly IServerAuthService _authService;
    private readonly HttpClient _httpClient;
    private readonly IPathService _pathService;

    public DatasetSyncService(IServerAuthService authService, IConfiguration config, IPathService pathService)
    {
        _authService = authService;
        _pathService = pathService;
        var serverUrl = config["Settings:ServerUrl"] ?? "http://localhost:5000";
        _httpClient = new HttpClient { BaseAddress = new Uri(serverUrl) };
    }

    public async Task<int> SyncExportedDatasetsAsync(IProgress<SyncProgress>? progress = null)
    {
        if (!_authService.IsAuthenticated) return 0;
        var token = await _authService.GetAccessTokenAsync();
        if (string.IsNullOrEmpty(token)) return 0;

        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var exportPath = _pathService.GetExportPath();
        if (!Directory.Exists(exportPath)) return 0;

        var files = Directory.GetFiles(exportPath, "*.*", SearchOption.AllDirectories);
        int totalFiles = files.Length;
        int processedCount = 0;
        int successCount = 0;

        progress?.Report(new SyncProgress(0, $"Found {totalFiles} files to sync..."));

        // Use SemaphoreSlim to limit concurrency (e.g. 5 parallel uploads)
        // Parallel.ForEachAsync is also good, but we want to track successCount safely.
        
        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = 5 };
        
        await Parallel.ForEachAsync(files, parallelOptions, async (filePath, ct) =>
        {
            var relativePath = filePath.Substring(exportPath.Length).Replace("\\", "/").TrimStart('/');
            var success = await UploadFileAsync(filePath, relativePath);
            
            if (success) Interlocked.Increment(ref successCount);
            
            var current = Interlocked.Increment(ref processedCount);
            var percent = (double)current / totalFiles * 100;
            
            progress?.Report(new SyncProgress(percent, $"Syncing {Path.GetFileName(filePath)}") 
            { 
                 ProcessedCount = current, 
                 TotalCount = totalFiles 
            });
        });

        return successCount;
    }

    private async Task<bool> UploadFileAsync(string localPath, string relativePath)
    {
        try
        {
            var fileName = Path.GetFileName(localPath);
            using var fileStream = File.OpenRead(localPath); // FileShare.Read might be needed if contested
            using var content = new MultipartFormDataContent();
            content.Add(new StreamContent(fileStream), "file", fileName);

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
