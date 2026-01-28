using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Maui.Storage;

using Microsoft.Extensions.Configuration;

namespace GoldTracker.Mobile.Services;

public interface IServerAuthService
{
    Task<bool> LoginWithGoogleAsync(string idToken);
    Task SetAccessTokenAsync(string token);
    Task<string?> GetAccessTokenAsync();
    bool IsAuthenticated { get; }
    Task InitializeAsync();
    Task LogoutAsync();
}

public class ServerAuthService : IServerAuthService
{
    private readonly HttpClient _httpClient;
    private string? _accessToken;
    private const string TokenKey = "auth_token";

    public ServerAuthService(IConfiguration configuration)
    {
        var serverUrl = configuration["Settings:ServerUrl"] ?? "http://localhost:5000";
        _httpClient = new HttpClient { BaseAddress = new Uri(serverUrl) };
    }

    public bool IsAuthenticated => !string.IsNullOrEmpty(_accessToken);

    public async Task InitializeAsync()
    {
        try
        {
            _accessToken = await SecureStorage.Default.GetAsync(TokenKey);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading token: {ex.Message}");
        }
    }

    public async Task SetAccessTokenAsync(string token)
    {
        _accessToken = token;
        try
        {
            await SecureStorage.Default.SetAsync(TokenKey, token);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving token: {ex.Message}");
        }
    }

    public async Task<bool> LoginWithGoogleAsync(string idToken)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("/api/auth/google", new { IdToken = idToken });
            
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<AuthResponse>();
                if (result != null)
                {
                    await SetAccessTokenAsync(result.Token);
                    return true;
                }
            }
            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Login failed: {ex.Message}");
            return false;
        }
    }

    public Task<string?> GetAccessTokenAsync()
    {
        return Task.FromResult(_accessToken);
    }

    public async Task LogoutAsync()
    {
        _accessToken = null;
        try
        {
            SecureStorage.Default.Remove(TokenKey);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error removing token: {ex.Message}");
        }
    }
}

public record AuthResponse(string Token, string Email, string? Name);
