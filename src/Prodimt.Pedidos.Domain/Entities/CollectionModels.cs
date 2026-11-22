namespace Prodimt.Pedidos.Domain.Entities;

// Financial movements retain their source and never rewrite delivered quantities.
public sealed class CollectionCharge
{
    public Guid Id { get; init; }
    public string SourceKey { get; init; } = "";
    public Guid? DeliveryEventId { get; init; }
    public Guid AssignmentId { get; init; }
    public Guid TripId { get; init; }
    public Guid OrderId { get; init; }
    public Guid OrderLineId { get; init; }
    public Guid CustomerId { get; init; }
    public string CustomerName { get; init; } = "";
    public Guid ProductId { get; init; }
    public string ProductName { get; init; } = "";
    public string UnitCode { get; init; } = "";
    public DateOnly ServiceDate { get; init; }
    public string RouteCode { get; init; } = "";
    public int TripNumber { get; init; }
    public Guid DriverUserId { get; init; }
    public string DriverName { get; init; } = "";
    public string? PaymentTermCode { get; init; }
    public int Sequence { get; init; }
    public decimal RequestedQuantity { get; init; }
    public decimal Quantity { get; init; }
    public bool IsCredit { get; init; }
    public decimal? UnitPrice { get; init; }
    public decimal? Amount { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public string? ActorId { get; init; }
    public string? ActorName { get; init; }
}

public sealed class CollectionChargeValuation
{
    public Guid Id { get; init; }
    public Guid ChargeId { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal Amount { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public string? ActorId { get; init; }
    public string? ActorName { get; init; }
}

public sealed class CollectionPayment
{
    public Guid Id { get; init; }
    public Guid CustomerId { get; init; }
    public string CustomerName { get; init; } = "";
    public Guid TripId { get; init; }
    public Guid AssignmentId { get; init; }
    public Guid DriverUserId { get; init; }
    public string DriverName { get; init; } = "";
    public DateOnly ServiceDate { get; init; }
    public string RouteCode { get; init; } = "";
    public int TripNumber { get; init; }
    public decimal Amount { get; init; }
    public string AllocationPolicy { get; init; } = "service-day-then-oldest-debt-then-credit";
    public string AllocationsJson { get; init; } = "[]";
    public decimal? CreditAmount { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public string? ActorId { get; init; }
    public string? ActorName { get; init; }
}

public sealed class CollectionCashReceipt
{
    public Guid Id { get; init; }
    public Guid TripId { get; init; }
    public DateOnly ServiceDate { get; init; }
    public Guid DriverUserId { get; init; }
    public string DriverName { get; init; } = "";
    public string RouteCode { get; init; } = "";
    public int TripNumber { get; init; }
    public decimal ExpectedAmount { get; init; }
    public decimal ReceivedAmount { get; init; }
    public string? Reason { get; init; }
    public Guid ReceiverUserId { get; init; }
    public string ReceiverName { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed class CollectionCashReceiptLine
{
    public Guid Id { get; init; }
    public Guid ReceiptId { get; init; }
    public Guid PaymentId { get; init; }
    public decimal Amount { get; init; }
}
