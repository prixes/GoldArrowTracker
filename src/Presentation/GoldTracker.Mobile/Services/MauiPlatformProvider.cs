using GoldTracker.Shared.UI.Services.Abstractions;
using Microsoft.Maui.Devices;

namespace GoldTracker.Mobile.Services;

public class MauiPlatformProvider : IPlatformProvider
{
    public bool IsMobile => true;
    public bool IsWeb => false;
    public string PlatformName => DeviceInfo.Current.Platform.ToString();
}
