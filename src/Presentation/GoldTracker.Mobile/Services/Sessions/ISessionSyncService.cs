using Archery.Shared.Models;
using GoldTracker.Shared.UI.Models;

namespace GoldTracker.Mobile.Services.Sessions;

public interface ISessionSyncService
{
    Task<bool> SyncSessionAsync(Session session, IProgress<SyncProgress>? progress = null);
    Task<int> SyncFromServerAsync(IProgress<SyncProgress>? progress = null);
}
