using System;
using GoldTracker.Shared.UI.Models;
using System.Threading.Tasks;

namespace GoldTracker.Shared.UI.Services.Abstractions;

public interface IServerAuthService
{
    Task<bool> LoginWithGoogleAsync(string idToken);
    Task SetAccessTokenAsync(string token);
    Task<string?> GetAccessTokenAsync();
    bool IsAuthenticated { get; }
    UserInfo? CurrentUser { get; }
    Task<UserInfo?> GetUserInfoAsync();
    Task InitializeAsync();
    Task LogoutAsync();
    Task SignInAsync();
    void SetGuestMode();
    bool IsGuest { get; }
    event Action OnSignedIn;
}
