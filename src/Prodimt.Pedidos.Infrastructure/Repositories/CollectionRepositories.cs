using Microsoft.EntityFrameworkCore;
using Prodimt.Pedidos.Application.Collections;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Infrastructure.Persistence;

namespace Prodimt.Pedidos.Infrastructure.Repositories;

public sealed class EfCollectionRepository(PedidosDbContext db) : ICollectionRepository
{
    public async Task<IReadOnlyList<Guid>> GetCustomerIdsAsync(DateOnly throughDate, string? routeCode, CancellationToken token)
    {
        var charges = db.Set<CollectionCharge>().Where(x => x.ServiceDate <= throughDate &&
            (routeCode == null || x.RouteCode == routeCode)).Select(x => x.CustomerId);
        var payments = db.Set<CollectionPayment>().Where(x => x.ServiceDate <= throughDate &&
            (routeCode == null || x.RouteCode == routeCode)).Select(x => x.CustomerId);
        return await charges.Union(payments).Distinct().ToArrayAsync(token);
    }
    public async Task<IReadOnlyList<CollectionCharge>> GetCustomerChargesAsync(Guid customerId, CancellationToken token) =>
        (await db.Set<CollectionCharge>().AsNoTracking().Where(x => x.CustomerId == customerId).ToArrayAsync(token))
            .Concat(Added<CollectionCharge>().Where(x => x.CustomerId == customerId)).ToArray();
    public async Task<IReadOnlyList<CollectionChargeValuation>> GetCustomerValuationsAsync(Guid customerId, CancellationToken token)
    {
        var charges = await GetCustomerChargesAsync(customerId, token);
        var ids = charges.Select(x => x.Id).ToHashSet();
        var saved = await (from value in db.Set<CollectionChargeValuation>().AsNoTracking()
                           join charge in db.Set<CollectionCharge>() on value.ChargeId equals charge.Id
                           where charge.CustomerId == customerId select value).ToArrayAsync(token);
        return saved.Concat(Added<CollectionChargeValuation>().Where(x => ids.Contains(x.ChargeId))).ToArray();
    }
    public async Task<IReadOnlyList<CollectionPayment>> GetCustomerPaymentsAsync(Guid customerId, CancellationToken token) =>
        (await db.Set<CollectionPayment>().AsNoTracking().Where(x => x.CustomerId == customerId).ToArrayAsync(token))
            .Concat(Added<CollectionPayment>().Where(x => x.CustomerId == customerId)).ToArray();
    public async Task<IReadOnlyList<CollectionPayment>> GetTripPaymentsAsync(Guid tripId, CancellationToken token) =>
        (await db.Set<CollectionPayment>().AsNoTracking().Where(x => x.TripId == tripId).ToArrayAsync(token))
            .Concat(Added<CollectionPayment>().Where(x => x.TripId == tripId)).ToArray();
    public async Task<IReadOnlyList<CollectionCashReceipt>> GetTripReceiptsAsync(Guid tripId, CancellationToken token) =>
        (await db.Set<CollectionCashReceipt>().AsNoTracking().Where(x => x.TripId == tripId).ToArrayAsync(token))
            .Concat(Added<CollectionCashReceipt>().Where(x => x.TripId == tripId)).ToArray();
    public async Task<IReadOnlyList<CollectionCashReceiptLine>> GetTripReceiptLinesAsync(Guid tripId, CancellationToken token)
    {
        var receipts = (await GetTripReceiptsAsync(tripId, token)).Select(x => x.Id).ToHashSet();
        return (await (from line in db.Set<CollectionCashReceiptLine>().AsNoTracking()
                       join receipt in db.Set<CollectionCashReceipt>() on line.ReceiptId equals receipt.Id
                       where receipt.TripId == tripId select line).ToArrayAsync(token))
            .Concat(Added<CollectionCashReceiptLine>().Where(x => receipts.Contains(x.ReceiptId))).ToArray();
    }
    private IEnumerable<T> Added<T>() where T : class =>
        db.ChangeTracker.Entries<T>().Where(x => x.State == EntityState.Added).Select(x => x.Entity);
    public async Task<IReadOnlyList<CollectionCashReceipt>> GetReceivedBetweenAsync(DateTimeOffset from,
        DateTimeOffset until, CancellationToken token)
    {
        // SQLite tests cannot compare DateTimeOffset server-side.
        if (db.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
            return (await db.Set<CollectionCashReceipt>().AsNoTracking().ToArrayAsync(token))
                .Where(x => x.CreatedAt >= from && x.CreatedAt < until).ToArray();
        return await db.Set<CollectionCashReceipt>().AsNoTracking().Where(x => x.CreatedAt >= from && x.CreatedAt < until).ToArrayAsync(token);
    }
    public async Task AddChargeAsync(CollectionCharge item, CancellationToken token) => await db.Set<CollectionCharge>().AddAsync(item, token);
    public async Task AddValuationAsync(CollectionChargeValuation item, CancellationToken token) => await db.Set<CollectionChargeValuation>().AddAsync(item, token);
    public async Task AddPaymentAsync(CollectionPayment item, CancellationToken token) => await db.Set<CollectionPayment>().AddAsync(item, token);
    public async Task AddReceiptAsync(CollectionCashReceipt item, CancellationToken token) => await db.Set<CollectionCashReceipt>().AddAsync(item, token);
    public async Task AddReceiptLineAsync(CollectionCashReceiptLine item, CancellationToken token) => await db.Set<CollectionCashReceiptLine>().AddAsync(item, token);
}

public sealed class InMemoryCollectionRepository : ICollectionRepository
{
    public Task<IReadOnlyList<Guid>> GetCustomerIdsAsync(DateOnly throughDate, string? routeCode, CancellationToken token) => Task.FromResult<IReadOnlyList<Guid>>([]);
    public Task<IReadOnlyList<CollectionCharge>> GetCustomerChargesAsync(Guid customerId, CancellationToken token) => Task.FromResult<IReadOnlyList<CollectionCharge>>([]);
    public Task<IReadOnlyList<CollectionChargeValuation>> GetCustomerValuationsAsync(Guid customerId, CancellationToken token) => Task.FromResult<IReadOnlyList<CollectionChargeValuation>>([]);
    public Task<IReadOnlyList<CollectionPayment>> GetCustomerPaymentsAsync(Guid customerId, CancellationToken token) => Task.FromResult<IReadOnlyList<CollectionPayment>>([]);
    public Task<IReadOnlyList<CollectionPayment>> GetTripPaymentsAsync(Guid tripId, CancellationToken token) => Task.FromResult<IReadOnlyList<CollectionPayment>>([]);
    public Task<IReadOnlyList<CollectionCashReceipt>> GetTripReceiptsAsync(Guid tripId, CancellationToken token) => Task.FromResult<IReadOnlyList<CollectionCashReceipt>>([]);
    public Task<IReadOnlyList<CollectionCashReceiptLine>> GetTripReceiptLinesAsync(Guid tripId, CancellationToken token) => Task.FromResult<IReadOnlyList<CollectionCashReceiptLine>>([]);
    public Task<IReadOnlyList<CollectionCashReceipt>> GetReceivedBetweenAsync(DateTimeOffset from, DateTimeOffset until, CancellationToken token) => Task.FromResult<IReadOnlyList<CollectionCashReceipt>>([]);
    public Task AddChargeAsync(CollectionCharge item, CancellationToken token) => throw new NotSupportedException();
    public Task AddValuationAsync(CollectionChargeValuation item, CancellationToken token) => throw new NotSupportedException();
    public Task AddPaymentAsync(CollectionPayment item, CancellationToken token) => throw new NotSupportedException();
    public Task AddReceiptAsync(CollectionCashReceipt item, CancellationToken token) => throw new NotSupportedException();
    public Task AddReceiptLineAsync(CollectionCashReceiptLine item, CancellationToken token) => throw new NotSupportedException();
}
