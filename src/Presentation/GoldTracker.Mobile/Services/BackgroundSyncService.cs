using GoldTracker.Shared.UI.Services.Abstractions;
using Archery.Shared.Services;
using System.Diagnostics;

namespace GoldTracker.Mobile.Services;

public class BackgroundSyncService
{
    private readonly IServerAuthService _authService;
    private readonly ISessionSyncService _sessionSyncService;
    private readonly ISessionService _sessionService;
    private readonly IDatasetSyncService _datasetSyncService;
    private bool _isSyncing = false;

    public BackgroundSyncService(
        IServerAuthService authService, 
        ISessionSyncService sessionSyncService,
        ISessionService sessionService,
        IDatasetSyncService datasetSyncService)
    {
        _authService = authService;
        _sessionSyncService = sessionSyncService;
        _sessionService = sessionService;
        _datasetSyncService = datasetSyncService;

        _authService.OnSignedIn += () => {
            _ = RunBackgroundSyncAsync();
        };
    }

    public async Task RunBackgroundSyncAsync()
    {
        if (_isSyncing) return;
        if (!_authService.IsAuthenticated) return;

        _isSyncing = true;
        
        // GENTLE START: Give the UI 5 seconds to finish animations/transitions after login/startup
        await Task.Delay(5000);
        await Task.Yield(); 

        Debug.WriteLine("[BackgroundSyncService] Starting background sync...");

        try
        {
            // 1. Sync Sessions (Up/Down)
            var sessions = await _sessionService.GetSessionsAsync();
            foreach (var session in sessions)
            {
                // Check if we are still authenticated before each step
                if (!_authService.IsAuthenticated) break;

                await _sessionSyncService.SyncSessionAsync(session);
                await Task.Delay(100); // Be nice to the CPU/Network
            }

            if (_authService.IsAuthenticated)
            {
                await _sessionSyncService.SyncFromServerAsync();
            }

            // 2. Sync Datasets
            if (_authService.IsAuthenticated)
            {
                await _datasetSyncService.SyncExportedDatasetsAsync();
            }

            Debug.WriteLine("[BackgroundSyncService] Background sync completed successfully.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[BackgroundSyncService] Background sync failed: {ex.Message}");
        }
        finally
        {
            _isSyncing = false;
        }
    }
}
