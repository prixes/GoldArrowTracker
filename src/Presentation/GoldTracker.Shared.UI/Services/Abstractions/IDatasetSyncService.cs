using GoldTracker.Shared.UI.Models;

namespace GoldTracker.Shared.UI.Services.Abstractions;

public interface IDatasetSyncService
{
    Task<int> SyncExportedDatasetsAsync(IProgress<SyncProgress>? progress = null);
}
