using Archery.Shared.Models;

namespace Archery.Shared.Services
{
    public interface ISessionState
    {
        Session? CurrentSession { get; }
        bool IsSessionActive { get; }
        event Action? OnChange;

        Task InitializeAsync();
        void StartNewSession(string? topic = null, string? note = null);
        Task AddEndAsync(SessionEnd end);
        Task SaveCurrentSessionAsync();
        Task FinishSessionAsync();
        void CancelSession();
        Task ResumeSessionAsync(Session session);
        Task UpdateEndAsync(Guid sessionId, int index, SessionEnd updatedEnd);
        Task DeleteEndAsync(Guid sessionId, int index);
    }
}
