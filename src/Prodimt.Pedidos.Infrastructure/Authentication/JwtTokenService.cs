using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.Auth;
using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Infrastructure.Authentication;

public sealed class JwtTokenService(IConfiguration configuration, IDateTimeProvider dateTimeProvider) : IJwtTokenService
{
    public JwtTokenResult CreateCustomerToken(Customer customer)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, customer.Id.ToString()),
            new Claim(ProdimtAuthClaims.ActorType, ProdimtActorTypes.Customer),
            new Claim(ProdimtAuthClaims.CustomerId, customer.Id.ToString()),
            new Claim(ProdimtAuthClaims.CustomerName, customer.Name),
            new Claim(ClaimTypes.NameIdentifier, customer.Id.ToString()),
            new Claim(ClaimTypes.Name, customer.Name)
        };

        return CreateToken(claims);
    }

    public JwtTokenResult CreateAdminToken(AdminUser adminUser)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, adminUser.Id.ToString()),
            new Claim(ProdimtAuthClaims.ActorType, ProdimtActorTypes.Admin),
            new Claim(ProdimtAuthClaims.UserId, adminUser.Id.ToString()),
            new Claim(ProdimtAuthClaims.UserName, adminUser.UserName),
            new Claim(ProdimtAuthClaims.DisplayName, adminUser.DisplayName),
            new Claim(ClaimTypes.NameIdentifier, adminUser.Id.ToString()),
            new Claim(ClaimTypes.Name, adminUser.UserName)
        };

        return CreateToken(claims);
    }

    private JwtTokenResult CreateToken(IEnumerable<Claim> claims)
    {
        var settings = JwtSettings.FromConfiguration(configuration);
        var expiresAt = dateTimeProvider.Now.AddMinutes(settings.AccessTokenMinutes);
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SigningKey));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: settings.Issuer,
            audience: settings.Audience,
            claims: claims,
            notBefore: dateTimeProvider.Now.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return new JwtTokenResult(new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
