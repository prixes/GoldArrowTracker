using GoldTracker.Shared.UI.Models;
using GoldTracker.Shared.UI.Services.Abstractions;
using Archery.Shared.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using System;
using System.Threading.Tasks;

namespace GoldTracker.Shared.UI.Components.Pages.Settings
{
    public partial class Settings
    {
        [Inject] public IPreferenceService PreferenceService { get; set; } = default!;
        [Inject] public IServerAuthService AuthService { get; set; } = default!;
        [Inject] public ISessionService SessionService { get; set; } = default!;
        [Inject] public ISessionSyncService SessionSyncService { get; set; } = default!;
        [Inject] public IPlatformProvider PlatformProvider { get; set; } = default!;
        [Inject] public NavigationManager Navigation { get; set; } = default!;
        [Inject] public ISnackbar Snackbar { get; set; } = default!;
        
        // Optional service, only available on Mobile
        [Inject] public IDatasetSyncService? DatasetSyncService { get; set; }

        private bool _isSyncing = false;
        private SyncProgress _sessionSyncProgress = new(0, "");
        
        private bool _isDatasetSyncing = false;
        private SyncProgress _datasetSyncProgress = new(0, "");

        private async Task OnDarkModeDataChanged(bool value)
        {
            await PreferenceService.SetDarkModeAsync(value);
        }

        private async Task SignInWithGoogle()
        {
            await AuthService.SignInAsync();
        }

        private async Task SignOut()
        {
            await AuthService.LogoutAsync();
            StateHasChanged();
        }

        private async Task SyncAllSessions()
        {
            if (_isSyncing) return;
            
            _isSyncing = true;
            _sessionSyncProgress = new SyncProgress(0, "Preparing sync...");
            StateHasChanged();

            try
            {
                var progressIndicator = new Progress<SyncProgress>(p =>
                {
                    _sessionSyncProgress = p;
                    StateHasChanged();
                });

                // 1. Upload local sessions
                var localSessions = await SessionService.GetSessionsAsync();
                int totalSess = localSessions.Count;
                if (totalSess > 0)
                {
                    for (int i = 0; i < totalSess; i++)
                    {
                        var sess = localSessions[i];
                        double pct = (double)i / (totalSess + 1) * 50; 
                        _sessionSyncProgress = new SyncProgress(pct, $"Uploading session {i + 1}/{totalSess}...");
                        StateHasChanged();
                        await SessionSyncService.SyncSessionAsync(sess, progressIndicator);
                    }
                }

                // 2. Download from server
                int imported = await SessionSyncService.SyncFromServerAsync(progressIndicator);
                Snackbar.Add($"Sync completed. Imported {imported} new sessions.", Severity.Success);
            }
            catch (Exception ex)
            {
                Snackbar.Add($"Sync failed: {ex.Message}", Severity.Error);
            }
            finally
            {
                _isSyncing = false;
                StateHasChanged();
            }
        }

        private async Task SyncDatasets()
        {
            if (DatasetSyncService == null || _isDatasetSyncing) return;

            _isDatasetSyncing = true;
            _datasetSyncProgress = new SyncProgress(0, "Scanning datasets...");
            StateHasChanged();

            try
            {
                var progressIndicator = new Progress<SyncProgress>(p =>
                {
                    _datasetSyncProgress = p;
                    StateHasChanged();
                });

                int uploaded = await DatasetSyncService.SyncExportedDatasetsAsync(progressIndicator);
                Snackbar.Add($"Dataset sync completed. Uploaded {uploaded} files.", Severity.Success);
            }
            catch (Exception ex)
            {
                Snackbar.Add($"Dataset sync failed: {ex.Message}", Severity.Error);
            }
            finally
            {
                _isDatasetSyncing = false;
                StateHasChanged();
            }
        }
    }
}
