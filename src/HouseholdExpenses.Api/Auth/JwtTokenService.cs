using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using HouseholdExpenses.Api.Data;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace HouseholdExpenses.Api.Auth;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "HouseholdExpenses";
    public string Audience { get; set; } = "HouseholdExpenses.Web";
    /// <summary>Symmetric signing key; at least 32 characters. Supply via user-secrets or environment variable.</summary>
    public string Key { get; set; } = string.Empty;
    public int ExpiryMinutes { get; set; } = 60 * 8;
}

public interface IJwtTokenService
{
    (string Token, DateTime ExpiresAtUtc) Create(ApplicationUser user);
}

public class JwtTokenService(IOptions<JwtOptions> options) : IJwtTokenService
{
    private readonly JwtOptions _options = options.Value;

    public (string Token, DateTime ExpiresAtUtc) Create(ApplicationUser user)
    {
        var expires = DateTime.UtcNow.AddMinutes(_options.ExpiryMinutes);
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key)), SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id),
            new Claim(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new Claim("name", user.DisplayName),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(_options.Issuer, _options.Audience, claims, expires: expires, signingCredentials: credentials);
        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
