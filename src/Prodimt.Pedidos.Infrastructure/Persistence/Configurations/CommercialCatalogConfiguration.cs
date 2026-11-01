using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Infrastructure.Persistence.Configurations;

public sealed class PriceBookConfiguration : IEntityTypeConfiguration<CustomerProductPriceBook>
{
    public void Configure(EntityTypeBuilder<CustomerProductPriceBook> builder)
    {
        builder.ToTable("CustomerProductPriceBooks");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.CustomerId, x.ProductId }).IsUnique();
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Revisions).WithOne().HasForeignKey(x => x.PriceBookId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PriceRevisionConfiguration : IEntityTypeConfiguration<CustomerProductPriceRevision>
{
    public void Configure(EntityTypeBuilder<CustomerProductPriceRevision> builder)
    {
        builder.ToTable("CustomerProductPriceRevisions", table =>
            table.HasCheckConstraint("CK_PriceRevision_Amount", "[UnitPrice] IS NULL OR ([UnitPrice] >= 0 AND [UnitPrice] <= 999999999.9999)"));
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.OperationId).IsUnique();
        builder.HasIndex(x => new { x.PriceBookId, x.Version }).IsUnique();
        builder.HasIndex(x => new { x.PriceBookId, x.EffectiveFrom, x.Version }).IsDescending(false, true, true);
        builder.Property(x => x.UnitPrice).HasPrecision(18, 4);
        builder.Property(x => x.UnitCode).HasMaxLength(16);
        builder.Property(x => x.CurrencyCode).HasMaxLength(3);
        builder.Property(x => x.ProductNameSnapshot).HasMaxLength(200);
        builder.Property(x => x.ActorId).HasMaxLength(200);
        builder.Property(x => x.ActorDisplayName).HasMaxLength(200);
        builder.Property(x => x.Reason).HasMaxLength(500);
        builder.Property(x => x.PayloadHash).HasMaxLength(64).IsFixedLength();
    }
}

public sealed class ProductConsolidationConfiguration : IEntityTypeConfiguration<ProductConsolidationOperation>
{
    public void Configure(EntityTypeBuilder<ProductConsolidationOperation> builder)
    {
        builder.ToTable("ProductConsolidationOperations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.PayloadHash).HasMaxLength(64).IsFixedLength();
        builder.Property(x => x.ResultJson).IsRequired();
    }
}
