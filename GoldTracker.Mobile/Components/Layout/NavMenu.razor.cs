using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using MudBlazor;
using GoldTracker.Mobile.Services.Sessions;

namespace GoldTracker.Mobile.Components.Layout
{
    public partial class NavMenu : IDisposable
    {
        [Inject] private NavigationManager NavigationManager { get; set; } = default!;
        [Inject] private ISessionState SessionState { get; set; } = default!;

        private string _previousPath = string.Empty;

        protected override void OnInitialized()
        {
            NavigationManager.LocationChanged += OnLocationChanged;
            _previousPath = NavigationManager.ToBaseRelativePath(NavigationManager.Uri).ToLower();
        }

        private async void OnLocationChanged(object? sender, Microsoft.AspNetCore.Components.Routing.LocationChangedEventArgs e)
        {
            var currentPath = NavigationManager.ToBaseRelativePath(NavigationManager.Uri).ToLower();
            
            // Auto-save if navigating away from session-live and there's an active session
            if (_previousPath.StartsWith("session-live") && 
                !currentPath.StartsWith("session-live") && 
                SessionState.IsSessionActive)
            {
                await SessionState.SaveCurrentSessionAsync();
            }
            
            _previousPath = currentPath;
            StateHasChanged();
        }

        private bool IsActive(string href, NavLinkMatch match = NavLinkMatch.Prefix)
        {
            var relativePath = NavigationManager.ToBaseRelativePath(NavigationManager.Uri).ToLower();
            
            if (match == NavLinkMatch.All)
            {
                return string.IsNullOrEmpty(relativePath);
            }

            return relativePath.StartsWith(href.ToLower(), StringComparison.OrdinalIgnoreCase);
        }

        public void Dispose()
        {
            NavigationManager.LocationChanged -= OnLocationChanged;
        }
    }
}
