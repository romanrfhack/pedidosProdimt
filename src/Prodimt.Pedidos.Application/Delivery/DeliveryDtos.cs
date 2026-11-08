namespace Prodimt.Pedidos.Application.Delivery;

public sealed record DeliveryDashboardResponse(DateOnly Date, IReadOnlyList<DeliveryRouteResponse> Routes,
    IReadOnlyList<DeliveryDriverResponse> Drivers, IReadOnlyList<DeliveryTripResponse> Trips,
    IReadOnlyList<DeliveryOrderResponse> Orders, bool CanPrepare = false, bool OperationalDate = false);
public sealed record DeliveryRouteResponse(string Code, string Name, Guid? DefaultDriverUserId, bool IsActive, int Version,
    Guid? DayDriverUserId = null, int DayDriverVersion = 0);
public sealed record DeliveryDriverResponse(Guid Id, string DisplayName, string UserName);
public sealed record DeliveryOrderResponse(Guid OrderId, Guid CustomerId, string CustomerName, int SequenceNumber,
    string? RouteCode, int? RouteStopOrder, int OrderVersion, string? MapsUrl, string? Address,
    TimeOnly? RequestedDeliveryTime, TimeOnly? RequestedDeliveryWindowStart, TimeOnly? RequestedDeliveryWindowEnd,
    bool? HasStoreKey, string? PaymentTermCode, string? DeliveryNotes, string? InternalDeliveryInstructions,
    int LocationVersion, IReadOnlyList<DeliveryOrderLineResponse> Lines);
public sealed record DeliveryOrderLineResponse(Guid OrderLineId, Guid ProductId, string ProductName, string? UnitCode,
    decimal RequestedQuantity, decimal DeliveredQuantity, decimal ReservedQuantity, decimal AvailableQuantity);
public sealed record DeliveryTripResponse(Guid Id, DateOnly Date, string RouteCode, string RouteName,
    int TripNumber, Guid DriverUserId, string DriverName, string Status, int Version,
    DateTimeOffset CreatedAt, DateTimeOffset? ClosedAt, IReadOnlyList<DeliveryAssignmentResponse> Assignments,
    IReadOnlyList<DeliveryEventResponse> Events, DateTimeOffset? DepartedAt = null,
    IReadOnlyList<DeliveryOrderResponse>? PlannedOrders = null, bool CanChangeDriver = false);
public sealed record DeliveryAssignmentResponse(Guid Id, Guid OrderId, Guid OrderLineId, Guid CustomerId,
    string CustomerName, int SequenceNumber, int? RouteStopOrder, string ProductName, string UnitCode,
    decimal RequestedQuantity, decimal AssignedQuantity, decimal ReleasedQuantity, decimal? CountedQuantity,
    decimal? LoadedQuantity, decimal DeliveredQuantity, decimal ReturnedQuantity, decimal TransferredOutQuantity,
    decimal TransferredInQuantity, decimal CustodyQuantity, decimal PendingQuantity, string Status, int Version,
    int CurrentOrderVersion, bool HasOrderChanges, string? MapsUrl, string? Address,
    TimeOnly? RequestedDeliveryTime, TimeOnly? RequestedDeliveryWindowStart, TimeOnly? RequestedDeliveryWindowEnd,
    bool? HasStoreKey, string? PaymentTermCode, string? DeliveryNotes, string? InternalDeliveryInstructions,
    bool OrderIsComplete = false);
public sealed record DeliveryEventResponse(Guid Id, Guid? AssignmentId, string Type, decimal? Quantity,
    decimal? PreviousQuantity, string? Reason, string? Notes, string? ActorName, DateTimeOffset OccurredAt);
public sealed record DeliveryCommandResponse(Guid Id, int? Version = null, bool Replayed = false);
public sealed record SaveDeliveryRouteRequest(Guid OperationId, string Code, string Name, Guid? DefaultDriverUserId,
    int? ExpectedVersion = null, bool IsActive = true);
public sealed record SaveDeliveryLocationRequest(Guid OperationId, string? MapsUrl, string? Address, int ExpectedVersion = 0);
public sealed record CreateDeliveryTripRequest(Guid OperationId, DateOnly Date, string RouteCode, int TripNumber,
    Guid? DriverUserId = null, string? Reason = null);
public sealed record PrepareDeliveryRequest(Guid OperationId, DateOnly Date);
public sealed record SaveDeliveryDayDriverRequest(Guid OperationId, DateOnly Date, string RouteCode, Guid? DriverUserId,
    int ExpectedVersion, int? ExpectedTripVersion, string Reason);
public sealed record CountPlannedDeliveryLineRequest(Guid OperationId, Guid OrderId, Guid OrderLineId,
    int ExpectedOrderVersion, int ExpectedTripVersion, decimal Quantity, string? Reason = null, string? Notes = null);
public sealed record DepartDeliveryTripRequest(Guid OperationId, int ExpectedVersion);
public sealed record AssignDeliveryLineRequest(Guid OperationId, Guid OrderId, Guid OrderLineId, decimal Quantity,
    int ExpectedTripVersion, int ExpectedOrderVersion);
public sealed record RecordDeliveryStageRequest(Guid OperationId, int ExpectedVersion, int ExpectedOrderVersion,
    decimal Quantity, string? Reason = null, string? Notes = null, string? RemainderDisposition = null,
    bool AcknowledgeOrderChange = false);
public sealed record CloseDeliveryTripRequest(Guid OperationId, int ExpectedVersion, string? Notes = null);
public sealed record TransferDeliveryRequest(Guid OperationId, int ExpectedVersion, Guid TargetTripId,
    int ExpectedTargetTripVersion, decimal Quantity, string Reason);
public sealed record AdjustDeliveryRequest(Guid OperationId, int ExpectedVersion, string Stage, decimal Quantity, string Reason);
public sealed record ReleaseDeliveryAssignmentRequest(Guid OperationId, int ExpectedVersion, string Reason);
