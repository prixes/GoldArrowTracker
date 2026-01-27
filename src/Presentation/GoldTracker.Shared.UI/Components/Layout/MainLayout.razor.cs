using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Archery.Shared.Services;

namespace GoldTracker.Shared.UI.Components.Layout
{
    public partial class MainLayout
    {
        [Inject] private IJSRuntime JS { get; set; } = default!;
        [Inject] private NavigationManager NavigationManager { get; set; } = default!;
        [Inject] private ISessionState? SessionState { get; set; }

        protected override async Task OnInitializedAsync()
        {
            if (SessionState != null)
            {
                await SessionState.InitializeAsync();
            }
        }

        private async Task GoBack()
        {
            await JS.InvokeVoidAsync("goBack");
        }

        private string CurrentUri => NavigationManager.Uri.Replace(NavigationManager.BaseUri, "");
    }
}
