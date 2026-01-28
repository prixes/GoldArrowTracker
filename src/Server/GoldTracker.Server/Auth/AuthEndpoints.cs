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
                    CreatedAt = DateTime.UtcNow
                };
                db.Users.Add(user);
            }
            
            user.LastLoginAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            // 3. Generate App JWT
            var token = GenerateJwt(user, config);

            return Results.Ok(new AuthResponse(token, user.Email, user.DisplayName));
        });

        // Redirect Endpoint for WebAuthenticator
        app.MapGet("/api/auth/login-redirect", (IConfiguration config, HttpContext context) => 
        {
            var clientId = config["Authentication:Google:ClientId"];
            if (string.IsNullOrEmpty(clientId) || clientId == "YOUR_GOOGLE_CLIENT_ID")
            {
                return Results.BadRequest("Google Client ID is not configured on the server.");
            }

            // REAL GOOGLE FLOW
            var redirectUri = $"{context.Request.Scheme}://{context.Request.Host}/api/auth/google/callback";
            var googleAuthUrl = $"https://accounts.google.com/o/oauth2/v2/auth?response_type=code&client_id={clientId}&redirect_uri={System.Net.WebUtility.UrlEncode(redirectUri)}&scope=openid%20email%20profile";
            
            return Results.Redirect(googleAuthUrl);
        });

        // Callback from Google
        app.MapGet("/api/auth/google/callback", async (
            string code, 
            IConfiguration config, 
            HttpContext context,
            GoogleAuthService authService,
            AppDbContext db) =>
        {
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
                    CreatedAt = DateTime.UtcNow
                };
                db.Users.Add(user);
            }
            user.LastLoginAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            var appToken = GenerateJwt(user, config);
            return Results.Redirect($"goldtracker://#access_token={appToken}");
        });
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
