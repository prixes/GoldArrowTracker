// Copyright (c) GoldArrowTracker. Licensed under the GNU AFFERO GENERAL PUBLIC LICENSE v3.0

namespace GoldTracker.Shared.UI.Services.Abstractions;

/// <summary>
/// Platform-agnostic interface for file storage operations.
/// Implementations: Mobile saves to device storage, Web downloads files.
/// </summary>
public interface IFileStorageService
{
    /// <summary>
    /// Saves a file with the given data.
    /// </summary>
    /// <param name="fileName">Name of the file</param>
    /// <param name="data">File data</param>
    /// <param name="subdirectory">Optional subdirectory</param>
    /// <returns>Path or identifier of saved file</returns>
    Task<string?> SaveFileAsync(string fileName, byte[] data, string? subdirectory = null);

    /// <summary>
    /// Saves multiple files (e.g., for dataset export).
    /// </summary>
    /// <param name="files">Dictionary of filename -> data</param>
    /// <param name="subdirectory">Optional subdirectory</param>
    Task SaveMultipleFilesAsync(Dictionary<string, byte[]> files, string? subdirectory = null);

    /// <summary>
    /// Reads a file from storage.
    /// </summary>
    Task<byte[]?> ReadFileAsync(string filePath);

    /// <summary>
    /// Deletes a file from storage.
    /// </summary>
    Task<bool> DeleteFileAsync(string filePath);

    /// <summary>
    /// Gets a list of files in a directory.
    /// </summary>
    Task<List<string>> GetFilesAsync(string? subdirectory = null);
}
