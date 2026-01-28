
namespace GoldTracker.Shared.UI.Services.Abstractions;

/// <summary>
/// Provides platform-specific paths for storage locations.
/// </summary>
public interface IPathService
{
    /// <summary>
    /// Gets the application data directory (internal storage).
    /// </summary>
    string GetAppDataPath();

    /// <summary>
    /// Gets the public downloads directory (or equivalent).
    /// </summary>
    string GetDownloadsPath();

    /// <summary>
    /// Gets the specifically configured export path for datasets.
    /// </summary>
    string GetExportPath();
}
