namespace Prodimt.Pedidos.Application.AdminOperations;

public sealed record ProductionDashboardResponse(DateOnly Date, string? RouteCode, string SummaryScope,
    IReadOnlyList<ProductionMachineResponse> Machines, IReadOnlyList<ProductionBatchResponse> Batches,
    IReadOnlyList<ProductionOrderResponse> Orders, IReadOnlyList<ProductionAllocationResponse> Allocations);
public sealed record ProductionMachineResponse(Guid MachineId, int MachineNumber, string MachineName,
    decimal AssignedQuantity, decimal ProducedQuantity, decimal PendingQuantity, decimal AllocatedQuantity,
    decimal AvailableQuantity, bool IsActive = true);
public sealed record ProductionBatchResponse(Guid Id, DateOnly Date, Guid MachineId, int? MachineNumber,
    string? MachineName, Guid ProductId, string ProductName, string UnitCode, decimal Quantity,
    decimal AllocatedQuantity, decimal AvailableQuantity, DateTimeOffset CreatedAt);
public sealed record ProductionOrderResponse(Guid OrderId, Guid? CustomerId, string CustomerName, int SequenceNumber,
    string? RouteCode, int? RouteStopOrder, int OrderVersion, string? InternalDeliveryInstructions,
    TimeOnly? RequestedDeliveryTime, TimeOnly? RequestedDeliveryWindowStart, TimeOnly? RequestedDeliveryWindowEnd,
    IReadOnlyList<ProductionLineResponse> Lines);
public sealed record ProductionLineResponse(Guid OrderLineId, Guid ProductId, string ProductName,
    string? UnitCode, decimal Quantity, Guid? AssignedMachineId, int? AssignedMachineNumber,
    string? AssignedMachineName, decimal AllocatedQuantity, decimal PendingAllocationQuantity, string? Notes);
public sealed record ProductionAllocationResponse(Guid Id, Guid BatchId, Guid OrderId, Guid OrderLineId,
    string CustomerName, decimal Quantity, DateTimeOffset CreatedAt, DateTimeOffset? ReversedAt, string? ReverseReason);
public sealed record RegisterProductionRequest(Guid OperationId, DateOnly Date, Guid MachineId, Guid ProductId,
    decimal Quantity, string? Notes = null);
public sealed record AllocateProductionRequest(Guid OperationId, Guid BatchId, Guid OrderId, Guid OrderLineId,
    decimal Quantity, int ExpectedOrderVersion);
public sealed record ReverseProductionAllocationRequest(Guid OperationId, string Reason);
public sealed record AssignProductionMachineRequest(Guid OperationId, Guid? MachineId, int ExpectedOrderVersion, string Reason);
public sealed record ProductionCommandResponse(Guid Id, int? OrderVersion, bool Replayed = false);
