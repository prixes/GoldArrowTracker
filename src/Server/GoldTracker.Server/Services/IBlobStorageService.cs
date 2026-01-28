namespace GoldTracker.Server.Services;

public interface IBlobStorageService
{
    Task UploadBlobAsync(string container, string blobName, Stream content);
    Task<Stream> GetBlobAsync(string container, string blobName);
    Task<bool> ExistsAsync(string container, string blobName);
    Task<IEnumerable<string>> ListBlobsAsync(string container);
}
