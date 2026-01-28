using Microsoft.AspNetCore.Mvc;
using GoldTracker.Server.Data;
using Microsoft.EntityFrameworkCore;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace GoldTracker.Server.Auth;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/google", async (
            [FromBody] GoogleLoginRequest request,
            GoogleAuthService authService,
            AppDbContext db,
            IConfiguration config) =>
        {
            // 1. Validate Google Token
            var payload = await authService.ValidateTokenAsync(request.IdToken);
            if (payload == null)
            {
                return Results.Unauthorized();
            }

            // 2. Find or Create User
            var user = await db.Users.FirstOrDefaultAsync(u => u.GoogleSubjectId == payload.Subject);
            if (user == null)
            {
                user = new User
                {
                    GoogleSubjectId = payload.Subject,
                    Email = payload.Email,
                    DisplayName = payload.Name,
                    PictureUrl = payload.Picture,
                    CreatedAt = DateTime.UtcNow
                };
                db.Users.Add(user);
            }
            else
            {
                user.DisplayName = payload.Name;
                user.PictureUrl = payload.Picture;
            }
            
            user.LastLoginAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            // 3. Generate App JWT
            var token = GenerateJwt(user, config);

            return Results.Ok(new AuthResponse(token, user.Email, user.DisplayName));
        });

        // Redirect Endpoint for WebAuthenticator or Browser
        app.MapGet("/api/auth/login-redirect", (string? returnUrl, string? platform, IConfiguration config, HttpContext context, ILogger<AuthEndpoints> logger) => 
        {
            logger.LogInformation("Login redirect requested. Platform: {Platform}, ReturnUrl: {ReturnUrl}", platform, returnUrl);
            
            var clientId = config["Authentication:Google:ClientId"];
            if (string.IsNullOrEmpty(clientId) || clientId == "YOUR_GOOGLE_CLIENT_ID")
            {
                return Results.BadRequest("Google Client ID is not configured on the server.");
            }

            // Simple state string: "platform|returnUrl" base64 encoded
            var stateValue = $"{(platform ?? "mobile")}|{(returnUrl ?? "")}";
            var stateBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(stateValue));

            var redirectUri = $"{context.Request.Scheme}://{context.Request.Host}/api/auth/google/callback";
            var googleAuthUrl = $"https://accounts.google.com/o/oauth2/v2/auth?response_type=code&client_id={clientId}&redirect_uri={Uri.EscapeDataString(redirectUri)}&scope=openid%20email%20profile&state={Uri.EscapeDataString(stateBase64)}";
            
            logger.LogDebug("Redirecting to Google OAuth...");
            return Results.Redirect(googleAuthUrl);
        });

        // Callback from Google
        app.MapGet("/api/auth/google/callback", async (
            string code, 
            string? state,
            IConfiguration config, 
            HttpContext context,
            ILogger<AuthEndpoints> logger,
            GoogleAuthService authService,
            AppDbContext db) =>
        {
            logger.LogInformation("Google callback received. State: {State}", state);
            
            var token = await authService.ExchangeCodeForTokenAsync(code, $"{context.Request.Scheme}://{context.Request.Host}/api/auth/google/callback");
            if (token == null) return Results.BadRequest("Failed to exchange code");
            
            var payload = await authService.ValidateTokenAsync(token);
            if (payload == null) return Results.Unauthorized();

            // Find or Create User
            var user = await db.Users.FirstOrDefaultAsync(u => u.GoogleSubjectId == payload.Subject);
            if (user == null)
            {
                user = new User
                {
                    GoogleSubjectId = payload.Subject,
                    Email = payload.Email,
                    DisplayName = payload.Name,
                    PictureUrl = payload.Picture,
                    CreatedAt = DateTime.UtcNow
                };
                db.Users.Add(user);
            }
            else
            {
                user.DisplayName = payload.Name;
                user.PictureUrl = payload.Picture;
            }
            user.LastLoginAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            var appToken = GenerateJwt(user, config);

            // Handle Redirection based on state
            string platform = "mobile";
            string? returnUrl = null;

            if (!string.IsNullOrEmpty(state))
            {
                try
                {
                    var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(state));
                    var parts = decoded.Split('|');
                    if (parts.Length >= 1) platform = parts[0];
                    if (parts.Length >= 2) returnUrl = parts[1];
                    logger.LogInformation("Parsed state - Platform: {Platform}, ReturnUrl: {ReturnUrl}", platform, returnUrl);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error parsing state");
                }
            }
            else
            {
                logger.LogWarning("No state parameter received from Google.");
            }

            if (platform == "web")
            {
                var finalUrl = returnUrl;
                if (string.IsNullOrEmpty(finalUrl))
                {
                    finalUrl = $"{context.Request.Scheme}://{context.Request.Host}/";
                }

                if (finalUrl.Contains("#"))
                {
                    finalUrl += $"&access_token={appToken}";
                }
                else
                {
                    finalUrl += $"#access_token={appToken}";
                }
                
                logger.LogInformation("Redirecting back to Web: {FinalUrl}", finalUrl);
                return Results.Redirect(finalUrl);
            }

            return Results.Redirect($"goldtracker://#access_token={appToken}");
        });

        app.MapGet("/api/auth/me", async (HttpContext context, AppDbContext db) => 
        {
            var userIdStr = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            {
                return Results.Unauthorized();
            }

            var user = await db.Users.FindAsync(userId);
            if (user == null) return Results.Unauthorized();

            return Results.Ok(new UserInfo(user.DisplayName ?? user.Email, user.Email, user.PictureUrl));
        }).RequireAuthorization();
    }

    private static string GenerateJwt(User user, IConfiguration config)
    {
        var keyString = config["Jwt:Key"] ?? "super_secret_key_that_is_long_enough_for_hmac_sha256";
        var key = Encoding.ASCII.GetBytes(keyString);
        
        var tokenHandler = new JwtSecurityTokenHandler();
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Email, user.Email)
            }),
            Expires = DateTime.UtcNow.AddDays(30),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };
        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }
}

public record GoogleLoginRequest(string IdToken);
public record AuthResponse(string Token, string Email, string? Name);
public record UserInfo(string Name, string Email, string? PictureUrl);
