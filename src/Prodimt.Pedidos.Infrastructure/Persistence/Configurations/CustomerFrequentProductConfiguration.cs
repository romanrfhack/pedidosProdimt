using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Infrastructure.Persistence.Configurations;

public sealed class CustomerFrequentProductConfiguration : IEntityTypeConfiguration<CustomerFrequentProduct>
{
    public void Configure(EntityTypeBuilder<CustomerFrequentProduct> builder)
    {
        builder.HasKey(x => new { x.CustomerId, x.ProductId });

        builder.Property(x => x.DefaultQuantity)
            .HasPrecision(18, 2);

        builder.Property(x => x.CaptureAlias).HasMaxLength(16);
        builder.HasIndex(x => new { x.CustomerId, x.CaptureAlias })
            .IsUnique().HasFilter("[CaptureAlias] IS NOT NULL");

        builder.HasOne<Machine>().WithMany()
            .HasForeignKey(x => x.PreferredMachineId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.CustomerId, x.InterpretationPosition })
            .IsUnique()
            .HasFilter("[InterpretationPosition] IS NOT NULL");
    }
}
