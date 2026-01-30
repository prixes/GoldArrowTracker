using GoldTracker.Shared.UI.Services.Abstractions;
using Microsoft.JSInterop;

namespace GoldTracker.Web.Services;

public class BrowserPreferenceService : IPreferenceService
{
    private readonly IJSRuntime _jsRuntime;
    private bool _isDarkMode = false;
    private const string DarkModeKey = "is_dark_mode";

    public event Action? OnChange;

    public BrowserPreferenceService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    public bool IsDarkMode
    {
        get => _isDarkMode;
        set
        {
            if (_isDarkMode != value)
            {
                _isDarkMode = value;
                // Fire and forget storage update
                _ = SavePreferenceAsync(DarkModeKey, value);
                OnChange?.Invoke();
            }
        }
    }

    public async Task InitializeAsync()
    {
        try
        {
            var value = await _jsRuntime.InvokeAsync<string>("localStorage.getItem", DarkModeKey);
            if (bool.TryParse(value, out var result))
            {
                _isDarkMode = result;
            }
        }
        catch
        {
            // Fallback to default
            _isDarkMode = false;
        }
    }

    public Task ToggleDarkModeAsync()
    {
        IsDarkMode = !IsDarkMode;
        return Task.CompletedTask;
    }

    public async Task SetDarkModeAsync(bool isDark)
    {
        IsDarkMode = isDark;
        await SavePreferenceAsync(DarkModeKey, isDark);
    }

    private async Task SavePreferenceAsync(string key, bool value)
    {
        try
        {
            await _jsRuntime.InvokeVoidAsync("localStorage.setItem", key, value.ToString().ToLower());
        }
        catch
        {
            // Ignore storage errors on web
        }
    }
}
