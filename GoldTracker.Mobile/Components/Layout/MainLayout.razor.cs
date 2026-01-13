using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace GoldTracker.Mobile.Components.Layout
{
    public partial class MainLayout
    {
        [Inject] private IJSRuntime JS { get; set; } = default!;
        [Inject] private NavigationManager NavigationManager { get; set; } = default!;

        private async Task GoBack()
        {
            await JS.InvokeVoidAsync("goBack");
        }

        private string CurrentUri => NavigationManager.Uri.Replace(NavigationManager.BaseUri, "");
    }
}
