using System.Text.Json;
using Archery.Shared.Models;
using Archery.Shared.Services;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;

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

        public async Task<bool> SyncSessionAsync(Session session)
        {
            if (!_authService.IsAuthenticated) return false;
            var token = await _authService.GetAccessTokenAsync();
            if (string.IsNullOrEmpty(token)) return false;

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            try
            {
                // 1. Upload Session JSON
                var response = await _httpClient.PostAsJsonAsync("/api/sessions", session);
                if (!response.IsSuccessStatusCode) 
                {
                    System.Diagnostics.Debug.WriteLine($"Sync failed: {response.ReasonPhrase}");
                    return false;
                }

                // 2. Upload Images
                foreach (var end in session.Ends)
                {
                    if (!string.IsNullOrEmpty(end.ImagePath) && File.Exists(end.ImagePath))
                    {
                        var fileName = Path.GetFileName(end.ImagePath);
                        using var content = new MultipartFormDataContent();
                        using var fileStream = File.OpenRead(end.ImagePath);
                        content.Add(new StreamContent(fileStream), "file", fileName);

                        var imgResponse = await _httpClient.PostAsync($"/api/sessions/{session.Id}/images/{fileName}", content);
                        if (!imgResponse.IsSuccessStatusCode)
                        {
                            System.Diagnostics.Debug.WriteLine($"Image upload failed: {fileName}");
                             // Continue uploading other images anyway
                        }
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Sync Exception: {ex.Message}");
                return false;
            }
        }
    }
}
