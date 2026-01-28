// Copyright (c) GoldArrowTracker. Licensed under the GNU AFFERO GENERAL PUBLIC LICENSE v3.0

using Archery.Shared.Models;
using Archery.Shared.Services;
using Microsoft.JSInterop;
using System.Text.Json;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using GoldTracker.Shared.UI;
using GoldTracker.Shared.UI.Models;
using GoldTracker.Shared.UI.Services.Abstractions;

namespace GoldTracker.Web.Services;

/// <summary>
/// Browser implementation of session service using localStorage and supporting server sync.
/// </summary>
public class BrowserSessionService : ISessionService, ISessionSyncService
{
    private readonly IJSRuntime _jsRuntime;
    private readonly HttpClient _httpClient;
    private readonly IServerAuthService _authService;
    private const string StorageKey = "goldtracker_sessions";

    public BrowserSessionService(IJSRuntime jsRuntime, HttpClient httpClient, IServerAuthService authService)
    {
        _jsRuntime = jsRuntime;
        _httpClient = httpClient;
        _authService = authService;
    }

    public async Task<List<Session>> GetSessionsAsync()
    {
        try
        {
            var json = await _jsRuntime.InvokeAsync<string>("localStorage.getItem", StorageKey);
            if (string.IsNullOrEmpty(json))
                return new List<Session>();

            return JsonSerializer.Deserialize<List<Session>>(json) ?? new List<Session>();
        }
        catch
        {
            return new List<Session>();
        }
    }

    public async Task<Session?> GetSessionAsync(Guid id)
    {
        var sessions = await GetSessionsAsync();
        return sessions.FirstOrDefault(s => s.Id == id);
    }

    public async Task<Session?> GetUnfinishedSessionAsync()
    {
        var sessions = await GetSessionsAsync();
        return sessions.FirstOrDefault(s => s.EndTime == null);
    }

    public async Task SaveSessionAsync(Session session)
    {
        var sessions = await GetSessionsAsync();
        var existing = sessions.FirstOrDefault(s => s.Id == session.Id);
        
        if (existing != null)
        {
            sessions.Remove(existing);
        }
        
        sessions.Add(session);
        
        await SaveAllSessionsAsync(sessions);
    }

    public async Task DeleteSessionAsync(Guid id)
    {
        var sessions = await GetSessionsAsync();
        sessions.RemoveAll(s => s.Id == id);
        await SaveAllSessionsAsync(sessions);
    }

    private async Task SaveAllSessionsAsync(List<Session> sessions)
    {
        var json = JsonSerializer.Serialize(sessions);
        await _jsRuntime.InvokeVoidAsync("localStorage.setItem", StorageKey, json);
    }

    // ISessionSyncService Implementation

    public async Task<bool> SyncSessionAsync(Session session, IProgress<SyncProgress>? progress = null)
    {
        if (!_authService.IsAuthenticated) return false;
        var token = await _authService.GetAccessTokenAsync();
        if (string.IsNullOrEmpty(token)) return false;

        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            progress?.Report(new SyncProgress(0, $"Uploading session {session.Id}..."));
            
            // Clean paths before upload
            var sessionToUpload = JsonSerializer.Deserialize<Session>(JsonSerializer.Serialize(session))!;
            foreach(var end in sessionToUpload.Ends)
            {
                if (!string.IsNullOrEmpty(end.ImagePath))
                    end.ImagePath = GetCleanFileName(end.ImagePath, session.Id);
            }

            var response = await _httpClient.PostAsJsonAsync("/api/sessions", sessionToUpload);
            if (!response.IsSuccessStatusCode) return false;

            // Note: In Browser, we typically don't upload images from localStorage unless we stored them as Base64/Blobs
            // For now, we only sync the JSON. If images were taken on web, they might be in-memory.
            
            return true;
        }
        catch
        {
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
            var sessions = await _httpClient.GetFromJsonAsync<List<Session>>("/api/sessions");
            if (sessions == null) return 0;

            var localSessions = await GetSessionsAsync();
            foreach (var serverSession in sessions)
            {
                var existing = localSessions.FirstOrDefault(s => s.Id == serverSession.Id);
                if (existing == null)
                {
                    localSessions.Add(serverSession);
                    importedCount++;
                }
                else
                {
                    // Update existing if server is newer? For now just skip if exists locally
                }
            }

            if (importedCount > 0)
            {
                await SaveAllSessionsAsync(localSessions);
            }

            return importedCount;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SyncFromServer Error: {ex.Message}");
            return 0;
        }
    }

    private string GetCleanFileName(string path, Guid sessionId)
    {
        if (string.IsNullOrEmpty(path)) return string.Empty;
        var fileName = System.IO.Path.GetFileName(path);
        var prefix = $"synced_{sessionId}_";
        while (fileName.StartsWith(prefix))
            fileName = fileName.Substring(prefix.Length);
        return fileName;
    }
}
