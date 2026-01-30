using Archery.Shared.Models;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using Archery.Shared.Services;

namespace GoldTracker.Shared.UI.Components.Pages.Sessions
{
    public partial class SessionLive : IDisposable
    {
        [Inject] private ISessionState SessionState { get; set; } = default!;
        [Inject] private NavigationManager Navigation { get; set; } = default!;
        [Inject] private ISnackbar Snackbar { get; set; } = default!;

        private List<ScoreRow> _scoreSheet = new();

        private record ScoreRow(int EndNumber, DateTime Timestamp, List<ArrowScore> Arrows, int EndScore, int RunningTotal);

        protected override void OnInitialized()
        {
            SessionState.OnChange += OnStateChanged;
            GenerateScoreSheet();
        }

        public void Dispose()
        {
            SessionState.OnChange -= OnStateChanged;
        }

        private void OnStateChanged()
        {
            GenerateScoreSheet();
            StateHasChanged();
        }

        private void GenerateScoreSheet()
        {
            _scoreSheet.Clear();
            if (SessionState.CurrentSession == null) return;

            int runningTotal = 0;
            var sortedEnds = SessionState.CurrentSession.Ends.OrderBy(e => e.Index).ToList();

            foreach (var end in sortedEnds)
            {
                runningTotal += end.Score;
                _scoreSheet.Add(new ScoreRow(end.Index, end.Timestamp, end.Arrows.OrderByDescending(a => a.Points).ToList(), end.Score, runningTotal));
            }
        }

        private void AddEnd()
        {
            Navigation.NavigateTo("/target-capture");
        }

        private async Task GoBackToSessions()
        {
            // Auto-save the session before going back
            if (SessionState.CurrentSession != null)
            {
                await SessionState.SaveCurrentSessionAsync();
                Snackbar.Add("Session auto-saved", Severity.Info);
            }
            Navigation.NavigateTo("/sessions");
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

        private void OnEndRowClick(TableRowClickEventArgs<ScoreRow> args)
        {
            if (args.Item != null)
            {
                NavigateToEnd(args.Item.EndNumber);
            }
        }

        private void NavigateToComprehensiveDetail()
        {
            if (SessionState.CurrentSession != null)
            {
                Navigation.NavigateTo($"/session-comprehensive/{SessionState.CurrentSession.Id}");
            }
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
