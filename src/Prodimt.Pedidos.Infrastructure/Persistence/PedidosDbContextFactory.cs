using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Prodimt.Pedidos.Infrastructure.Persistence;

public sealed class PedidosDbContextFactory : IDesignTimeDbContextFactory<PedidosDbContext>
{
    public PedidosDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<PedidosDbContext>();
        optionsBuilder.UseSqlServer(
            "Server=localhost,1433;Database=ProdimtPedidos;User Id=sa;Password=CHANGE_ME_LOCAL_ONLY;TrustServerCertificate=True");

        return new PedidosDbContext(optionsBuilder.Options);
    }
}
