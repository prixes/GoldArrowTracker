using GoldTracker.Shared.UI.Models;
using GoldTracker.Shared.UI.Services.Abstractions;

namespace GoldTracker.Web.Services;

public class BrowserDatasetSyncService : IDatasetSyncService
{
    public Task<int> SyncExportedDatasetsAsync(IProgress<SyncProgress>? progress = null)
    {
        // Not implemented for browser
        return Task.FromResult(0);
    }
}
