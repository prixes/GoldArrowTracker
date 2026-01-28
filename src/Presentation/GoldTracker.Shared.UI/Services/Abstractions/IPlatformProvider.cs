namespace GoldTracker.Shared.UI.Services.Abstractions;

public interface IPlatformProvider
{
    bool IsMobile { get; }
    bool IsWeb { get; }
    string PlatformName { get; }
}
