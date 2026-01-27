// Copyright (c) GoldArrowTracker. Licensed under the GNU AFFERO GENERAL PUBLIC LICENSE v3.0

using GoldTracker.Shared.UI.Services.Abstractions;
using Microsoft.JSInterop;

namespace GoldTracker.Web.Services;

/// <summary>
/// Browser implementation of media picker using HTML file input.
/// </summary>
public class BrowserMediaPickerService : IMediaPickerService
{
    private readonly IJSRuntime _jsRuntime;

    public BrowserMediaPickerService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    public bool IsCaptureSupported => false; // No camera in browser

    public async Task<byte[]?> PickPhotoAsync()
    {
        try
        {
            var result = await _jsRuntime.InvokeAsync<string>("pickFile", "image/*", false);
            if (string.IsNullOrEmpty(result))
                return null;

            return Convert.FromBase64String(result);
        }
        catch
        {
            return null;
        }
    }

    public async Task<List<byte[]>> PickMultiplePhotosAsync()
    {
        try
        {
            var results = await _jsRuntime.InvokeAsync<string[]>("pickFile", "image/*", true);
            if (results == null || results.Length == 0)
                return new List<byte[]>();

            return results.Select(r => Convert.FromBase64String(r)).ToList();
        }
        catch
        {
            return new List<byte[]>();
        }
    }

    public Task<byte[]?> CapturePhotoAsync()
    {
        // Not supported in browser
        return Task.FromResult<byte[]?>(null);
    }
}
