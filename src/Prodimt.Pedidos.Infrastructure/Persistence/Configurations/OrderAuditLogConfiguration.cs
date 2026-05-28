using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Infrastructure.Persistence.Configurations;

public sealed class OrderAuditLogConfiguration : IEntityTypeConfiguration<OrderAuditLog>
{
    public void Configure(EntityTypeBuilder<OrderAuditLog> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.EventType)
            .HasConversion<string>()
            .HasMaxLength(80);

        builder.Property(x => x.ActorType)
            .HasConversion<string>()
            .HasMaxLength(40);

        builder.Property(x => x.ActorId)
            .HasMaxLength(100);

        builder.Property(x => x.ActorDisplayName)
            .HasMaxLength(200);

        builder.Property(x => x.OrderStatus)
            .HasConversion<string>()
            .HasMaxLength(40);

        builder.Property(x => x.AdminReviewReason)
            .HasConversion<string>()
            .HasMaxLength(60);

        builder.Property(x => x.AdminDecision)
            .HasConversion<string>()
            .HasMaxLength(60);

        builder.Property(x => x.Summary)
            .HasMaxLength(500);

        builder.Property(x => x.MetadataJson)
            .HasMaxLength(2000);

        builder.HasIndex(x => new { x.OrderId, x.OccurredAt });

        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
