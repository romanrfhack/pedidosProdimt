using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Infrastructure.Persistence.Configurations;

public sealed class CustomerAccessTokenConfiguration : IEntityTypeConfiguration<CustomerAccessToken>
{
    public void Configure(EntityTypeBuilder<CustomerAccessToken> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TokenHash)
            .HasMaxLength(128)
            .IsRequired();

        builder.HasIndex(x => x.TokenHash)
            .IsUnique();

        builder.Property(x => x.DisplayName)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.ProtectedToken)
            .HasMaxLength(1024);

        builder.Property(x => x.MetadataVersion)
            .HasDefaultValue(1)
            .IsConcurrencyToken();

        builder.Property(x => x.IsReminderLink)
            .HasDefaultValue(false);

        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
