using Archery.Shared.Models;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using Archery.Shared.Services;

namespace GoldTracker.Shared.UI.Components.Pages.Sessions
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
        private List<ScoreRow> _scoreSheet = new();

        private record ScoreRow(int EndNumber, DateTime Timestamp, List<ArrowScore> Arrows, int EndScore, int RunningTotal);

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
                if (_session != null)
                {
                    GenerateScoreSheet();
                }
            }
            finally
            {
                _isLoading = false;
                StateHasChanged();
            }
        }

        private void GenerateScoreSheet()
        {
            _scoreSheet.Clear();
            if (_session == null) return;

            int runningTotal = 0;
            var sortedEnds = _session.Ends.OrderBy(e => e.Index).ToList();

            foreach (var end in sortedEnds)
            {
                runningTotal += end.Score;
                _scoreSheet.Add(new ScoreRow(end.Index, end.Timestamp, end.Arrows.OrderByDescending(a => a.Points).ToList(), end.Score, runningTotal));
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

        private void OnEndRowClick(TableRowClickEventArgs<ScoreRow> args)
        {
            if (args.Item != null)
            {
                NavigateToEnd(args.Item.EndNumber);
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

        private string GetArrowHexColor(int points) => points switch {
            100 or 10 or 9 => "#FFEB3B", // Gold/Yellow
            8 or 7 => "#F44336",         // Red
            6 or 5 => "#03A9F4",         // Blue
            4 or 3 => "#212121",         // Black
            _ => "#EEE"                  // White/Miss
        };
    }
}
