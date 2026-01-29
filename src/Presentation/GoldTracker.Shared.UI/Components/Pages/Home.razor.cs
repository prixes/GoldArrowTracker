using Microsoft.AspNetCore.Components;

namespace GoldTracker.Shared.UI.Components.Pages
{
    public partial class Home
    {
        [Inject] private NavigationManager Navigation { get; set; } = default!;
        [Inject] private Archery.Shared.Services.ISessionService SessionService { get; set; } = default!;

        private Archery.Shared.Models.Session? _latestSession;

        protected override async Task OnInitializedAsync()
        {
            var sessions = await SessionService.GetSessionsAsync();
            if (sessions != null)
            {
                _latestSession = sessions.OrderByDescending(s => s.StartTime).FirstOrDefault();
            }
        }

        private void NavigateToCapture()
        {
            Navigation.NavigateTo("/target-capture");
        }

        private void NavigateToLatestSession()
        {
            if (_latestSession != null)
            {
                Navigation.NavigateTo($"/session/{_latestSession.Id}");
            }
        }

        private async Task CreateManualSession()
        {
            // Logic to start a new empty session and navigate to it
            // This assumes we might want to pre-create a session or just navigate to a "new session" page
            // reusing the existing logic in Sessions.razor.cs "StartNewSession" concept if possible, 
            // but for now, we'll navigate to the Sessions page or trigger the same "Start Session" flow.
            
            // To be consistent with "StartNewSession" in Sessions.razor which often creates a session first:
            var newSession = new Archery.Shared.Models.Session 
            { 
                Id = Guid.NewGuid(), 
                StartTime = DateTime.UtcNow 
            };
            
            // We need to save it so we can navigate to it
            await SessionService.SaveSessionAsync(newSession);
            Navigation.NavigateTo($"/session/{newSession.Id}");
        }
    }
}
