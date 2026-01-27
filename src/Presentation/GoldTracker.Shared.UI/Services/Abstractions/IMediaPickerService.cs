// Copyright (c) GoldArrowTracker. Licensed under the GNU AFFERO GENERAL PUBLIC LICENSE v3.0

namespace GoldTracker.Shared.UI.Services.Abstractions;

/// <summary>
/// Platform-agnostic interface for media picking (camera/file selection).
/// Implementations: Mobile uses MAUI MediaPicker, Web uses HTML file input.
/// </summary>
public interface IMediaPickerService
{
    /// <summary>
    /// Picks a photo from the device/browser.
    /// </summary>
    /// <returns>Image bytes, or null if cancelled</returns>
    Task<byte[]?> PickPhotoAsync();

    /// <summary>
    /// Picks multiple photos from the device/browser.
    /// </summary>
    /// <returns>List of image bytes</returns>
    Task<List<byte[]>> PickMultiplePhotosAsync();

    /// <summary>
    /// Captures a photo using the camera (mobile only).
    /// </summary>
    /// <returns>Image bytes, or null if not supported/cancelled</returns>
    Task<byte[]?> CapturePhotoAsync();

    /// <summary>
    /// Indicates if camera capture is supported on this platform.
    /// </summary>
    bool IsCaptureSupported { get; }
}
