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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>(builder =>
        {
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
            builder.Property(x => x.PhoneNumber).HasMaxLength(40).IsRequired();
            builder.Property(x => x.DeliveryNotes).HasMaxLength(500);
        });

        modelBuilder.Entity<Product>(builder =>
        {
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Name).HasMaxLength(120).IsRequired();
            builder.Property(x => x.Description).HasMaxLength(500);
        });

        modelBuilder.Entity<CustomerFrequentProduct>(builder =>
        {
            builder.HasKey(x => new { x.CustomerId, x.ProductId });
            builder.Property(x => x.DefaultQuantity).HasPrecision(18, 2);
        });

        modelBuilder.Entity<Machine>(builder =>
        {
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Name).HasMaxLength(120);
        });

        modelBuilder.Entity<CustomerMachineAssignment>(builder =>
        {
            builder.HasKey(x => new { x.CustomerId, x.MachineId });
            builder.Property(x => x.Notes).HasMaxLength(500);
        });

        modelBuilder.Entity<SalesChannel>(builder =>
        {
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Name).HasMaxLength(80).IsRequired();
            builder.Property(x => x.Type).HasConversion<string>().HasMaxLength(40);
        });

        modelBuilder.Entity<Order>(builder =>
        {
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(40);
            builder.Property(x => x.AdminReviewReason).HasConversion<string>().HasMaxLength(60);
            builder.Property(x => x.AdminDecision).HasConversion<string>().HasMaxLength(60);
            builder.Property(x => x.DeliveryNotes).HasMaxLength(500);
            builder.Property(x => x.InternalNotes).HasMaxLength(1000);
            builder.HasMany<OrderLine>("_lines").WithOne().HasForeignKey(x => x.OrderId);
            builder.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<OrderLine>(builder =>
        {
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Quantity).HasPrecision(18, 2);
            builder.Property(x => x.Notes).HasMaxLength(500);
        });
    }
}
