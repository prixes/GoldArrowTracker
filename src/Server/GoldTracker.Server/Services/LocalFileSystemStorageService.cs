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
        
        // Simple retry policy for handling file locks
        int maxRetries = 3;
        int delay = 100;

        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                using var fileStream = new FileStream(fullFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
                await content.CopyToAsync(fileStream);
                return;
            }
            catch (IOException ex) when (i < maxRetries - 1)
            {
                _logger.LogWarning($"File lock encountered for {blobName}, retrying ({i + 1}/{maxRetries})... Details: {ex.Message}");
                await Task.Delay(delay);
                delay *= 2; // Exponential backoff
                content.Position = 0; // Reset stream position for retry
            }
        }
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
