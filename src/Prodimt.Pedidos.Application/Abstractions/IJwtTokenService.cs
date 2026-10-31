using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Application.Abstractions;

public interface IJwtTokenService
{
    JwtTokenResult CreateCustomerToken(Customer customer, CustomerAccessToken? accessToken = null);

    JwtTokenResult CreateAdminToken(AdminUser adminUser);
}
