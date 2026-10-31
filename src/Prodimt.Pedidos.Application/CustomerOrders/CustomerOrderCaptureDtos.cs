using Prodimt.Pedidos.Application.AdminOrders;
using Prodimt.Pedidos.Domain.Enums;

namespace Prodimt.Pedidos.Application.CustomerOrders;

public sealed record CustomerCaptureProduct(Guid ProductId, string ProductName, string? UnitCode,
    int? InterpretationPosition, string? CaptureAlias, int SortOrder);

public sealed record CustomerCaptureCurrentLine(Guid ProductId, string ProductName, decimal Quantity,
    string? UnitCode, string? Notes);

public sealed record CustomerCaptureCurrentOrder(Guid OrderId, DateOnly DeliveryDate, OrderStatus Status,
    DateTimeOffset SubmittedAt, string? DeliveryNotes, IReadOnlyList<CustomerCaptureCurrentLine> Lines);

public sealed record CustomerNextOrderResponse(Guid CustomerId, string CustomerName, DateOnly DeliveryDate,
    DateTimeOffset ServerNow, int ProfileVersion, bool IsProfileApproved, bool HasActiveOrder,
    IReadOnlyList<CustomerCaptureProduct> Products, CustomerCaptureCurrentOrder? CurrentOrder,
    TimeOnly? PreferredDeliveryTime, TimeOnly? PreferredDeliveryWindowStart, TimeOnly? PreferredDeliveryWindowEnd);

public sealed record PreviewCustomerOrderCaptureRequest(string Text, DateOnly ExpectedDeliveryDate);

public sealed record PreviewCustomerOrderCaptureResponse(DateOnly DeliveryDate, DateTimeOffset ServerNow,
    string Text, string Fingerprint, int ProfileVersion, IReadOnlyList<ResolvedCaptureLine> Lines);

public sealed record SubmitCustomerOrderCaptureRequest(Guid OperationId, int ExpectedProfileVersion,
    DateOnly ExpectedDeliveryDate, string? Text = null, string? PreviewFingerprint = null,
    IReadOnlyList<ManualOrderCaptureLine>? Lines = null, string? DeliveryNotes = null);

public sealed record MarkNextCustomerNoOrderRequest(Guid OperationId, DateOnly ExpectedDeliveryDate);

public sealed record SubmitCustomerOrderCaptureResponse(CustomerOrderResponse Order, bool Replayed);
