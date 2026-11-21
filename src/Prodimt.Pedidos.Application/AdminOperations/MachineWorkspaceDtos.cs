namespace Prodimt.Pedidos.Application.AdminOperations;

public sealed record MachineWorkspaceMachine(Guid Id, int Number, string Name);
public sealed record MachineWorkspaceTotals(decimal ProducedQuantity, decimal AllocatedQuantity,
    decimal PendingQuantity, decimal AvailableQuantity);
public sealed record MachineWorkspaceLine(Guid OrderLineId, Guid ProductId, string ProductName,
    decimal Quantity, decimal AllocatedQuantity, decimal PendingQuantity, decimal ReleasedQuantity);
public sealed record MachineWorkspaceOrder(Guid OrderId, Guid? CustomerId, string CustomerName, int SequenceNumber,
    string? RouteCode, int? RouteStopOrder, int OrderVersion, IReadOnlyList<MachineWorkspaceLine> Lines);
public sealed record MachineWorkspaceBatch(Guid Id, Guid ProductId, string ProductName, decimal Quantity,
    decimal AllocatedQuantity, decimal AvailableQuantity, decimal HandedOffQuantity, DateTimeOffset CreatedAt);
public sealed record MachineWorkspaceProduct(Guid Id, string Name);
public sealed record MachineWorkspaceNotice(Guid Id, Guid OrderId, Guid OrderLineId, string CustomerName,
    string ProductName, decimal Quantity, DateTimeOffset CreatedAt);
public sealed record MachineWorkspaceResponse(DateOnly Date, MachineWorkspaceMachine Machine, MachineWorkspaceTotals Totals,
    IReadOnlyList<MachineWorkspaceOrder> Orders, IReadOnlyList<MachineWorkspaceBatch> Batches,
    IReadOnlyList<MachineWorkspaceProduct> Products, IReadOnlyList<MachineWorkspaceNotice> Notices);
public sealed record MachineCaptureRequest(Guid OperationId, Guid OrderId, Guid OrderLineId, decimal Quantity, int ExpectedOrderVersion);
public sealed record MachineLotRequest(Guid OperationId, Guid ProductId, decimal Quantity, DateOnly Date);
public sealed record MachineReleaseRequest(Guid OperationId, Guid OrderId, Guid? OrderLineId, decimal? Quantity, int ExpectedOrderVersion);
public sealed record MachineHandoffRequest(Guid OperationId, Guid BatchId, decimal Quantity);
public sealed record MachineOperatorUser(Guid Id, string DisplayName);
public sealed record MachineOperatorsResponse(IReadOnlyList<MachineOperatorUser> Users, IReadOnlyList<Guid> UserIds, string Version);
public sealed record ReplaceMachineOperatorsRequest(Guid OperationId, IReadOnlyList<Guid> UserIds, string ExpectedVersion);
