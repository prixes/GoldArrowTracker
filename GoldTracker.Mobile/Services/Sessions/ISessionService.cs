using Archery.Shared.Models;

namespace GoldTracker.Mobile.Services.Sessions
{
    public interface ISessionService
    {
        Task<List<Session>> GetSessionsAsync();
        Task<Session?> GetSessionAsync(Guid id);
        Task SaveSessionAsync(Session session);
        Task DeleteSessionAsync(Guid id);
    }
}
