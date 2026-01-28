using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Maui.Storage;

using Microsoft.Extensions.Configuration;
using GoldTracker.Shared.UI.Models;
using System.Net.Http.Headers;

using GoldTracker.Shared.UI.Services.Abstractions;
using GoldTracker.Shared.UI;

using Microsoft.Maui.Authentication;

namespace GoldTracker.Mobile.Services;

public class ServerAuthService : IServerAuthService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private string? _accessToken;
    private UserInfo? _currentUser;
    private const string TokenKey = "auth_token";
    private bool _isGuest = false;
    public event Action? OnSignedIn;

    public ServerAuthService(IConfiguration configuration)
    {
        _configuration = configuration;
        var serverUrl = configuration["Settings:ServerUrl"] ?? "http://localhost:5000";
        _httpClient = new HttpClient { BaseAddress = new Uri(serverUrl) };
    }

    public bool IsAuthenticated => !string.IsNullOrEmpty(_accessToken);
    public bool IsGuest => _isGuest;
    public UserInfo? CurrentUser => _currentUser;
    
    public void SetGuestMode()
    {
        _isGuest = true;
    }

    public async Task InitializeAsync()
    {
        try
        {
            _accessToken = await Task.Run(() => SecureStorage.Default.GetAsync(TokenKey));
            if (!string.IsNullOrEmpty(_accessToken))
            {
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
                await GetUserInfoAsync();
                OnSignedIn?.Invoke();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading token: {ex.Message}");
        }
    }

    public async Task SetAccessTokenAsync(string token)
    {
        _accessToken = token;
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        await GetUserInfoAsync();
        if (!string.IsNullOrEmpty(token))
        {
             OnSignedIn?.Invoke();
        }
        try
        {
            await Task.Run(() => SecureStorage.Default.SetAsync(TokenKey, token));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving token: {ex.Message}");
        }
    }

    public async Task<bool> LoginWithGoogleAsync(string idToken)
    {
        // ... (existing implementation is mostly for server-to-server or test)
        // Ignoring for now as we use Redirect flow mostly
        return false; 
    }

    public async Task<UserInfo?> GetUserInfoAsync()
    {
        if (string.IsNullOrEmpty(_accessToken)) return null;

        try
        {
            var user = await _httpClient.GetFromJsonAsync<UserInfo>("/api/auth/me");
            System.Diagnostics.Debug.WriteLine($"[ServerAuthService] Fetched User: {user?.Name}, Email: {user?.Email}, Pic: {user?.PictureUrl}");
            _currentUser = user;
            return user;
        }
        catch (Exception ex)
        {
             System.Diagnostics.Debug.WriteLine($"GetUserInfo failed: {ex.Message}");
             return null;
        }
    }

    public async Task SignInAsync()
    {
        try
        {
            // For Android Emulator (10.0.2.2) vs Device (Actual IP or via Tunnel)
            // The configuration should handle this.
            var serverUrl = _configuration["Settings:ServerUrl"] ?? "http://10.0.2.2:5000";
            var authUrl = $"{serverUrl}/api/auth/login-redirect"; 
            
            var result = await WebAuthenticator.Default.AuthenticateAsync(
                new Uri(authUrl),
                new Uri("goldtracker://")
            );

            if (result?.Properties.TryGetValue("access_token", out var token) == true && !string.IsNullOrEmpty(token))
            {
                 await SetAccessTokenAsync(token);
            }
            else if (!string.IsNullOrEmpty(result?.AccessToken))
            {
                await SetAccessTokenAsync(result.AccessToken);
            }
        }
        catch (TaskCanceledException)
        {
            // User canceled
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Sign In Error: {ex.Message}");
        }
    }

    public Task<string?> GetAccessTokenAsync()
    {
        return Task.FromResult(_accessToken);
    }

    public async Task LogoutAsync()
    {
        _accessToken = null;
        _currentUser = null;
        _httpClient.DefaultRequestHeaders.Authorization = null;
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
