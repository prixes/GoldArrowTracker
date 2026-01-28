using GoldTracker.Shared.UI.Services.Abstractions;

namespace GoldTracker.Mobile.Services;

public class PathService : IPathService
{
    public string GetAppDataPath()
    {
        return FileSystem.AppDataDirectory;
    }

    public string GetDownloadsPath()
    {
#if ANDROID
        var downloadsPath = Android.OS.Environment.GetExternalStoragePublicDirectory(Android.OS.Environment.DirectoryDownloads);
        return downloadsPath?.AbsolutePath ?? "/storage/emulated/0/Download";
#else
        return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
#endif
    }

    public string GetExportPath()
    {
        return Path.Combine(GetDownloadsPath(), "Export");
    }
}
