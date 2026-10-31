using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Infrastructure.Persistence.Configurations;

public sealed class OrderOperationConfiguration : IEntityTypeConfiguration<OrderOperation>
{
    public void Configure(EntityTypeBuilder<OrderOperation> builder)
    {
        builder.HasKey(x => x.OperationKey);
        builder.Property(x => x.OperationKey).HasMaxLength(240);
        builder.Property(x => x.PayloadHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ResultJson).IsRequired();
    }
}
