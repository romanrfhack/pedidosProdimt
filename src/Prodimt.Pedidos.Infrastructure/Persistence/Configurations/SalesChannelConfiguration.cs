using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Infrastructure.Persistence.Configurations;

public sealed class SalesChannelConfiguration : IEntityTypeConfiguration<SalesChannel>
{
    public void Configure(EntityTypeBuilder<SalesChannel> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name)
            .HasMaxLength(80)
            .IsRequired();

        builder.Property(x => x.Type)
            .HasConversion<string>()
            .HasMaxLength(40);

        builder.HasIndex(x => x.Type)
            .IsUnique();

        builder.HasMany<Order>()
            .WithOne()
            .HasForeignKey(x => x.SalesChannelId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
