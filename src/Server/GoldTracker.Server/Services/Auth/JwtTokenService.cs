using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using GoldTracker.Server.Data;
using Microsoft.IdentityModel.Tokens;

namespace GoldTracker.Server.Services.Auth;

public interface ITokenService
{
    string GenerateJwt(User user);
}

public class JwtTokenService : ITokenService
{
    private readonly IConfiguration _config;

    public JwtTokenService(IConfiguration config)
    {
        _config = config;
    }

    public string GenerateJwt(User user)
    {
        var keyString = _config["Jwt:Key"];
        if (string.IsNullOrEmpty(keyString))
        {
            // Fallback only for strict dev envs or throw. 
            keyString = "super_secret_key_that_is_long_enough_for_hmac_sha256";
        }
        
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
