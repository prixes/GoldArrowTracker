using System.Text.Json;
using Archery.Shared.Models;
using GoldTracker.Server.Services;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace GoldTracker.Server.Api;

public static class SessionEndpoints
{
    public static void MapSessionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/sessions").RequireAuthorization();

        // Upload Session
        group.MapPost("/", async (
            [FromBody] Session session,
            IBlobStorageService storage,
            ClaimsPrincipal user,
            ILogger<Session> logger) =>
        {
            try
            {
                var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

                // Path: sessions/{userId}/{sessionId}.json
                var blobName = $"{userId}/{session.Id}.json";
                
                using var stream = new MemoryStream();
                await JsonSerializer.SerializeAsync(stream, session);
                stream.Position = 0;

                await storage.UploadBlobAsync("sessions", blobName, stream);
                
                logger.LogInformation($"Saved session {session.Id} for user {userId}");
                return Results.Ok();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error saving session");
                return Results.Problem("Error saving session");
            }
        });

        // Upload Image related to session
        group.MapPost("/{sessionId}/images/{fileName}", async (
            string sessionId,
            string fileName,
            IFormFile file,
            IBlobStorageService storage,
            ClaimsPrincipal user,
             ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("SessionImages");
            var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

            // Path: sessions/{userId}/{sessionId}/images/{fileName}
            var blobName = $"{userId}/{sessionId}/images/{fileName}";

            try 
            {
                using var stream = file.OpenReadStream();
                await storage.UploadBlobAsync("sessions", blobName, stream);
                return Results.Ok(new { Path = blobName });
            }
            catch(Exception ex)
            {
                logger.LogError(ex, "Error saving image");
                return Results.Problem("Internal Server Error");
            }
        }).DisableAntiforgery(); 

        // List Sessions
        group.MapGet("/", async (
            IBlobStorageService storage,
            ClaimsPrincipal user,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("SessionEndpoints");
            var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            
            logger.LogInformation("Session List Request. UserID from Claim: {UserId}. Authenticated: {IsAuth}", userId, user.Identity?.IsAuthenticated);

            if (string.IsNullOrEmpty(userId)) 
            {
                logger.LogWarning("Unauthorized: No UserID claim found.");
                return Results.Unauthorized();
            }

            // List blobs in "sessions/{userId}"
            var containerPath = $"sessions/{userId}";
            var files = await storage.ListBlobsAsync(containerPath);
            var fileList = files.ToList();
            
            logger.LogInformation("Searching for sessions in {Path}. Found {Count} files.", containerPath, fileList.Count);
            
            var sessions = new List<Session>();

            foreach (var file in fileList)
            {
                if (file.EndsWith(".json"))
                {
                    try 
                    {
                        using var stream = await storage.GetBlobAsync(containerPath, file);
                        var session = await JsonSerializer.DeserializeAsync<Session>(stream);
                        if (session != null) sessions.Add(session);
                    }
                    catch (Exception ex)
                    {
                         logger.LogError(ex, "Failed to load session {File}", file);
                    }
                }
            }

            return Results.Ok(sessions.OrderByDescending(s => s.StartTime));
        });

        // Download Image
        group.MapGet("/{sessionId}/images/{fileName}", async (
            string sessionId,
            string fileName,
            IBlobStorageService storage,
            ClaimsPrincipal user) =>
        {
            var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

            var blobName = $"{userId}/{sessionId}/images/{fileName}";
            
            // 1. Try exact match
            if (await storage.ExistsAsync("sessions", blobName))
            {
                var stream = await storage.GetBlobAsync("sessions", blobName);
                return Results.Stream(stream, "image/jpeg");
            }

            // 2. Resilient Fallback: If exact match fails, search for a file that ends with this name
            // (Fixes issues where files were uploaded with 'synced_{id}_' prefixes)
            try
            {
                var imagesPath = $"sessions/{userId}/{sessionId}/images";
                var files = await storage.ListBlobsAsync(imagesPath);
                var matchingFile = files.FirstOrDefault(f => 
                    f.Equals(fileName, StringComparison.OrdinalIgnoreCase) || 
                    f.EndsWith("_" + fileName, StringComparison.OrdinalIgnoreCase));

                if (matchingFile != null)
                {
                    var fallbackBlobName = $"{userId}/{sessionId}/images/{matchingFile}";
                    var stream = await storage.GetBlobAsync("sessions", fallbackBlobName);
                    return Results.Stream(stream, "image/jpeg");
                }
            }
            catch { /* Ignore search errors */ }

            return Results.NotFound();
        });
    }
}
