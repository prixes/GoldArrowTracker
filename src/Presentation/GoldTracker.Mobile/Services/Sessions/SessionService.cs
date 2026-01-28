using System.Text.Json;
using Archery.Shared.Models;
using Archery.Shared.Services;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using System.Collections.Concurrent;
using GoldTracker.Shared.UI.Models;

namespace GoldTracker.Mobile.Services.Sessions
{
    public class SessionService : ISessionService, ISessionSyncService
    {
        private readonly string _sessionsDir;
        private readonly IServerAuthService _authService;
        private readonly HttpClient _httpClient;

        public SessionService(IServerAuthService authService, IConfiguration config)
        {
            _authService = authService;
             var serverUrl = config["Settings:ServerUrl"] ?? "http://localhost:5000";
            _httpClient = new HttpClient { BaseAddress = new Uri(serverUrl) };

            _sessionsDir = Path.Combine(FileSystem.AppDataDirectory, "sessions");
            if (!Directory.Exists(_sessionsDir))
            {
                Directory.CreateDirectory(_sessionsDir);
            }
        }

        public async Task<List<Session>> GetSessionsAsync()
        {
            var sessions = new List<Session>();
            var files = Directory.GetFiles(_sessionsDir, "*.json");

            foreach (var file in files)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(file);
                    var session = JsonSerializer.Deserialize<Session>(json);
                    if (session != null)
                    {
                        sessions.Add(session);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error loading session {file}: {ex.Message}");
                }
            }

            return sessions.OrderByDescending(s => s.StartTime).ToList();
        }

        public async Task<Session?> GetSessionAsync(Guid id)
        {
            var filePath = Path.Combine(_sessionsDir, $"{id}.json");
            if (!File.Exists(filePath)) return null;

            try
            {
                var json = await File.ReadAllTextAsync(filePath);
                return JsonSerializer.Deserialize<Session>(json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading session {id}: {ex.Message}");
                return null;
            }
        }

        public async Task SaveSessionAsync(Session session)
        {
            try
            {
                var filePath = Path.Combine(_sessionsDir, $"{session.Id}.json");
                var json = JsonSerializer.Serialize(session, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(filePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving session {session.Id}: {ex.Message}");
                throw;
            }
        }

        public async Task DeleteSessionAsync(Guid id)
        {
            var filePath = Path.Combine(_sessionsDir, $"{id}.json");
            if (File.Exists(filePath))
            {
                await Task.Run(() => File.Delete(filePath));
            }
        }

        public async Task<bool> SyncSessionAsync(Session session, IProgress<SyncProgress>? progress = null)
        {
            if (!_authService.IsAuthenticated) return false;
            var token = await _authService.GetAccessTokenAsync();
            if (string.IsNullOrEmpty(token)) return false;

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            try
            {
                progress?.Report(new SyncProgress(0, $"Uploading session {session.Id}..."));
                
                // 1. Upload Session JSON
                var response = await _httpClient.PostAsJsonAsync("/api/sessions", session);
                if (!response.IsSuccessStatusCode) 
                {
                    System.Diagnostics.Debug.WriteLine($"Sync failed: {response.ReasonPhrase}");
                    return false;
                }

                // 2. Upload Images (Parallel)
                var validImages = session.Ends
                    .Where(e => !string.IsNullOrEmpty(e.ImagePath) && File.Exists(e.ImagePath))
                    .ToList();
                
                int totalImages = validImages.Count;
                if (totalImages > 0)
                {
                    var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = 4 };
                    int processedImages = 0;

                    await Parallel.ForEachAsync(validImages, parallelOptions, async (end, ct) => 
                    {
                        var fileName = Path.GetFileName(end.ImagePath);
                        using var content = new MultipartFormDataContent();
                        using var fileStream = File.OpenRead(end.ImagePath);
                        content.Add(new StreamContent(fileStream), "file", fileName);

                        var imgResponse = await _httpClient.PostAsync($"/api/sessions/{session.Id}/images/{fileName}", content);
                        
                        var current = Interlocked.Increment(ref processedImages);
                        var percent = (double)current / totalImages * 100;
                        progress?.Report(new SyncProgress(percent, $"Uploading image {current}/{totalImages}"));
                    });
                }
                
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Sync Exception: {ex.Message}");
                return false;
            }
        }

        public async Task<int> SyncFromServerAsync(IProgress<SyncProgress>? progress = null)
        {
            if (!_authService.IsAuthenticated) return 0;
            var token = await _authService.GetAccessTokenAsync();
            if (string.IsNullOrEmpty(token)) return 0;

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            int importedCount = 0;

            try
            {
                progress?.Report(new SyncProgress(0, "Fetching session list..."));
                
                // 1. Get List of Sessions from Server
                var sessions = await _httpClient.GetFromJsonAsync<List<Session>>("/api/sessions");
                if (sessions == null || sessions.Count == 0) return 0;

                // 2. Filter new sessions
                var newSessions = new ConcurrentBag<Session>();
                foreach(var s in sessions)
                {
                    if (await GetSessionAsync(s.Id) == null)
                        newSessions.Add(s);
                }

                int totalSessions = newSessions.Count;
                if (totalSessions == 0) return 0;

                int processedSessions = 0;
                var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = 3 };

                // 3. Process sessions in parallel
                await Parallel.ForEachAsync(newSessions, parallelOptions, async (serverSession, ct) =>
                {
                    // Update progress
                    var currentSessIdx = Interlocked.Increment(ref processedSessions);
                    progress?.Report(new SyncProgress(((double)currentSessIdx / totalSessions * 100), $"Downloading session {currentSessIdx}/{totalSessions}"));

                    // Download images for this session
                    var imagesToDownload = serverSession.Ends
                        .Where(e => !string.IsNullOrEmpty(e.ImagePath))
                        .ToList();

                    if (imagesToDownload.Any())
                    {
                         // Download images in parallel within the session (or sequentially if we want to avoid flooding network)
                         // Let's do sequential for images within a session to keep it simple, since we parallelize sessions.
                         foreach(var end in imagesToDownload)
                         {
                             var fileName = Path.GetFileName(end.ImagePath);
                             var localFileName = $"synced_{serverSession.Id}_{fileName}";
                             var localPath = Path.Combine(FileSystem.AppDataDirectory, localFileName);

                             if (!File.Exists(localPath))
                             {
                                 try 
                                 {
                                     var imgBytes = await _httpClient.GetByteArrayAsync($"/api/sessions/{serverSession.Id}/images/{fileName}");
                                     await File.WriteAllBytesAsync(localPath, imgBytes);
                                     end.ImagePath = localPath; // Update path
                                 }
                                 catch 
                                 { 
                                     // Ignore failure, maybe placeholder?
                                 }
                             }
                             else
                             {
                                 end.ImagePath = localPath;
                             }
                         }
                    }

                    // 4. Save Session Locally
                    await SaveSessionAsync(serverSession);
                    Interlocked.Increment(ref importedCount);
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SyncFromServer Error: {ex.Message}");
            }

            return importedCount;
        }
    }
}
