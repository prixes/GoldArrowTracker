using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using GoldTracker.Shared.UI;
using GoldTracker.Shared.UI.Models;
using GoldTracker.Shared.UI.Services.Abstractions;

namespace GoldTracker.Mobile.Services.Sessions;

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

#if ANDROID
        if (serverUrl.Contains("localhost") || serverUrl.Contains("127.0.0.1"))
        {
            serverUrl = serverUrl.Replace("localhost", "10.0.2.2").Replace("127.0.0.1", "10.0.2.2");
        }
#endif

        _httpClient = new HttpClient { 
            BaseAddress = new Uri(serverUrl),
            Timeout = TimeSpan.FromMinutes(5)
        };
    }

    public async Task<int> SyncExportedDatasetsAsync(IProgress<SyncProgress>? progress = null)
    {
        if (!_authService.IsAuthenticated) return 0;
        var token = await _authService.GetAccessTokenAsync();
        if (string.IsNullOrEmpty(token)) return 0;

        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return await Task.Run(async () => 
        {
            var exportPath = _pathService.GetExportPath();
            if (!Directory.Exists(exportPath)) return 0;

            var files = Directory.GetFiles(exportPath, "*.*", SearchOption.AllDirectories);
            int totalFiles = files.Length;
            if (totalFiles == 0) return 0;

            int processedCount = 0;
            int successCount = 0;

            progress?.Report(new SyncProgress(0, $"Found {totalFiles} files to sync..."));
            
            // Limit parallelism on mobile - 2 concurrent uploads is enough
            var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = 2 };
            
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

                // Yield to keep UI smooth
                await Task.Yield();
            });

            return successCount;
        });
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
