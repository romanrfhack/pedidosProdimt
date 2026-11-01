using Prodimt.Pedidos.Domain.Enums;

namespace Prodimt.Pedidos.Application.AdminOrders;

public sealed record AdminOrderDetailResponse(
    Guid OrderId,
    Guid? CustomerId,
    string CustomerName,
    DateOnly OrderDate,
    DateTimeOffset SubmittedAt,
    OrderStatus Status,
    int SequenceNumber,
    bool IsLate,
    bool RequiresAdminReview,
    AdminReviewReason? AdminReviewReason,
    AdminDecision? AdminDecision,
    TimeOnly? RequestedDeliveryTime,
    TimeOnly? RequestedDeliveryWindowStart,
    TimeOnly? RequestedDeliveryWindowEnd,
    string? DeliveryNotes,
    string? InternalNotes,
    string? SalesChannelName,
    SalesChannelType? SalesChannelType,
    IReadOnlyList<AdminOrderLineResponse> Lines,
    int Version = 1,
    string? Source = null,
    string? ExternalOrderKey = null)
{
    public bool HasUnpricedLines => Lines.Any(x => x.UnitPrice is null);
    public decimal? TotalAmount => HasUnpricedLines ? null : Lines.Sum(x => x.LineAmount ?? 0m);
    public string CurrencyCode => "MXN";
}

public sealed record AdminOrderLineResponse(
    Guid OrderLineId,
    Guid ProductId,
    string ProductName,
    decimal Quantity,
    string? Notes,
    Guid? AssignedMachineId,
    string? AssignedMachineName,
    int? AssignedMachineNumber,
    string? UnitCode = null,
    decimal? UnitPrice = null,
    string? CurrencyCode = null,
    Guid? PriceRevisionId = null,
    DateOnly? PriceEffectiveFrom = null)
{
    public decimal? LineAmount => UnitPrice is { } price
        ? decimal.Round(Quantity * price, 2, MidpointRounding.AwayFromZero) : null;
}
