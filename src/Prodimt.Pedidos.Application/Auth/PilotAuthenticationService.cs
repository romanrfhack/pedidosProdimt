using Prodimt.Pedidos.Application.Abstractions;

namespace Prodimt.Pedidos.Application.Auth;

public sealed class PilotAuthenticationService(
    IAdminUserRepository adminUsers,
    ICustomerAccessTokenRepository customerAccessTokens,
    ICustomerAccessTokenHasher customerAccessTokenHasher,
    ICustomerRepository customers,
    IPasswordHashService passwordHashService,
    IJwtTokenService jwtTokenService,
    IDateTimeProvider dateTimeProvider)
{
    public async Task<CustomerTokenLoginResponse> LoginCustomerWithTokenAsync(
        CustomerTokenLoginRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Token))
        {
            throw new AuthenticationFailedException("Token de cliente invalido.");
        }

        var tokenHash = customerAccessTokenHasher.HashToken(request.Token);
        var accessToken = await customerAccessTokens.GetByTokenHashAsync(tokenHash, cancellationToken);

        if (accessToken is null ||
            !accessToken.IsActive ||
            (accessToken.ExpiresAt is not null && accessToken.ExpiresAt <= dateTimeProvider.Now))
        {
            throw new AuthenticationFailedException("Token de cliente invalido.");
        }

        var customer = await customers.GetByIdAsync(accessToken.CustomerId, cancellationToken);

        if (customer is null || !customer.IsActive)
        {
            throw new AuthenticationFailedException("Token de cliente invalido.");
        }

        accessToken.LastUsedAt = dateTimeProvider.Now;
        await customerAccessTokens.SaveChangesAsync(cancellationToken);

        var jwt = jwtTokenService.CreateCustomerToken(customer);

        return new CustomerTokenLoginResponse(
            jwt.AccessToken,
            "Bearer",
            jwt.ExpiresAt,
            customer.Id,
            customer.Name);
    }

    public async Task<AdminLoginResponse> LoginAdminAsync(
        AdminLoginRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.UserName) || string.IsNullOrWhiteSpace(request.Password))
        {
            throw new AuthenticationFailedException("Credenciales administrativas invalidas.");
        }

        var adminUser = await adminUsers.GetByUserNameAsync(request.UserName, cancellationToken);

        if (adminUser is null ||
            !adminUser.IsActive ||
            !passwordHashService.VerifyPassword(adminUser.PasswordHash, request.Password))
        {
            throw new AuthenticationFailedException("Credenciales administrativas invalidas.");
        }

        var jwt = jwtTokenService.CreateAdminToken(adminUser);

        return new AdminLoginResponse(
            jwt.AccessToken,
            "Bearer",
            jwt.ExpiresAt,
            adminUser.DisplayName);
    }
}
