using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Infrastructure.Persistence.Configurations;

public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(40);

        builder.Property(x => x.AdminReviewReason)
            .HasConversion<string>()
            .HasMaxLength(60);

        builder.Property(x => x.AdminDecision)
            .HasConversion<string>()
            .HasMaxLength(60);

        builder.Property(x => x.DeliveryNotes)
            .HasMaxLength(500);

        builder.Property(x => x.InternalNotes)
            .HasMaxLength(1000);

        builder.HasIndex(x => new { x.CustomerId, x.OrderDate, x.SequenceNumber });

        builder.HasMany(x => x.Lines)
            .WithOne()
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(x => x.Lines)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
