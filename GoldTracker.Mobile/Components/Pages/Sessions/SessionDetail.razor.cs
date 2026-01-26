using Archery.Shared.Models;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using GoldTracker.Mobile.Services.Sessions;

namespace GoldTracker.Mobile.Components.Pages.Sessions
{
    public partial class SessionDetail
    {
        [Inject] private ISessionService SessionService { get; set; } = default!;
        [Inject] private NavigationManager Navigation { get; set; } = default!;
        [Inject] private ISnackbar Snackbar { get; set; } = default!;
        [Inject] private IDialogService DialogService { get; set; } = default!;
        [Inject] private ISessionState SessionState { get; set; } = default!;

        [Parameter] public Guid Id { get; set; }

        private Session? _session;
        private bool _isLoading = true;

        protected override async Task OnInitializedAsync()
        {
            await LoadSession();
        }

        private async Task LoadSession()
        {
            _isLoading = true;
            try
            {
                _session = await SessionService.GetSessionAsync(Id);
            }
            finally
            {
                _isLoading = false;
                StateHasChanged();
            }
        }

        private async Task DeleteSessionAsync()
        {
            bool? result = await DialogService.ShowMessageBox(
                "Delete Session", 
                "Are you sure you want to delete this session? This cannot be undone.", 
                yesText: "Delete", cancelText: "Cancel");

            if (result == true)
            {
                await SessionService.DeleteSessionAsync(Id);
                Snackbar.Add("Session deleted", Severity.Success);
                Navigation.NavigateTo("/sessions");
            }
        }

        private async Task ResumeSessionAsync()
        {
            if (_session == null) return;
            
            await SessionState.ResumeSessionAsync(_session);
            Snackbar.Add("Session resumed for editing", Severity.Info);
            Navigation.NavigateTo("/session-live");
        }

        private void NavigateToComprehensiveDetail()
        {
            if (_session != null)
            {
                Navigation.NavigateTo($"/session-comprehensive/{_session.Id}");
            }
        }

        private void NavigateToEnd(int endIndex)
        {
            if (_session != null)
            {
                Navigation.NavigateTo($"/session-end/{_session.Id}/{endIndex}");
            }
        }

        private MudBlazor.Color GetArrowColor(int points)
        {
            return points switch
            {
                10 or 9 => MudBlazor.Color.Warning,
                8 or 7 => MudBlazor.Color.Error,
                6 or 5 => MudBlazor.Color.Info,
                4 or 3 => MudBlazor.Color.Dark,
                _ => MudBlazor.Color.Default
            };
        }
    }
}
