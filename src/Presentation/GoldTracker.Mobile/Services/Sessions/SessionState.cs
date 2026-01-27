using Archery.Shared.Models;
using Archery.Shared.Services;

namespace GoldTracker.Mobile.Services.Sessions
{
    public class SessionState : ISessionState
    {
        private readonly ISessionService _sessionService;
        
        public Session? CurrentSession { get; private set; }
        public bool IsSessionActive => CurrentSession != null;
        
        public event Action? OnChange;

        public SessionState(ISessionService sessionService)
        {
            _sessionService = sessionService;
        }

        public async Task InitializeAsync()
        {
            var sessions = await _sessionService.GetSessionsAsync();
            var unfinishedSession = sessions.FirstOrDefault(s => s.EndTime == null);
            if (unfinishedSession != null)
            {
                CurrentSession = unfinishedSession;
                NotifyStateChanged();
            }
        }

        public void StartNewSession(string? topic = null, string? note = null)
        {
            CurrentSession = new Session
            {
                StartTime = DateTime.Now,
                Topic = topic,
                Note = note
            };
            NotifyStateChanged();
        }

        public async Task AddEndAsync(SessionEnd end)
        {
            if (CurrentSession == null) return;
            
            // Auto-increment index
            end.Index = CurrentSession.Ends.Count + 1;
            CurrentSession.Ends.Add(end);
            
            // Auto-save progress
            await _sessionService.SaveSessionAsync(CurrentSession);
            NotifyStateChanged();
        }

        public async Task SaveCurrentSessionAsync()
        {
            if (CurrentSession != null)
            {
                await _sessionService.SaveSessionAsync(CurrentSession);
            }
        }

        public async Task FinishSessionAsync()
        {
            if (CurrentSession != null)
            {
                CurrentSession.EndTime = DateTime.Now;
                await _sessionService.SaveSessionAsync(CurrentSession);
                CurrentSession = null;
                NotifyStateChanged();
            }
        }
        
        public void CancelSession()
        {
            CurrentSession = null;
            NotifyStateChanged();
        }

        public async Task ResumeSessionAsync(Session session)
        {
            // If they are resuming a finished session, clear the EndTime
            if (session.EndTime != null)
            {
                session.EndTime = null;
                await _sessionService.SaveSessionAsync(session);
            }
            
            CurrentSession = session;
            NotifyStateChanged();
        }

        public async Task UpdateEndAsync(Guid sessionId, int index, SessionEnd updatedEnd)
        {
            Session? sessionToUpdate = null;

            if (CurrentSession?.Id == sessionId)
            {
                sessionToUpdate = CurrentSession;
            }
            else
            {
                sessionToUpdate = await _sessionService.GetSessionAsync(sessionId);
            }

            if (sessionToUpdate == null) return;

            var existingIndex = sessionToUpdate.Ends.FindIndex(e => e.Index == index);
            if (existingIndex != -1)
            {
                sessionToUpdate.Ends[existingIndex] = updatedEnd;
                updatedEnd.Index = index; // Ensure index stays consistent
                await _sessionService.SaveSessionAsync(sessionToUpdate);
                
                if (sessionToUpdate == CurrentSession)
                {
                    NotifyStateChanged();
                }
            }
        }

        public async Task DeleteEndAsync(Guid sessionId, int index)
        {
            Session? sessionToUpdate = null;

            if (CurrentSession?.Id == sessionId)
            {
                sessionToUpdate = CurrentSession;
            }
            else
            {
                sessionToUpdate = await _sessionService.GetSessionAsync(sessionId);
            }

            if (sessionToUpdate == null) return;

            var existingIndex = sessionToUpdate.Ends.FindIndex(e => e.Index == index);
            if (existingIndex != -1)
            {
                sessionToUpdate.Ends.RemoveAt(existingIndex);
                
                // Re-index remaining ends to keep them sequential
                for (int i = 0; i < sessionToUpdate.Ends.Count; i++)
                {
                    sessionToUpdate.Ends[i].Index = i + 1;
                }
                
                await _sessionService.SaveSessionAsync(sessionToUpdate);
                
                if (sessionToUpdate == CurrentSession)
                {
                    NotifyStateChanged();
                }
            }
        }

        private void NotifyStateChanged() => OnChange?.Invoke();
    }
}
