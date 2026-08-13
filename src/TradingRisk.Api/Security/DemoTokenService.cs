using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TradingRisk.Api.Options;

namespace TradingRisk.Api.Security;

/// <summary>
/// Creates a short-lived token only for the local learning environment. A real service
/// accepts tokens issued by an identity provider; it does not mint them from user input.
/// </summary>
public sealed class DemoTokenService(IOptions<SecurityOptions> options)
{
    public string Create(string userName, string role)
    {
        var security = options.Value;
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userName),
            new Claim(ClaimTypes.Name, userName),
            new Claim(ClaimTypes.Role, role)
        };
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(security.DemoSigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: security.Issuer,
            audience: security.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
