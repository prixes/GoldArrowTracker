using GoldTracker.Shared.UI.Models;
using System.Threading.Tasks;
using System;

namespace GoldTracker.Shared.UI.Services.Abstractions;

public interface IDatasetSyncService
{
    Task<int> SyncExportedDatasetsAsync(IProgress<SyncProgress>? progress = null);
}
