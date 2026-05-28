using Microsoft.EntityFrameworkCore;
using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Infrastructure.Persistence;

public sealed class PedidosDbContext(DbContextOptions<PedidosDbContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<CustomerFrequentProduct> CustomerFrequentProducts => Set<CustomerFrequentProduct>();

    public DbSet<Machine> Machines => Set<Machine>();

    public DbSet<CustomerMachineAssignment> CustomerMachineAssignments => Set<CustomerMachineAssignment>();

    public DbSet<SalesChannel> SalesChannels => Set<SalesChannel>();

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<OrderLine> OrderLines => Set<OrderLine>();

    public DbSet<OrderAuditLog> OrderAuditLogs => Set<OrderAuditLog>();

    public DbSet<AdminUser> AdminUsers => Set<AdminUser>();

    public DbSet<CustomerAccessToken> CustomerAccessTokens => Set<CustomerAccessToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PedidosDbContext).Assembly);
    }
}
