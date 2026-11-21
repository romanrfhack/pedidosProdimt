using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Infrastructure.Persistence.Configurations;

public sealed class MachineOperatorConfiguration : IEntityTypeConfiguration<MachineOperator>
{
    public void Configure(EntityTypeBuilder<MachineOperator> builder)
    {
        builder.ToTable("MachineOperators");
        builder.HasKey(x => new { x.MachineId, x.UserId });
        builder.HasOne<Machine>().WithMany().HasForeignKey(x => x.MachineId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AdminUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ProductionLotHandoffConfiguration : IEntityTypeConfiguration<ProductionLotHandoff>
{
    public void Configure(EntityTypeBuilder<ProductionLotHandoff> builder)
    {
        builder.ToTable("ProductionLotHandoffs", table =>
            table.HasCheckConstraint("CK_ProductionLotHandoff_Quantity", "[Quantity] > 0"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Quantity).HasPrecision(18, 2);
        builder.Property(x => x.CreatedBy).HasMaxLength(120);
        builder.HasOne<ProductionBatch>().WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class MachineProductionNoticeConfiguration : IEntityTypeConfiguration<MachineProductionNotice>
{
    public void Configure(EntityTypeBuilder<MachineProductionNotice> builder)
    {
        builder.ToTable("MachineProductionNotices", table =>
            table.HasCheckConstraint("CK_MachineProductionNotice_Quantity", "[Quantity] > 0"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.CustomerName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ProductName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Quantity).HasPrecision(18, 2);
        builder.HasIndex(x => new { x.MachineId, x.ServiceDate });
        builder.HasOne<Machine>().WithMany().HasForeignKey(x => x.MachineId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
    }
}
