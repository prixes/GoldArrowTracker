using System.Security.Claims;
using GoldTracker.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace GoldTracker.Server.Api;

public static class DatasetEndpoints
{
    public static void MapDatasetEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/datasets").RequireAuthorization();

        // Upload a single file to the dataset structure
        // Endpoint: POST /api/datasets/upload?relativePath=Macro_Model/images/foo.jpg
        group.MapPost("/upload", async (
            IFormFile file,
            [FromQuery] string relativePath,
            IBlobStorageService storage,
            ClaimsPrincipal user,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("DatasetUpload");
            
            // 1. Get User ID
            var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

            if (file == null || file.Length == 0) return Results.BadRequest("No file uploaded");
            if (string.IsNullOrEmpty(relativePath)) return Results.BadRequest("relativePath is required");

            // 2. Determine target Blob Name
            // Old Structure: datasets/{userId}/{relativePath}
            // New Structure: datasets/{relativePath_dir}/{shortUserId}_{filename}
            
            // Normalize separators to forward slash
            var safePath = relativePath.Replace("\\", "/").Replace("..", "").TrimStart('/');
            
            // Manually parse directory vs filename to avoid OS-specific Path.GetDirectoryName issues (which caused bugs)
            var lastSlashIndex = safePath.LastIndexOf('/');
            
            string directory = "";
            string fileName = safePath;
            
            if (lastSlashIndex >= 0)
            {
                directory = safePath.Substring(0, lastSlashIndex);
                fileName = safePath.Substring(lastSlashIndex + 1);
            }
            
            // Shorten User ID (GUID) to first segment (8 chars) for brevity
            var shortUserId = userId.Split('-')[0];
            
            // Prepend UserID to filename
            var newFileName = $"{shortUserId}_{fileName}";
            var newRelativePath = string.IsNullOrEmpty(directory) ? newFileName : $"{directory}/{newFileName}";

            var blobName = $"datasets/{newRelativePath}";

            try
            {
                using var stream = file.OpenReadStream();
                // Check if file exists? Does not matter, usually overwrite for sync.
                // Or maybe we skip if size matches? For now, nice and simple overwrite.
                
                await storage.UploadBlobAsync("sessions", blobName, stream); // We store in "sessions" container root but blobName is "datasets/..." which is fine. 
                                                                             // Actually, LocalFileSystemStorageService puts everything in App_Data/Storage/{container}.
                                                                             // If we pass "sessions", it goes to App_Data/Storage/sessions/datasets/...
                                                                             // The user wanted "same file structure". 
                                                                             // If we want root/datasets, we should use a "datasets" container.
                
                return Results.Ok();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to upload dataset file: {Path}", relativePath);
                return Results.Problem("Upload failed");
            }
        }).DisableAntiforgery();
    }
}
