namespace Prodimt.Pedidos.Domain.Entities;

public sealed class DeliveryRoute
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Guid? DefaultDriverUserId { get; set; }
    public bool IsActive { get; set; } = true;
    public int Version { get; set; } = 1;
}

public sealed class CustomerDeliveryLocation
{
    public Guid CustomerId { get; set; }
    public string? MapsUrl { get; set; }
    public string? Address { get; set; }
    public int Version { get; set; } = 1;
}

public sealed class DeliveryTrip
{
    public Guid Id { get; set; }
    public DateOnly Date { get; set; }
    public string RouteCode { get; set; } = string.Empty;
    public string RouteName { get; set; } = string.Empty;
    public int TripNumber { get; set; }
    public Guid DriverUserId { get; set; }
    public string DriverName { get; set; } = string.Empty;
    public string Status { get; set; } = "Open";
    public int Version { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
}

public sealed class DeliveryAssignment
{
    public Guid Id { get; set; }
    public Guid TripId { get; set; }
    public Guid OrderId { get; set; }
    // Snapshot reference: historical attempts survive a released line being replaced.
    public Guid OrderLineId { get; set; }
    public Guid ProductId { get; set; }
    public Guid CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public int SequenceNumber { get; set; }
    public int? RouteStopOrder { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string UnitCode { get; set; } = string.Empty;
    public decimal RequestedQuantity { get; set; }
    public decimal AssignedQuantity { get; set; }
    public decimal ReleasedQuantity { get; set; }
    public decimal? CountedQuantity { get; set; }
    public decimal? LoadedQuantity { get; set; }
    public decimal DeliveredQuantity { get; set; }
    public decimal ReturnedQuantity { get; set; }
    public decimal TransferredOutQuantity { get; set; }
    public decimal TransferredInQuantity { get; set; }
    public bool HasUnresolvedIncident { get; set; }
    public int AcknowledgedOrderVersion { get; set; }
    public int Version { get; set; } = 1;
    public string? MapsUrl { get; set; }
    public string? Address { get; set; }
    public TimeOnly? RequestedDeliveryTime { get; set; }
    public TimeOnly? RequestedDeliveryWindowStart { get; set; }
    public TimeOnly? RequestedDeliveryWindowEnd { get; set; }
    public bool? HasStoreKey { get; set; }
    public string? PaymentTermCode { get; set; }
    public string? DeliveryNotes { get; set; }
    public string? InternalDeliveryInstructions { get; set; }
    public decimal CustodyQuantity => (LoadedQuantity ?? 0) + TransferredInQuantity - DeliveredQuantity - ReturnedQuantity - TransferredOutQuantity;
    public decimal CommitmentQuantity => AssignedQuantity - ReleasedQuantity;
}

public sealed class DeliveryEvent
{
    public Guid Id { get; set; }
    public Guid TripId { get; set; }
    public Guid? AssignmentId { get; set; }
    public string Type { get; set; } = string.Empty;
    public decimal? Quantity { get; set; }
    public decimal? PreviousQuantity { get; set; }
    public string? Reason { get; set; }
    public string? Notes { get; set; }
    public string? ActorId { get; set; }
    public string? ActorName { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
