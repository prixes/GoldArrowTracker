using Archery.Shared.Models;
using GoldTracker.Shared.UI.Models;

namespace GoldTracker.Shared.UI.Services.Abstractions;

public interface ISessionSyncService
{
    Task<bool> SyncSessionAsync(Session session, IProgress<SyncProgress>? progress = null);
    Task<int> SyncFromServerAsync(IProgress<SyncProgress>? progress = null);
}
