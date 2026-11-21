using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Infrastructure.Persistence.Configurations;

public sealed class ProductionBatchConfiguration : IEntityTypeConfiguration<ProductionBatch>
{
    public void Configure(EntityTypeBuilder<ProductionBatch> builder)
    {
        builder.ToTable("ProductionBatches", table =>
        {
            table.HasCheckConstraint("CK_ProductionBatch_Quantity", "[Quantity] > 0");
            table.HasCheckConstraint("CK_ProductionBatch_Unit", "[UnitCode] = 'kg'");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ProductNameSnapshot).HasMaxLength(200).IsRequired();
        builder.Property(x => x.UnitCode).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Quantity).HasPrecision(18, 2);
        builder.Property(x => x.CreatedBy).HasMaxLength(120);
        builder.Property(x => x.Notes).HasMaxLength(500);
        builder.HasIndex(x => new { x.ServiceDate, x.MachineId, x.ProductId });
        builder.HasOne<Machine>().WithMany().HasForeignKey(x => x.MachineId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ProductionAllocationConfiguration : IEntityTypeConfiguration<ProductionAllocation>
{
    public void Configure(EntityTypeBuilder<ProductionAllocation> builder)
    {
        builder.ToTable("ProductionAllocations", table =>
        {
            table.HasCheckConstraint("CK_ProductionAllocation_Quantity", "[Quantity] > 0");
            table.HasCheckConstraint("CK_ProductionAllocation_Release", "CAST([ReleasedQuantity] AS decimal(18,2)) >= 0 AND CAST([ReleasedQuantity] AS decimal(18,2)) <= CAST([Quantity] AS decimal(18,2)) AND ([ReversedAt] IS NULL OR CAST([ReleasedQuantity] AS decimal(18,2)) = 0)");
            table.HasCheckConstraint("CK_ProductionAllocation_Reversal",
                "([ReversedAt] IS NULL AND [ReversedBy] IS NULL AND [ReverseReason] IS NULL) OR ([ReversedAt] IS NOT NULL AND [ReverseReason] IS NOT NULL)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.CustomerNameSnapshot).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Quantity).HasPrecision(18, 2);
        builder.Property(x => x.CreatedBy).HasMaxLength(120);
        builder.Property(x => x.ReversedBy).HasMaxLength(120);
        builder.Property(x => x.ReverseReason).HasMaxLength(500);
        builder.Property(x => x.ReleasedQuantity).HasPrecision(18, 2);
        builder.HasIndex(x => new { x.BatchId, x.ReversedAt });
        builder.HasIndex(x => new { x.OrderId, x.OrderLineId, x.ReversedAt });
        builder.HasOne<ProductionBatch>().WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        // OrderLineId is a historical snapshot, deliberately not a cascading FK.
        // Active links are protected centrally before any order-line edit/delete.
    }
}
