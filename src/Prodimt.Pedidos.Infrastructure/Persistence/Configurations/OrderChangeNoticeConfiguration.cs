using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Infrastructure.Persistence.Configurations;

public sealed class OrderChangeNoticeConfiguration : IEntityTypeConfiguration<OrderChangeNotice>
{
    public void Configure(EntityTypeBuilder<OrderChangeNotice> builder)
    {
        builder.ToTable("OrderChangeNotices");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.Property(x => x.CustomerName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.RouteCode).HasMaxLength(80);
        builder.Property(x => x.MachineName).HasMaxLength(120);
        builder.Property(x => x.Kind).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.ActorId).HasMaxLength(200);
        builder.Property(x => x.ActorDisplayName).HasMaxLength(200);
        builder.Property(x => x.AcknowledgedById).HasMaxLength(200);
        builder.Property(x => x.AcknowledgedBy).HasMaxLength(200);
        builder.Property(x => x.LinesJson).IsRequired();
        builder.HasIndex(x => new { x.OrderId, x.OrderVersion, x.MachineId }).IsUnique().HasFilter(null);
        builder.HasIndex(x => new { x.ServiceDate, x.MachineId, x.AcknowledgedAt });
        builder.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
    }
}
