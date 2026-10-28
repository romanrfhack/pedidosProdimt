using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Infrastructure.Persistence.Configurations;

public sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.ExternalCode)
            .HasMaxLength(80);

        builder.Property(x => x.PhoneNumber)
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(x => x.DeliveryNotes)
            .HasMaxLength(500);

        builder.Property(x => x.RouteCode).HasMaxLength(80);
        builder.Property(x => x.PaymentTermCode).HasMaxLength(32);
        builder.Property(x => x.InternalDeliveryInstructions).HasMaxLength(1000);
        builder.Property(x => x.OrderingProfileVersion).IsConcurrencyToken();
        builder.Property(x => x.RestDaysMask).HasDefaultValue(0);
        builder.ToTable(table => table.HasCheckConstraint("CK_Customers_RestDaysMask", "[RestDaysMask] BETWEEN 0 AND 127"));
        builder.HasIndex(x => new { x.RouteCode, x.RouteStopOrder });

        builder.HasMany<Order>()
            .WithOne()
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany<CustomerFrequentProduct>()
            .WithOne()
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany<CustomerMachineAssignment>()
            .WithOne()
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.ExternalCode)
            .IsUnique()
            .HasFilter("[ExternalCode] IS NOT NULL");
    }
}
