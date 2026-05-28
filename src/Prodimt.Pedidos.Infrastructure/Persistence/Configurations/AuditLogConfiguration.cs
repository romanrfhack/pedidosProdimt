using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Infrastructure.Persistence.Configurations;

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.EntityType)
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(x => x.EntityId)
            .HasMaxLength(80)
            .IsRequired();

        builder.Property(x => x.EventType)
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(x => x.ActorId)
            .HasMaxLength(120);

        builder.Property(x => x.ActorDisplayName)
            .HasMaxLength(200);

        builder.Property(x => x.Summary)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(x => x.MetadataJson)
            .HasMaxLength(2000);

        builder.HasIndex(x => new { x.EntityType, x.EntityId, x.OccurredAt });
        builder.HasIndex(x => x.EventType);
    }
}
