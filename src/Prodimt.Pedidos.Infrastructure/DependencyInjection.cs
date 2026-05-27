using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Infrastructure.Persistence;
using Prodimt.Pedidos.Infrastructure.Repositories;

namespace Prodimt.Pedidos.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Pedidos")
            ?? "Server=localhost,1433;Database=ProdimtPedidos;User Id=sa;Password=CHANGE_ME_LOCAL_ONLY;TrustServerCertificate=True";

        services.AddDbContext<PedidosDbContext>(options => options.UseSqlServer(connectionString));

        services.AddSingleton<InMemoryDataStore>();
        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
        services.AddSingleton<ICustomerRepository, InMemoryCustomerRepository>();
        services.AddSingleton<IProductRepository, InMemoryProductRepository>();
        services.AddSingleton<ISalesChannelRepository, InMemorySalesChannelRepository>();
        services.AddSingleton<IOrderRepository, InMemoryOrderRepository>();

        return services;
    }
}
