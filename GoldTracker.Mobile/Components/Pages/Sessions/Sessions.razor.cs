using Archery.Shared.Models;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using GoldTracker.Mobile.Services.Sessions;

namespace GoldTracker.Mobile.Components.Pages.Sessions
{
    public partial class Sessions
    {
        [Inject] private ISessionService SessionService { get; set; } = default!;
        [Inject] private ISessionState SessionState { get; set; } = default!;
        [Inject] private NavigationManager Navigation { get; set; } = default!;
        [Inject] private IDialogService DialogService { get; set; } = default!;

        private List<Session> _sessions = new();
        private bool _isLoading = true;

        protected override async Task OnInitializedAsync()
        {
            await LoadSessions();
        }

        private async Task LoadSessions()
        {
            _isLoading = true;
            try
            {
                _sessions = await SessionService.GetSessionsAsync();
            }
            finally
            {
                _isLoading = false;
                StateHasChanged();
            }
        }

        private async Task StartNewSession()
        {
            if (SessionState.IsSessionActive)
            {
                bool? result = await DialogService.ShowMessageBox(
                    "Session in Progress", 
                    "You have an active session. Do you want to resume it or discard it and start a new one?", 
                    yesText: "Resume", cancelText: "Discard & New");

                if (result == true)
                {
                    Navigation.NavigateTo("/session-live");
                    return;
                }
                
                // If they chose to discard, we could clear it, but for now we just jump to live if active.
                Navigation.NavigateTo("/session-live");
                return;
            }

            SessionState.StartNewSession();
            Navigation.NavigateTo("/session-live");
        }

        private void NavigateToSession(Guid sessionId)
        {
            Navigation.NavigateTo($"/session/{sessionId}");
        }
    }
}
