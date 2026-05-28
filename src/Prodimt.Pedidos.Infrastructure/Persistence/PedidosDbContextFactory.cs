using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Prodimt.Pedidos.Infrastructure.Persistence;

public sealed class PedidosDbContextFactory : IDesignTimeDbContextFactory<PedidosDbContext>
{
    public PedidosDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<PedidosDbContext>();
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Pedidos")
            ?? Environment.GetEnvironmentVariable("PRODIMT_PEDIDOS_CONNECTION_STRING")
            ?? "Server=localhost,1433;Database=ProdimtPedidos;User Id=sa;Password=CHANGE_ME_LOCAL_ONLY;TrustServerCertificate=True";

        optionsBuilder.UseSqlServer(connectionString);

        return new PedidosDbContext(optionsBuilder.Options);
    }
}
