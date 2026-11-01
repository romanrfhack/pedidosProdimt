using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Infrastructure.Persistence.Configurations;

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasOne<Product>().WithMany().HasForeignKey(x => x.MergedIntoProductId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable("Products", table => table.HasCheckConstraint("CK_Products_Consolidated",
            "[MergedIntoProductId] IS NULL OR ([MergedIntoProductId] <> [Id] AND [IsActive] = 0)"));

        builder.Property(x => x.Name)
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(x => x.ExternalCode)
            .HasMaxLength(80);

        builder.Property(x => x.Description)
            .HasMaxLength(500);

        builder.Property(x => x.UnitCode).HasMaxLength(16);

        builder.HasMany<OrderLine>()
            .WithOne()
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany<CustomerFrequentProduct>()
            .WithOne()
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.ExternalCode)
            .IsUnique()
            .HasFilter("[ExternalCode] IS NOT NULL");
    }
}
