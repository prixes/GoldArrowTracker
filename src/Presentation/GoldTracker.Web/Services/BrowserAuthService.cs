using GoldTracker.Shared.UI.Services.Abstractions;
using GoldTracker.Shared.UI.Models;
using Microsoft.JSInterop;
using System;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Components;

namespace GoldTracker.Web.Services;

public class BrowserAuthService : IServerAuthService
{
    private readonly HttpClient _httpClient;
    private readonly IJSRuntime _jsRuntime;
    private readonly NavigationManager _navigationManager;
    private string? _accessToken;
    private UserInfo? _currentUser;
    private const string TokenKey = "auth_token";
    public event Action? OnSignedIn;

    public BrowserAuthService(HttpClient httpClient, IJSRuntime jsRuntime, NavigationManager navigationManager)
    {
        _httpClient = httpClient;
        _jsRuntime = jsRuntime;
        _navigationManager = navigationManager;
    }

    public bool IsAuthenticated => !string.IsNullOrEmpty(_accessToken);
    public UserInfo? CurrentUser => _currentUser;
    public bool IsGuest { get; private set; } = false;

    public async Task InitializeAsync()
    {
        if (!string.IsNullOrEmpty(_accessToken)) return;

        try
        {
            _accessToken = await _jsRuntime.InvokeAsync<string>("localStorage.getItem", TokenKey);
            if (!string.IsNullOrEmpty(_accessToken))
            {
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
                await GetUserInfoAsync();
                OnSignedIn?.Invoke();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading token: {ex.Message}");
        }
    }

    public async Task<string?> GetAccessTokenAsync()
    {
        return _accessToken;
    }

    public async Task<UserInfo?> GetUserInfoAsync()
    {
        if (string.IsNullOrEmpty(_accessToken)) return null;

        try
        {
            var user = await _httpClient.GetFromJsonAsync<UserInfo>("/api/auth/me");
            _currentUser = user;
            return user;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"GetUserInfo failed: {ex.Message}");
            // Token might be expired
            if (ex.Message.Contains("401"))
            {
                await LogoutAsync();
            }
            return null;
        }
    }

    public async Task<bool> LoginWithGoogleAsync(string idToken)
    {
        // Not used in redirect flow
        return false;
    }

    public async Task LogoutAsync()
    {
        _accessToken = null;
        _currentUser = null;
        _httpClient.DefaultRequestHeaders.Authorization = null;
        try
        {
            await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", TokenKey);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error removing token: {ex.Message}");
        }
    }

    public async Task SetAccessTokenAsync(string token)
    {
        _accessToken = token;
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (!string.IsNullOrEmpty(token))
        {
            try
            {
                await _jsRuntime.InvokeVoidAsync("localStorage.setItem", TokenKey, token);
                await GetUserInfoAsync();
                OnSignedIn?.Invoke();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving token: {ex.Message}");
            }
        }
    }

    public async Task SignInAsync()
    {
        // Redirect to server login
        var returnUrl = _navigationManager.Uri;
        if (returnUrl.Contains("/login"))
        {
            returnUrl = _navigationManager.BaseUri;
        }

        var loginUrl = $"{_httpClient.BaseAddress}api/auth/login-redirect?returnUrl={Uri.EscapeDataString(returnUrl)}&platform=web";
        _navigationManager.NavigateTo(loginUrl, forceLoad: true);
    }

    public void SetGuestMode()
    {
        IsGuest = true;
    }
}
