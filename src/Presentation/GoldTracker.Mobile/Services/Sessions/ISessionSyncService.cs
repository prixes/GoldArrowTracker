using Archery.Shared.Models;

namespace GoldTracker.Mobile.Services.Sessions;

public interface ISessionSyncService
{
    Task<bool> SyncSessionAsync(Session session);
    Task<int> SyncFromServerAsync();
}
