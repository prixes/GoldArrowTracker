using Archery.Shared.Models;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using GoldTracker.Mobile.Services.Sessions;

namespace GoldTracker.Mobile.Components.Pages.Sessions
{
    public partial class SessionLive : IDisposable
    {
        [Inject] private ISessionState SessionState { get; set; } = default!;
        [Inject] private NavigationManager Navigation { get; set; } = default!;
        [Inject] private ISnackbar Snackbar { get; set; } = default!;

        protected override void OnInitialized()
        {
            SessionState.OnChange += StateHasChanged;
        }

        public void Dispose()
        {
            SessionState.OnChange -= StateHasChanged;
        }

        private void AddEnd()
        {
            Navigation.NavigateTo("/target-capture");
        }

        private async Task FinishSession()
        {
            await SessionState.FinishSessionAsync();
            Snackbar.Add("Session Saved!", Severity.Success);
            Navigation.NavigateTo("/sessions");
        }

        private void NavigateToEnd(int endIndex)
        {
            if (SessionState.CurrentSession != null)
            {
                Navigation.NavigateTo($"/session-end/{SessionState.CurrentSession.Id}/{endIndex}");
            }
        }

        private MudBlazor.Color GetArrowColor(int points)
        {
            return points switch
            {
                10 or 9 => MudBlazor.Color.Warning, // Gold
                8 or 7 => MudBlazor.Color.Error,   // Red
                6 or 5 => MudBlazor.Color.Info,    // Blue
                4 or 3 => MudBlazor.Color.Dark,    // Black
                _ => MudBlazor.Color.Default       // White/Miss
            };
        }
    }
}
