using Google.Apis.Auth;
using System.Text.Json;

namespace GoldTracker.Server.Auth;

public class GoogleAuthService
{
    private readonly ILogger<GoogleAuthService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;

    public GoogleAuthService(ILogger<GoogleAuthService> logger, IConfiguration configuration, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<string?> ExchangeCodeForTokenAsync(string code, string redirectUri)
    {
        try
        {
            var clientId = _configuration["Authentication:Google:ClientId"];
            var clientSecret = _configuration["Authentication:Google:ClientSecret"];
            
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "code", code },
                { "client_id", clientId ?? "" },
                { "client_secret", clientSecret ?? "" },
                { "redirect_uri", redirectUri },
                { "grant_type", "authorization_code" }
            });

            using var httpClient = _httpClientFactory.CreateClient();
            var response = await httpClient.PostAsync("https://oauth2.googleapis.com/token", content);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to exchange code: {Reason}", response.ReasonPhrase);
                return null;
            }

            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            if (json.TryGetProperty("id_token", out var token))
            {
                return token.GetString();
            }
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Code exchange failed");
            return null;
        }
    }

    public async Task<GoogleJsonWebSignature.Payload?> ValidateTokenAsync(string idToken)
    {
        try
        {
            // If ClientId is set, add to validation
            var webClientId = _configuration["Authentication:Google:ClientId"];
            var clientIds = _configuration.GetSection("Authentication:Google:ClientIds").Get<List<string>>() ?? new List<string>();
            
            if (!string.IsNullOrEmpty(webClientId)) 
            {
                clientIds.Add(webClientId);
            }

            var settings = new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = clientIds.Count > 0 ? clientIds : null
            };

            var payload = await GoogleJsonWebSignature.ValidateAsync(idToken, settings);
            return payload;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Token validation failed");
            return null;
        }
    }
}
