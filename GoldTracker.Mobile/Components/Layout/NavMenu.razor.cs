using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace GoldTracker.Mobile.Components.Layout
{
    public partial class NavMenu
    {
        [Inject] private NavigationManager NavigationManager { get; set; } = default!;

        private MudBlazor.Color GetNavLinkColor(string href)
        {
            return CurrentUri.Contains(href) && href != "" ? MudBlazor.Color.Tertiary : MudBlazor.Color.Primary;
        }

        private MudBlazor.Color GetTextColor(string href)
        {
            return CurrentUri.Contains(href) && href != "" ? MudBlazor.Color.Tertiary : MudBlazor.Color.Primary;
        }

        private string CurrentUri => NavigationManager.Uri.Replace(NavigationManager.BaseUri, "");
    }
}
