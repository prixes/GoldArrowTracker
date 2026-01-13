using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using MudBlazor;

namespace GoldTracker.Mobile.Components.Layout
{
    public partial class NavMenu : IDisposable
    {
        [Inject] private NavigationManager NavigationManager { get; set; } = default!;

        protected override void OnInitialized()
        {
            NavigationManager.LocationChanged += OnLocationChanged;
        }

        private void OnLocationChanged(object? sender, Microsoft.AspNetCore.Components.Routing.LocationChangedEventArgs e)
        {
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
