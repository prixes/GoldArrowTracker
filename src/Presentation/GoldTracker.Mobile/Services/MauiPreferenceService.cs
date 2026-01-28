using GoldTracker.Shared.UI.Services.Abstractions;
using Microsoft.Maui.Storage;

namespace GoldTracker.Mobile.Services;

public class MauiPreferenceService : IPreferenceService
{
    private bool _isDarkMode = false;
    public event Action? OnChange;

    public bool IsDarkMode
    {
        get => _isDarkMode;
        set
        {
            if (_isDarkMode != value)
            {
                _isDarkMode = value;
                Preferences.Set("is_dark_mode", value);
                OnChange?.Invoke();
            }
        }
    }

    public Task InitializeAsync()
    {
        _isDarkMode = Preferences.Get("is_dark_mode", false);
        return Task.CompletedTask;
    }

    public Task ToggleDarkModeAsync()
    {
        IsDarkMode = !IsDarkMode;
        return Task.CompletedTask;
    }

    public Task SetDarkModeAsync(bool isDark)
    {
        IsDarkMode = isDark;
        return Task.CompletedTask;
    }
}
