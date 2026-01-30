using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;
using GoldTracker.Shared.UI.Services.Abstractions;
using Archery.Shared.Services;

namespace GoldTracker.Shared.UI.Components.Layout
{
    public partial class MainLayout : LayoutComponentBase, IDisposable
    {
        [Inject] public IJSRuntime JS { get; set; } = default!;
        [Inject] public NavigationManager Navigation { get; set; } = default!;
        [Inject] public IServerAuthService AuthService { get; set; } = default!;
        [Inject] public ISessionState SessionState { get; set; } = default!;
        [Inject] public IPreferenceService PreferenceService { get; set; } = default!;

        private MudThemeProvider _mudThemeProvider = default!;
        private bool _isDarkMode = false;

        private MudTheme _theme = new MudTheme()
        {
            PaletteLight = new PaletteLight() { Primary = Colors.Amber.Darken3, Secondary = Colors.DeepOrange.Lighten1, Background = Colors.Gray.Lighten5 },
            PaletteDark = new PaletteDark() 
            { 
                Primary = Colors.Amber.Darken3, 
                Secondary = Colors.DeepOrange.Lighten1,
                Background = "#1a1a2e",
                Surface = "#16213e",
                AppbarBackground = "#16213e"
            }
        };

        protected override async Task OnInitializedAsync()
        {
            // Configure Typography
            _theme.Typography.Default.FontFamily = new[] { "Roboto", "sans-serif" };
            _theme.Typography.Body1.FontFamily = new[] { "Roboto", "sans-serif" };
            _theme.Typography.Body2.FontFamily = new[] { "Roboto", "sans-serif" };
            
            _theme.Typography.H1.FontFamily = new[] { "Koulen", "sans-serif" };
            _theme.Typography.H1.LetterSpacing = ".05rem";
            
            _theme.Typography.H2.FontFamily = new[] { "Koulen", "sans-serif" };
            _theme.Typography.H2.LetterSpacing = ".05rem";
            
            _theme.Typography.H3.FontFamily = new[] { "Koulen", "sans-serif" };
            _theme.Typography.H3.LetterSpacing = ".05rem";
            
            _theme.Typography.H4.FontFamily = new[] { "Koulen", "sans-serif" };
            _theme.Typography.H4.LetterSpacing = ".05rem";
            
            _theme.Typography.H5.FontFamily = new[] { "Koulen", "sans-serif" };
            _theme.Typography.H5.LetterSpacing = ".05rem";
            
            _theme.Typography.H6.FontFamily = new[] { "Koulen", "sans-serif" };
            _theme.Typography.H6.LetterSpacing = ".05rem";
            
            _theme.Typography.Subtitle1.FontFamily = new[] { "Roboto", "sans-serif" };
            _theme.Typography.Subtitle2.FontFamily = new[] { "Roboto", "sans-serif" };
            
            _theme.Typography.Button.FontFamily = new[] { "Roboto", "sans-serif" };
            _theme.Typography.Button.FontWeight = "700";

            PreferenceService.OnChange += UpdateState;
            
            // Quick Init for UI State
            await PreferenceService.InitializeAsync();
            _isDarkMode = PreferenceService.IsDarkMode;

            // Handle tokens in fragment BEFORE full init
            bool hasTokenInUrl = await ProcessUrlFragmentAsync();

            // YIELD early to allow the UI thread to render the initial shell
            await Task.Yield();

            // Start dependent services
            var authTask = AuthService.InitializeAsync();
            var sessionTask = SessionState != null ? SessionState.InitializeAsync() : Task.CompletedTask;

            await Task.WhenAll(authTask, sessionTask);
            


            // Only redirect to login if we aren't already there AND we aren't authenticated
            var currentUri = new Uri(Navigation.Uri);
            bool isAtLogin = currentUri.AbsolutePath.EndsWith("/login", StringComparison.OrdinalIgnoreCase);

            if (!AuthService.IsAuthenticated && !AuthService.IsGuest && !isAtLogin && !hasTokenInUrl)
            {
                 Navigation.NavigateTo("/login");
            }
        }

        private async Task<bool> ProcessUrlFragmentAsync()
        {
            var uri = Navigation.Uri;
            if (uri.Contains("#"))
            {
                var fragment = uri.Split('#')[1];
                var parts = fragment.Split('&');
                var tokenPart = parts.FirstOrDefault(p => p.StartsWith("access_token="));
                if (tokenPart != null)
                {
                    var token = tokenPart.Split('=')[1];
                    if (!string.IsNullOrEmpty(token))
                    {
                        await AuthService.SetAccessTokenAsync(token);
                        // Clean up URL
                        var baseUrl = uri.Split('#')[0];
                        Navigation.NavigateTo(baseUrl, replace: true);
                        return true;
                    }
                }
            }
            return false;
        }
        
        public void Dispose()
        {
            PreferenceService.OnChange -= UpdateState;
        }

        private void UpdateState()
        {
            _isDarkMode = PreferenceService.IsDarkMode;
            StateHasChanged();
        }

        private async Task ToggleDarkMode()
        {
            await PreferenceService.ToggleDarkModeAsync();
            _isDarkMode = PreferenceService.IsDarkMode;
        }

        private async Task LogoutAsync()
        {
            await AuthService.LogoutAsync();
            Navigation.NavigateTo("/login");
        }

        private async Task GoBack()
        {
            await JS.InvokeVoidAsync("goBack");
        }
    }
}
