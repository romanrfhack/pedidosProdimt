using Prodimt.Pedidos.Domain.Enums;

namespace Prodimt.Pedidos.Application.AdminOperations;

public sealed record OperationsPlanResponse(
    DateOnly Date,
    string? RouteCode,
    string DemandLabel,
    string PreparationLabel,
    IReadOnlyList<OperationsOrderResponse> Orders,
    IReadOnlyList<OperationsDemandResponse> MachineDemand,
    IReadOnlyList<OperationsPendingReviewResponse> PendingReview,
    IReadOnlyList<OperationsPlanWarning> Warnings);

public sealed record OperationsOrderResponse(
    Guid OrderId,
    Guid? CustomerId,
    string CustomerName,
    int SequenceNumber,
    OrderStatus Status,
    string? RouteCode,
    int? RouteStopOrder,
    bool? HasStoreKey,
    string? PaymentTermCode,
    string? InternalDeliveryInstructions,
    TimeOnly? RequestedDeliveryTime,
    TimeOnly? RequestedDeliveryWindowStart,
    TimeOnly? RequestedDeliveryWindowEnd,
    string? DeliveryNotes,
    int? OrderingProfileVersion,
    IReadOnlyList<OperationsLineResponse> Lines);

public sealed record OperationsLineResponse(
    Guid OrderLineId,
    Guid ProductId,
    string ProductName,
    string? UnitCode,
    decimal Quantity,
    Guid? AssignedMachineId,
    int? AssignedMachineNumber,
    string? AssignedMachineName,
    string? Notes);

public sealed record OperationsDemandResponse(
    Guid? MachineId,
    int? MachineNumber,
    string? MachineName,
    Guid ProductId,
    string ProductName,
    string UnitCode,
    decimal Quantity);

public sealed record OperationsPendingReviewResponse(
    Guid OrderId,
    Guid? CustomerId,
    string CustomerName,
    int SequenceNumber,
    AdminReviewReason? ReviewReason);

public sealed record OperationsPlanWarning(string Code, string Message, Guid? OrderId, Guid? OrderLineId);
