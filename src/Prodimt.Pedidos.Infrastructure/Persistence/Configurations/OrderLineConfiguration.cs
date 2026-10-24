using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Infrastructure.Persistence.Configurations;

public sealed class OrderLineConfiguration : IEntityTypeConfiguration<OrderLine>
{
    public void Configure(EntityTypeBuilder<OrderLine> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Quantity)
            .HasPrecision(18, 2);

        builder.Property(x => x.ProductNameSnapshot).HasMaxLength(200);
        builder.Property(x => x.UnitCode).HasMaxLength(16);
        builder.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Machine>().WithMany().HasForeignKey(x => x.AssignedMachineId).OnDelete(DeleteBehavior.Restrict);

        builder.Property(x => x.Notes)
            .HasMaxLength(500);
    }
}
