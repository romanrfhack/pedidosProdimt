using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prodimt.Pedidos.Domain.Entities;
namespace Prodimt.Pedidos.Infrastructure.Persistence.Configurations;

public sealed class DeliveryRouteConfiguration : IEntityTypeConfiguration<DeliveryRoute>
{
    public void Configure(EntityTypeBuilder<DeliveryRoute> b)
    {
        b.ToTable("DeliveryRoutes"); b.HasKey(x => x.Code);
        b.Property(x => x.Code).HasMaxLength(80); b.Property(x => x.Name).HasMaxLength(160).IsRequired();
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasOne<AdminUser>().WithMany().HasForeignKey(x => x.DefaultDriverUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class DeliveryRouteDayDriverConfiguration : IEntityTypeConfiguration<DeliveryRouteDayDriver>
{
    public void Configure(EntityTypeBuilder<DeliveryRouteDayDriver> b)
    {
        b.ToTable("DeliveryRouteDayDrivers");
        b.HasKey(x => new { x.Date, x.RouteCode });
        b.Property(x => x.RouteCode).HasMaxLength(80).IsRequired();
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasOne<DeliveryRoute>().WithMany().HasForeignKey(x => x.RouteCode).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AdminUser>().WithMany().HasForeignKey(x => x.DriverUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class CustomerDeliveryLocationConfiguration : IEntityTypeConfiguration<CustomerDeliveryLocation>
{
    public void Configure(EntityTypeBuilder<CustomerDeliveryLocation> b)
    {
        b.ToTable("CustomerDeliveryLocations"); b.HasKey(x => x.CustomerId);
        b.Property(x => x.MapsUrl).HasMaxLength(2048); b.Property(x => x.Address).HasMaxLength(500);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class DeliveryTripConfiguration : IEntityTypeConfiguration<DeliveryTrip>
{
    public void Configure(EntityTypeBuilder<DeliveryTrip> b)
    {
        b.ToTable("DeliveryTrips", t => { t.HasCheckConstraint("CK_DeliveryTrips_Number", "[TripNumber] >= 1 AND [TripNumber] <= 999");
            t.HasCheckConstraint("CK_DeliveryTrips_Status", "[Status] IN ('Open', 'Closed')"); });
        b.HasKey(x => x.Id); b.Property(x => x.RouteCode).HasMaxLength(80).IsRequired();
        b.Property(x => x.RouteName).HasMaxLength(160).IsRequired(); b.Property(x => x.DriverName).HasMaxLength(160).IsRequired();
        b.Property(x => x.Status).HasMaxLength(16).IsRequired(); b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => new { x.Date, x.RouteCode, x.TripNumber }).IsUnique();
        b.HasIndex(x => new { x.DriverUserId, x.Date });
        b.HasOne<DeliveryRoute>().WithMany().HasForeignKey(x => x.RouteCode).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AdminUser>().WithMany().HasForeignKey(x => x.DriverUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class DeliveryAssignmentConfiguration : IEntityTypeConfiguration<DeliveryAssignment>
{
    public void Configure(EntityTypeBuilder<DeliveryAssignment> b)
    {
        b.ToTable("DeliveryAssignments", t =>
        {
            t.HasCheckConstraint("CK_DeliveryAssignments_Quantities", "[AssignedQuantity] > 0 AND [ReleasedQuantity] >= 0 AND [ReleasedQuantity] <= [AssignedQuantity] AND ([CountedQuantity] IS NULL OR [CountedQuantity] >= 0) AND ([LoadedQuantity] IS NULL OR [LoadedQuantity] >= 0) AND [DeliveredQuantity] >= 0 AND [ReturnedQuantity] >= 0 AND [TransferredOutQuantity] >= 0 AND [TransferredInQuantity] >= 0");
            t.HasCheckConstraint("CK_DeliveryAssignments_Custody", "COALESCE([LoadedQuantity], 0) + [TransferredInQuantity] - [DeliveredQuantity] - [ReturnedQuantity] - [TransferredOutQuantity] >= 0");
            t.HasCheckConstraint("CK_DeliveryAssignments_Commitment", "[AssignedQuantity] - [ReleasedQuantity] >= COALESCE([LoadedQuantity], 0) + [TransferredInQuantity] - [ReturnedQuantity] - [TransferredOutQuantity]");
        });
        b.HasKey(x => x.Id); b.Property(x => x.CustomerName).HasMaxLength(200).IsRequired();
        b.Property(x => x.ProductName).HasMaxLength(160).IsRequired(); b.Property(x => x.UnitCode).HasMaxLength(20).IsRequired();
        b.Property(x => x.MapsUrl).HasMaxLength(2048); b.Property(x => x.Address).HasMaxLength(500);
        b.Property(x => x.PaymentTermCode).HasMaxLength(40); b.Property(x => x.DeliveryNotes).HasMaxLength(2000);
        b.Property(x => x.InternalDeliveryInstructions).HasMaxLength(2000); b.Property(x => x.Version).IsConcurrencyToken();
        b.Property(x => x.RequestedQuantity).HasColumnType("decimal(18,3)").HasPrecision(18, 3); b.Property(x => x.AssignedQuantity).HasColumnType("decimal(18,3)").HasPrecision(18, 3);
        b.Property(x => x.ReleasedQuantity).HasColumnType("decimal(18,3)").HasPrecision(18, 3); b.Property(x => x.CountedQuantity).HasColumnType("decimal(18,3)").HasPrecision(18, 3);
        b.Property(x => x.LoadedQuantity).HasColumnType("decimal(18,3)").HasPrecision(18, 3); b.Property(x => x.DeliveredQuantity).HasColumnType("decimal(18,3)").HasPrecision(18, 3);
        b.Property(x => x.ReturnedQuantity).HasColumnType("decimal(18,3)").HasPrecision(18, 3); b.Property(x => x.TransferredOutQuantity).HasColumnType("decimal(18,3)").HasPrecision(18, 3);
        b.Property(x => x.TransferredInQuantity).HasColumnType("decimal(18,3)").HasPrecision(18, 3);
        b.Ignore(x => x.CustodyQuantity); b.Ignore(x => x.CommitmentQuantity);
        b.HasIndex(x => new { x.OrderId, x.OrderLineId }); b.HasIndex(x => x.TripId);
        b.HasOne<DeliveryTrip>().WithMany().HasForeignKey(x => x.TripId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class DeliveryEventConfiguration : IEntityTypeConfiguration<DeliveryEvent>
{
    public void Configure(EntityTypeBuilder<DeliveryEvent> b)
    {
        b.ToTable("DeliveryEvents"); b.HasKey(x => x.Id); b.Property(x => x.Type).HasMaxLength(80).IsRequired();
        b.Property(x => x.Quantity).HasColumnType("decimal(18,3)").HasPrecision(18, 3); b.Property(x => x.PreviousQuantity).HasColumnType("decimal(18,3)").HasPrecision(18, 3);
        b.Property(x => x.Reason).HasMaxLength(500); b.Property(x => x.Notes).HasMaxLength(2000);
        b.Property(x => x.ActorId).HasMaxLength(100); b.Property(x => x.ActorName).HasMaxLength(160);
        b.HasIndex(x => new { x.TripId, x.OccurredAt });
        b.HasOne<DeliveryTrip>().WithMany().HasForeignKey(x => x.TripId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<DeliveryAssignment>().WithMany().HasForeignKey(x => x.AssignmentId).OnDelete(DeleteBehavior.Restrict);
    }
}
