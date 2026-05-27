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
    }
}
