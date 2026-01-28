using System.Text.Json;
using Archery.Shared.Models;
using GoldTracker.Server.Services;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;

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
            var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

            // List blobs in "sessions/{userId}"
            var containerPath = $"sessions/{userId}";
            var files = await storage.ListBlobsAsync(containerPath);
            
            var sessions = new List<Session>();

            foreach (var file in files)
            {
                if (file.EndsWith(".json"))
                {
                    try 
                    {
                        using var stream = await storage.GetBlobAsync(containerPath, file);
                        var session = await JsonSerializer.DeserializeAsync<Session>(stream);
                        if (session != null) sessions.Add(session);
                    }
                    catch {}
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
            
            if (await storage.ExistsAsync("sessions", blobName))
            {
                var stream = await storage.GetBlobAsync("sessions", blobName);
                return Results.Stream(stream, "image/jpeg"); // Assume jpeg for simplicity
            }
            return Results.NotFound();
        });
    }
}
