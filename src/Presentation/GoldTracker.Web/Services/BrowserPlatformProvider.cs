using GoldTracker.Shared.UI.Services.Abstractions;

namespace GoldTracker.Web.Services;

public class BrowserPlatformProvider : IPlatformProvider
{
    public bool IsMobile => false;
    public bool IsWeb => true;
    public string PlatformName => "WebAssembly";
}
