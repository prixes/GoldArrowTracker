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
    }
}
