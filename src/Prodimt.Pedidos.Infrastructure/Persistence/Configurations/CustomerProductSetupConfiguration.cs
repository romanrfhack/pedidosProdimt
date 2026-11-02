using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Infrastructure.Persistence.Configurations;

public sealed class CustomerProductSetupConfiguration : IEntityTypeConfiguration<CustomerProductSetupRequest>
{
    public void Configure(EntityTypeBuilder<CustomerProductSetupRequest> builder)
    {
        builder.ToTable("CustomerProductSetupRequests", table =>
            table.HasCheckConstraint("CK_ProductSetup_Version", "[Version] > 0"));
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.CustomerId, x.ProductId }).IsUnique();
        builder.HasIndex(x => new { x.IsResolved, x.LastRequestedAt });
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.Property(x => x.ProductNameSnapshot).HasMaxLength(200);
        builder.Property(x => x.UnitCode).HasMaxLength(16);
        builder.Property(x => x.ResolvedBy).HasMaxLength(200);
        builder.Property(x => x.ResolvedByName).HasMaxLength(200);
        builder.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Orders).WithOne().HasForeignKey(x => x.SetupRequestId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CustomerProductSetupOrderConfiguration : IEntityTypeConfiguration<CustomerProductSetupOrder>
{
    public void Configure(EntityTypeBuilder<CustomerProductSetupOrder> builder)
    {
        builder.ToTable("CustomerProductSetupOrders");
        builder.HasKey(x => new { x.SetupRequestId, x.OrderId });
        builder.Property(x => x.Source).HasMaxLength(16);
        builder.Property(x => x.ActorId).HasMaxLength(200);
        builder.Property(x => x.ActorDisplayName).HasMaxLength(200);
        builder.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
    }
}
