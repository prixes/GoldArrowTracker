using Microsoft.AspNetCore.Components;

namespace GoldTracker.Shared.UI.Components.Pages
{
    public partial class Home
    {
        [Inject] private NavigationManager Navigation { get; set; } = default!;

        private void NavigateToCapture()
        {
            Navigation.NavigateTo("/target-capture");
        }
    }
}
