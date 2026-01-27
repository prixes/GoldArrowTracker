using Archery.Shared.Models;

namespace Archery.Shared.Services
{
    public interface ISessionService
    {
        Task<List<Session>> GetSessionsAsync();
        Task<Session?> GetSessionAsync(Guid id);
        Task SaveSessionAsync(Session session);
        Task DeleteSessionAsync(Guid id);
    }
}
