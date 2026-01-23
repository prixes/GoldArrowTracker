using Archery.Shared.Models;

namespace GoldTracker.Mobile.Services.Sessions
{
    public interface ISessionState
    {
        Session? CurrentSession { get; }
        bool IsSessionActive { get; }
        event Action? OnChange;

        Task InitializeAsync();
        void StartNewSession(string? topic = null, string? note = null);
        Task AddEndAsync(SessionEnd end);
        Task FinishSessionAsync();
        void CancelSession();
        Task ResumeSessionAsync(Session session);
        Task UpdateEndAsync(Guid sessionId, int index, SessionEnd updatedEnd);
        Task DeleteEndAsync(Guid sessionId, int index);
    }
}
