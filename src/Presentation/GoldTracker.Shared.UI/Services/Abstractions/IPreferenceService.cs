namespace GoldTracker.Shared.UI.Services.Abstractions;

public interface IPreferenceService
{
    bool IsDarkMode { get; set; }
    event Action OnChange;
    Task InitializeAsync();
    Task ToggleDarkModeAsync();
    Task SetDarkModeAsync(bool isDark);
}
