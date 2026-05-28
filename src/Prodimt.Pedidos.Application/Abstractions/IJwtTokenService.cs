using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Application.Abstractions;

public interface IJwtTokenService
{
    JwtTokenResult CreateCustomerToken(Customer customer);

    JwtTokenResult CreateAdminToken(AdminUser adminUser);
}
