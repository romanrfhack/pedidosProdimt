namespace Prodimt.Pedidos.Application.AdminOrders;

public sealed record DailyOrderImportRequest(string Content, string Source, string BatchKey, string? FileName = null);
public sealed record CorrectOrderRequest(string OperationKey, int ExpectedVersion, string Reason,
    IReadOnlyList<AdminSubmitCustomerOrderLineRequest> Lines);
public sealed record DailyOrderImportError(int? RowNumber, string Message);
public sealed record DailyOrderImportLine(Guid ProductId, string ProductName, decimal Quantity, string UnitCode, string? Notes);
public sealed record DailyOrderImportOrder(string ExternalOrderKey, Guid CustomerId, string CustomerName,
    DateOnly ServiceDate, string Action, string Disposition, Guid? OrderId, IReadOnlyList<DailyOrderImportLine> Lines);
public sealed record DailyOrderImportResponse(string BatchKey, bool CanApply, bool Applied, bool Replayed,
    IReadOnlyList<DailyOrderImportOrder> Orders, IReadOnlyList<DailyOrderImportError> Errors, IReadOnlyList<string> Warnings);
