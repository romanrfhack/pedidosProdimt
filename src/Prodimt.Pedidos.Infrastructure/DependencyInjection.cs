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
        var persistenceProvider = configuration["Persistence:Provider"] ?? "SqlServer";

        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();

        if (string.Equals(persistenceProvider, "InMemory", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<InMemoryDataStore>();
            services.AddSingleton<ICustomerRepository, InMemoryCustomerRepository>();
            services.AddSingleton<IProductRepository, InMemoryProductRepository>();
            services.AddSingleton<ISalesChannelRepository, InMemorySalesChannelRepository>();
            services.AddSingleton<IOrderRepository, InMemoryOrderRepository>();
            services.AddSingleton<IOrderAuditLogRepository, InMemoryOrderAuditLogRepository>();

            return services;
        }

        var connectionString = configuration.GetConnectionString("Pedidos")
            ?? configuration["PRODIMT_PEDIDOS_CONNECTION_STRING"]
            ?? "Server=localhost,1433;Database=ProdimtPedidos;User Id=sa;Password=CHANGE_ME_LOCAL_ONLY;TrustServerCertificate=True";

        services.AddDbContext<PedidosDbContext>(options => options.UseSqlServer(connectionString));
        services.AddScoped<ICustomerRepository, EfCustomerRepository>();
        services.AddScoped<IProductRepository, EfProductRepository>();
        services.AddScoped<ISalesChannelRepository, EfSalesChannelRepository>();
        services.AddScoped<IOrderRepository, EfOrderRepository>();
        services.AddScoped<IOrderAuditLogRepository, EfOrderAuditLogRepository>();

        return services;
    }
}
