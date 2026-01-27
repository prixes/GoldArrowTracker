// Copyright (c) GoldArrowTracker. Licensed under the GNU AFFERO GENERAL PUBLIC LICENSE v3.0

using Archery.Shared.Models;
using Archery.Shared.Services;

namespace GoldTracker.Web.Services;

/// <summary>
/// Browser implementation of session state management.
/// </summary>
public class BrowserSessionState : ISessionState
{
    private readonly ISessionService _sessionService;
    private Session? _currentSession;

    public BrowserSessionState(ISessionService sessionService)
    {
        _sessionService = sessionService;
    }

    public Session? CurrentSession => _currentSession;
    public bool IsSessionActive => _currentSession != null;
    public event Action? OnChange;

    public async Task InitializeAsync()
    {
        // Load any active session from storage if needed
        await Task.CompletedTask;
    }

    public void StartNewSession(string? topic = null, string? note = null)
    {
        _currentSession = new Session
        {
            Id = Guid.NewGuid(),
            StartTime = DateTime.Now,
            Topic = topic,
            Note = note,
            Ends = new List<SessionEnd>()
        };
        OnChange?.Invoke();
    }

    public async Task AddEndAsync(SessionEnd end)
    {
        if (_currentSession == null) return;
        
        _currentSession.Ends.Add(end);
        await SaveCurrentSessionAsync();
        OnChange?.Invoke();
    }

    public async Task SaveCurrentSessionAsync()
    {
        if (_currentSession != null)
        {
            await _sessionService.SaveSessionAsync(_currentSession);
        }
    }

    public async Task FinishSessionAsync()
    {
        if (_currentSession != null)
        {
            await _sessionService.SaveSessionAsync(_currentSession);
            _currentSession = null;
            OnChange?.Invoke();
        }
    }

    public void CancelSession()
    {
        _currentSession = null;
        OnChange?.Invoke();
    }

    public async Task ResumeSessionAsync(Session session)
    {
        _currentSession = session;
        OnChange?.Invoke();
        await Task.CompletedTask;
    }

    public async Task UpdateEndAsync(Guid sessionId, int index, SessionEnd updatedEnd)
    {
        var session = await _sessionService.GetSessionAsync(sessionId);
        if (session != null && index >= 0 && index < session.Ends.Count)
        {
            session.Ends[index] = updatedEnd;
            await _sessionService.SaveSessionAsync(session);
            
            if (_currentSession?.Id == sessionId)
            {
                _currentSession = session;
            }
            
            OnChange?.Invoke();
        }
    }

    public async Task DeleteEndAsync(Guid sessionId, int index)
    {
        var session = await _sessionService.GetSessionAsync(sessionId);
        if (session != null && index >= 0 && index < session.Ends.Count)
        {
            session.Ends.RemoveAt(index);
            await _sessionService.SaveSessionAsync(session);
            
            if (_currentSession?.Id == sessionId)
            {
                _currentSession = session;
            }
            
            OnChange?.Invoke();
        }
    }
}
