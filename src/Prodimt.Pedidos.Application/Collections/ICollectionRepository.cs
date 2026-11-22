using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Application.Collections;

public interface ICollectionRepository
{
    Task<IReadOnlyList<Guid>> GetCustomerIdsAsync(DateOnly throughDate, string? routeCode, CancellationToken token);
    Task<IReadOnlyList<CollectionCashReceipt>> GetReceivedBetweenAsync(DateTimeOffset from, DateTimeOffset until, CancellationToken token);
    Task<IReadOnlyList<CollectionCharge>> GetCustomerChargesAsync(Guid customerId, CancellationToken token);
    Task<IReadOnlyList<CollectionChargeValuation>> GetCustomerValuationsAsync(Guid customerId, CancellationToken token);
    Task<IReadOnlyList<CollectionPayment>> GetCustomerPaymentsAsync(Guid customerId, CancellationToken token);
    Task<IReadOnlyList<CollectionPayment>> GetTripPaymentsAsync(Guid tripId, CancellationToken token);
    Task<IReadOnlyList<CollectionCashReceipt>> GetTripReceiptsAsync(Guid tripId, CancellationToken token);
    Task<IReadOnlyList<CollectionCashReceiptLine>> GetTripReceiptLinesAsync(Guid tripId, CancellationToken token);
    Task AddChargeAsync(CollectionCharge item, CancellationToken token);
    Task AddValuationAsync(CollectionChargeValuation item, CancellationToken token);
    Task AddPaymentAsync(CollectionPayment item, CancellationToken token);
    Task AddReceiptAsync(CollectionCashReceipt item, CancellationToken token);
    Task AddReceiptLineAsync(CollectionCashReceiptLine item, CancellationToken token);
}
