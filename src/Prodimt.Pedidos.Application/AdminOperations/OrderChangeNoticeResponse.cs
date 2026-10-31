namespace Prodimt.Pedidos.Application.AdminOperations;

public sealed record OrderChangeNoticeListResponse(DateOnly Date, int PendingCount, IReadOnlyList<OrderChangeNoticeResponse> Changes);

public sealed record OrderChangeNoticeResponse(Guid Id, Guid OrderId, int OrderVersion, int Version,
    DateOnly ServiceDate, Guid? CustomerId, string CustomerName, string? RouteCode, Guid? MachineId,
    int? MachineNumber, string? MachineName, string Kind, string Reason, string? ActorDisplayName,
    DateTimeOffset OccurredAt, DateTimeOffset? AcknowledgedAt, string? AcknowledgedBy,
    IReadOnlyList<OrderChangeLineResponse> Lines);

public sealed record OrderChangeLineResponse(Guid ProductId, string ProductName, string? UnitCode,
    decimal BeforeQuantity, decimal AfterQuantity, decimal DeltaQuantity, string? BeforeNotes, string? AfterNotes);

public sealed record AcknowledgeOrderChangeRequest(string OperationKey, int ExpectedVersion);
