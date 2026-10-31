using Prodimt.Pedidos.Application.Abstractions;

namespace Prodimt.Pedidos.Application.Auth;

// Re-check the originating access link on every request, including writes from an already open form.
public sealed class CustomerSessionValidator(
    ICustomerAccessTokenRepository accessTokens,
    ICustomerRepository customers,
    IDateTimeProvider clock)
{
    public async Task<bool> IsValidAsync(Guid customerId, Guid tokenId, CancellationToken cancellationToken)
    {
        var token = await accessTokens.GetByIdAsync(tokenId, cancellationToken);
        if (token is null || token.CustomerId != customerId || !token.IsActive ||
            (token.ExpiresAt is not null && token.ExpiresAt <= clock.Now))
            return false;

        var customer = await customers.GetByIdAsync(customerId, cancellationToken);
        return customer is { IsActive: true };
    }
}
