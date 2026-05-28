using System.Security.Cryptography;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.AdminOrders;
using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Application.AdminCatalogs;

public sealed class AdminCustomerAccessTokenService(
    ICustomerRepository customers,
    ICustomerAccessTokenRepository accessTokens,
    ICustomerAccessTokenHasher tokenHasher,
    IAuditLogRepository auditLogs,
    IDateTimeProvider dateTimeProvider)
{
    public async Task<IReadOnlyList<AdminCustomerAccessTokenResponse>> GetByCustomerAsync(
        Guid customerId,
        CancellationToken cancellationToken)
    {
        await GetRequiredCustomerAsync(customerId, cancellationToken);
        var tokens = await accessTokens.GetByCustomerIdAsync(customerId, cancellationToken);
        return tokens.Select(MapToken).ToArray();
    }

    public async Task<CreatedCustomerAccessTokenResponse> CreateAsync(
        Guid customerId,
        CreateCustomerAccessTokenRequest request,
        AdminActorContext? actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var customer = await GetRequiredCustomerAsync(customerId, cancellationToken);
        if (!customer.IsActive)
        {
            throw new ArgumentException("No se puede crear token para un cliente inactivo.", nameof(customerId));
        }

        if (request.ExpiresAt is not null && request.ExpiresAt <= dateTimeProvider.Now)
        {
            throw new ArgumentException("La expiracion del token debe ser futura.", nameof(request));
        }

        var plainToken = GeneratePlainToken();
        var token = CustomerAccessToken.Create(
            customerId,
            tokenHasher.HashToken(plainToken),
            request.Description,
            request.ExpiresAt,
            dateTimeProvider.Now);

        await accessTokens.AddAsync(token, cancellationToken);
        await auditLogs.AddAsync(CatalogAudit.Create(
            CatalogEntityTypes.CustomerAccessToken,
            token.Id,
            CatalogAuditEventTypes.CustomerAccessTokenCreated,
            dateTimeProvider.Now,
            actor,
            $"Token de acceso creado para {customer.Name}.",
            new
            {
                token.CustomerId,
                description = token.DisplayName,
                token.ExpiresAt
            }), cancellationToken);
        await accessTokens.SaveChangesAsync(cancellationToken);

        return new CreatedCustomerAccessTokenResponse(
            token.Id,
            token.CustomerId,
            plainToken,
            token.DisplayName,
            token.ExpiresAt,
            token.IsActive);
    }

    public async Task<AdminCustomerAccessTokenResponse> RevokeAsync(
        Guid customerId,
        Guid tokenId,
        AdminActorContext? actor,
        CancellationToken cancellationToken)
    {
        await GetRequiredCustomerAsync(customerId, cancellationToken);
        var token = await accessTokens.GetByIdAsync(tokenId, cancellationToken);

        if (token is null || token.CustomerId != customerId)
        {
            throw new InvalidOperationException("Customer access token was not found.");
        }

        token.Revoke();
        await auditLogs.AddAsync(CatalogAudit.Create(
            CatalogEntityTypes.CustomerAccessToken,
            token.Id,
            CatalogAuditEventTypes.CustomerAccessTokenRevoked,
            dateTimeProvider.Now,
            actor,
            "Token de acceso de cliente revocado.",
            new
            {
                token.CustomerId,
                description = token.DisplayName
            }), cancellationToken);
        await accessTokens.SaveChangesAsync(cancellationToken);

        return MapToken(token);
    }

    private async Task<Customer> GetRequiredCustomerAsync(Guid customerId, CancellationToken cancellationToken)
    {
        var customer = await customers.GetByIdAsync(customerId, cancellationToken);

        if (customer is null)
        {
            throw new InvalidOperationException("Customer was not found.");
        }

        return customer;
    }

    private static string GeneratePlainToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    private static AdminCustomerAccessTokenResponse MapToken(CustomerAccessToken token)
    {
        return new AdminCustomerAccessTokenResponse(
            token.Id,
            token.CustomerId,
            token.DisplayName,
            token.ExpiresAt,
            token.IsActive,
            token.CreatedAt,
            token.LastUsedAt);
    }
}
