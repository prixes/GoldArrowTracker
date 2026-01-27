// Copyright (c) GoldArrowTracker. Licensed under the GNU AFFERO GENERAL PUBLIC LICENSE v3.0

using GoldTracker.Shared.UI.Services.Abstractions;
using Microsoft.JSInterop;

namespace GoldTracker.Web.Services;

/// <summary>
/// Browser implementation of file storage using download functionality.
/// </summary>
public class BrowserFileStorageService : IFileStorageService
{
    private readonly IJSRuntime _jsRuntime;

    public BrowserFileStorageService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    public async Task<string?> SaveFileAsync(string fileName, byte[] data, string? subdirectory = null)
    {
        try
        {
            var base64 = Convert.ToBase64String(data);
            await _jsRuntime.InvokeVoidAsync("downloadFile", fileName, base64);
            return fileName;
        }
        catch
        {
            return null;
        }
    }

    public async Task SaveMultipleFilesAsync(Dictionary<string, byte[]> files, string? subdirectory = null)
    {
        // For browser, we'll create a zip file and download it
        // For now, download files individually
        foreach (var file in files)
        {
            await SaveFileAsync(file.Key, file.Value, subdirectory);
        }
    }

    public Task<byte[]?> ReadFileAsync(string filePath)
    {
        // Not supported in browser (files are downloaded, not stored)
        return Task.FromResult<byte[]?>(null);
    }

    public Task<bool> DeleteFileAsync(string filePath)
    {
        // Not supported in browser
        return Task.FromResult(false);
    }

    public Task<List<string>> GetFilesAsync(string? subdirectory = null)
    {
        // Not supported in browser
        return Task.FromResult(new List<string>());
    }
}
