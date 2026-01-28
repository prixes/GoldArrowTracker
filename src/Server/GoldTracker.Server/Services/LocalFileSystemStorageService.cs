namespace GoldTracker.Server.Services;

public class LocalFileSystemStorageService : IBlobStorageService
{
    private readonly string _rootPath;
    private readonly ILogger<LocalFileSystemStorageService> _logger;

    public LocalFileSystemStorageService(IWebHostEnvironment env, ILogger<LocalFileSystemStorageService> logger)
    {
        // Store in /App_Data/Storage inside the content root
        _rootPath = Path.Combine(env.ContentRootPath, "App_Data", "Storage");
        _logger = logger;
        
        if (!Directory.Exists(_rootPath))
        {
            Directory.CreateDirectory(_rootPath);
        }
    }

    public async Task UploadBlobAsync(string container, string blobName, Stream content)
    {
        var containerPath = Path.Combine(_rootPath, container);
        // Ensure container dir exists
        if (!Directory.Exists(containerPath))
        {
            Directory.CreateDirectory(containerPath);
        }

        var fullFilePath = Path.Combine(containerPath, blobName);
        var parentDir = Path.GetDirectoryName(fullFilePath);

        // Ensure parent directory for the specific file exists (e.g. for userId subfolders)
        if (parentDir != null && !Directory.Exists(parentDir))
        {
            Directory.CreateDirectory(parentDir);
        }

        _logger.LogInformation("Writing file to {FilePath}", fullFilePath);
        
        using var fileStream = new FileStream(fullFilePath, FileMode.Create, FileAccess.Write);
        await content.CopyToAsync(fileStream);
    }

    public Task<Stream> GetBlobAsync(string container, string blobName)
    {
        var filePath = Path.Combine(_rootPath, container, blobName);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Blob not found", filePath);
        }
        
        // Return file stream
        return Task.FromResult<Stream>(new FileStream(filePath, FileMode.Open, FileAccess.Read));
    }

    public Task<bool> ExistsAsync(string container, string blobName)
    {
        var filePath = Path.Combine(_rootPath, container, blobName);
        return Task.FromResult(File.Exists(filePath));
    }

    public Task<IEnumerable<string>> ListBlobsAsync(string container)
    {
        var dirPath = Path.Combine(_rootPath, container);
        if (!Directory.Exists(dirPath))
        {
            return Task.FromResult(Enumerable.Empty<string>());
        }

        var files = Directory.GetFiles(dirPath).Select(Path.GetFileName).Cast<string>();
        return Task.FromResult(files);
    }
}
