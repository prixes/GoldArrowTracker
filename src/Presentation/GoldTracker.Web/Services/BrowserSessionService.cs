// Copyright (c) GoldArrowTracker. Licensed under the GNU AFFERO GENERAL PUBLIC LICENSE v3.0

using Archery.Shared.Models;
using Archery.Shared.Services;
using Microsoft.JSInterop;
using System.Text.Json;

namespace GoldTracker.Web.Services;

/// <summary>
/// Browser implementation of session service using localStorage.
/// </summary>
public class BrowserSessionService : ISessionService
{
    private readonly IJSRuntime _jsRuntime;
    private const string StorageKey = "goldtracker_sessions";

    public BrowserSessionService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
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

    public async Task SaveSessionAsync(Session session)
    {
        var sessions = await GetSessionsAsync();
        var existing = sessions.FirstOrDefault(s => s.Id == session.Id);
        
        if (existing != null)
        {
            sessions.Remove(existing);
        }
        
        sessions.Add(session);
        
        var json = JsonSerializer.Serialize(sessions);
        await _jsRuntime.InvokeVoidAsync("localStorage.setItem", StorageKey, json);
    }

    public async Task DeleteSessionAsync(Guid id)
    {
        var sessions = await GetSessionsAsync();
        sessions.RemoveAll(s => s.Id == id);
        
        var json = JsonSerializer.Serialize(sessions);
        await _jsRuntime.InvokeVoidAsync("localStorage.setItem", StorageKey, json);
    }
}
