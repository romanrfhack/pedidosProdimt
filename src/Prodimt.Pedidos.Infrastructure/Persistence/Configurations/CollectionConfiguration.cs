using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Infrastructure.Persistence.Configurations;

public sealed class CollectionChargeConfiguration : IEntityTypeConfiguration<CollectionCharge>
{
    public void Configure(EntityTypeBuilder<CollectionCharge> builder)
    {
        builder.ToTable("CollectionCharges", table =>
        {
            table.HasCheckConstraint("CK_CollectionCharge_Quantity", "[Quantity] > 0 AND [RequestedQuantity] >= 0");
            table.HasCheckConstraint("CK_CollectionCharge_Amount", "([Amount] IS NULL OR [Amount] >= 0) AND ([UnitPrice] IS NULL OR [UnitPrice] >= 0)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.SourceKey).HasMaxLength(160).IsRequired();
        builder.HasIndex(x => x.SourceKey).IsUnique();
        builder.HasIndex(x => new { x.CustomerId, x.ServiceDate });
        builder.HasIndex(x => new { x.OrderLineId, x.Sequence }).IsUnique();
        builder.Property(x => x.CustomerName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ProductName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.UnitCode).HasMaxLength(16).IsRequired();
        builder.Property(x => x.RouteCode).HasMaxLength(80).IsRequired();
        builder.Property(x => x.DriverName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.PaymentTermCode).HasMaxLength(40);
        builder.Property(x => x.ActorId).HasMaxLength(120);
        builder.Property(x => x.ActorName).HasMaxLength(200);
        builder.Property(x => x.Quantity).HasPrecision(18, 3);
        builder.Property(x => x.RequestedQuantity).HasPrecision(18, 3);
        builder.Property(x => x.UnitPrice).HasPrecision(18, 4);
        builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DeliveryAssignment>().WithMany().HasForeignKey(x => x.AssignmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DeliveryTrip>().WithMany().HasForeignKey(x => x.TripId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DeliveryEvent>().WithMany().HasForeignKey(x => x.DeliveryEventId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CollectionChargeValuationConfiguration : IEntityTypeConfiguration<CollectionChargeValuation>
{
    public void Configure(EntityTypeBuilder<CollectionChargeValuation> builder)
    {
        builder.ToTable("CollectionChargeValuations", table => table.HasCheckConstraint("CK_CollectionValuation_Amount", "[Amount] >= 0 AND [UnitPrice] >= 0"));
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.ChargeId).IsUnique();
        builder.Property(x => x.UnitPrice).HasPrecision(18, 4);
        builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.Property(x => x.ActorId).HasMaxLength(120);
        builder.Property(x => x.ActorName).HasMaxLength(200);
        builder.HasOne<CollectionCharge>().WithMany().HasForeignKey(x => x.ChargeId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CollectionPaymentConfiguration : IEntityTypeConfiguration<CollectionPayment>
{
    public void Configure(EntityTypeBuilder<CollectionPayment> builder)
    {
        builder.ToTable("CollectionPayments", table => table.HasCheckConstraint("CK_CollectionPayment_Amount",
            "[Amount] > 0 AND ([CreditAmount] IS NULL OR ([CreditAmount] >= 0 AND [CreditAmount] <= [Amount]))"));
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.CustomerId, x.ServiceDate });
        builder.HasIndex(x => x.TripId);
        builder.Property(x => x.CustomerName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.DriverName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.RouteCode).HasMaxLength(80).IsRequired();
        builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.Property(x => x.CreditAmount).HasPrecision(18, 2);
        builder.Property(x => x.AllocationPolicy).HasMaxLength(80).IsRequired();
        builder.Property(x => x.AllocationsJson).IsRequired();
        builder.Property(x => x.ActorId).HasMaxLength(120);
        builder.Property(x => x.ActorName).HasMaxLength(200);
        builder.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DeliveryTrip>().WithMany().HasForeignKey(x => x.TripId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DeliveryAssignment>().WithMany().HasForeignKey(x => x.AssignmentId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CollectionCashReceiptConfiguration : IEntityTypeConfiguration<CollectionCashReceipt>
{
    public void Configure(EntityTypeBuilder<CollectionCashReceipt> builder)
    {
        builder.ToTable("CollectionCashReceipts", table => table.HasCheckConstraint("CK_CollectionCashReceipt_Amount", "[ExpectedAmount] > 0 AND [ReceivedAmount] >= 0"));
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.TripId);
        builder.HasIndex(x => new { x.ReceiverUserId, x.CreatedAt });
        builder.Property(x => x.ExpectedAmount).HasPrecision(18, 2);
        builder.Property(x => x.ReceivedAmount).HasPrecision(18, 2);
        builder.Property(x => x.Reason).HasMaxLength(500);
        builder.Property(x => x.ReceiverName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.DriverName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.RouteCode).HasMaxLength(80).IsRequired();
        builder.HasOne<DeliveryTrip>().WithMany().HasForeignKey(x => x.TripId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AdminUser>().WithMany().HasForeignKey(x => x.ReceiverUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CollectionCashReceiptLineConfiguration : IEntityTypeConfiguration<CollectionCashReceiptLine>
{
    public void Configure(EntityTypeBuilder<CollectionCashReceiptLine> builder)
    {
        builder.ToTable("CollectionCashReceiptLines", table => table.HasCheckConstraint("CK_CollectionCashReceiptLine_Amount", "[Amount] > 0"));
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.ReceiptId, x.PaymentId }).IsUnique();
        builder.HasIndex(x => x.PaymentId);
        builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.HasOne<CollectionCashReceipt>().WithMany().HasForeignKey(x => x.ReceiptId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CollectionPayment>().WithMany().HasForeignKey(x => x.PaymentId).OnDelete(DeleteBehavior.Restrict);
    }
}
