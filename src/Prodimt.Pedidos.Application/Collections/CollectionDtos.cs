namespace Prodimt.Pedidos.Application.Collections;

public sealed record CollectionTicketLine(Guid OrderId, Guid OrderLineId, string ProductName, string UnitCode,
    decimal Quantity, decimal? UnitPrice, decimal? Amount);
public sealed record CollectionPreviewResponse(string? PaymentTermCode, Guid CustomerId, string CustomerName,
    IReadOnlyList<CollectionTicketLine> TicketLines, decimal? TicketTotal, decimal? PriorBalance,
    decimal CollectedAmount, decimal? SuggestedAmount, decimal? Balance, string Version,
    string CurrencyCode, bool PricePending, decimal? AmountDue, decimal? CreditBalance);
public sealed record CollectionPaymentResponse(Guid Id, Guid CustomerId, string CustomerName, decimal Amount,
    DateTimeOffset CreatedAt, string DriverName, DateOnly ServiceDate, Guid TripId, string RouteCode, int TripNumber, string? ActorId, string? ActorName);
public sealed record CollectionTripCustomerResponse(Guid CustomerId, string CustomerName, decimal? ChargeAmount,
    decimal PaidAmount, decimal? Balance, bool PricePending);
public sealed record CollectionTripResponse(Guid TripId, IReadOnlyList<CollectionTripCustomerResponse> Customers,
    decimal? ChargeAmount, decimal PaidAmount, decimal PendingCashAmount, bool PricePending);
public sealed record CollectionReceiptResponse(Guid Id, DateTimeOffset CreatedAt, string ReceiverName,
    decimal ExpectedAmount, decimal ReceivedAmount, decimal Difference, string? Reason);
public sealed record CollectionCashTripResponse(Guid TripId, DateOnly Date, string RouteCode, string RouteName,
    int TripNumber, Guid DriverUserId, string DriverName, DateTimeOffset? DepartedAt, string Status,
    decimal ExpectedAmount, decimal AccountedAmount, decimal ReceivedAmount, decimal PendingAmount, decimal Difference,
    string Version, IReadOnlyList<CollectionReceiptResponse> Receipts);
public sealed record CollectionCashDashboardResponse(DateOnly Date, string? RouteCode,
    IReadOnlyList<CollectionCashTripResponse> Trips, decimal ExpectedAmount, decimal ReceivedAmount,
    decimal PendingAmount, decimal Difference, decimal MyReceivedAmount,
    IReadOnlyList<CollectionCashCustomerResponse> Customers);
public sealed record CollectionCashCustomerResponse(Guid CustomerId, string CustomerName, decimal? DayChargeAmount,
    decimal DayPaidAmount, decimal? Balance, decimal? CreditBalance, bool PricePending);
public sealed record ReceiveCollectionCashRequest(Guid OperationId, Guid TripId, string ExpectedVersion,
    decimal? DeclaredAmount, decimal ReceivedAmount, string? Reason);
public sealed record CollectionCommandResponse(Guid Id, bool Replayed = false);
public sealed record CollectionAccountEntry(Guid Id, DateOnly Date, Guid OrderId, Guid OrderLineId,
    string ProductName, string UnitCode, decimal RequestedQuantity, decimal Quantity, bool IsCredit, decimal? UnitPrice,
    decimal? Amount, string RouteCode, int TripNumber, string DriverName, bool Recorded);
public sealed record CollectionCustomerAccountResponse(Guid CustomerId, string CustomerName,
    IReadOnlyList<CollectionAccountEntry> Charges, IReadOnlyList<CollectionPaymentResponse> Payments,
    decimal? ChargesTotal, decimal PaidTotal, decimal? Balance, decimal? CreditBalance, bool PricePending,
    string AllocationPolicy = "service-day-then-oldest-debt-then-credit");
