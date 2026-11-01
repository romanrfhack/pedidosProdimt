using Microsoft.EntityFrameworkCore;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Infrastructure.Persistence;

namespace Prodimt.Pedidos.Infrastructure.Repositories;

public sealed class EfCommercialCatalogRepository(PedidosDbContext db) : ICommercialCatalogRepository
{
    public async Task<IReadOnlyList<CustomerProductPriceQuote>> GetQuotesAsync(Guid customerId,
        IReadOnlyCollection<Guid> productIds, DateOnly date, CancellationToken token)
        => await (from revision in db.CustomerProductPriceRevisions.AsNoTracking()
                  join book in db.CustomerProductPriceBooks.AsNoTracking() on revision.PriceBookId equals book.Id
                  where book.CustomerId == customerId && productIds.Contains(book.ProductId) && revision.EffectiveFrom <= date
                     && !db.CustomerProductPriceRevisions.Any(next => next.PriceBookId == revision.PriceBookId
                         && next.EffectiveFrom <= date && (next.EffectiveFrom > revision.EffectiveFrom
                             || next.EffectiveFrom == revision.EffectiveFrom && next.Version > revision.Version))
                  select new CustomerProductPriceQuote(book.ProductId, revision.Id, revision.UnitPrice,
                      revision.CurrencyCode, revision.UnitCode, revision.EffectiveFrom)).ToArrayAsync(token);

    private IQueryable<CustomerProductPriceBook> Books(bool forUpdate) => forUpdate
        ? db.CustomerProductPriceBooks.Include(x => x.Revisions)
        : db.CustomerProductPriceBooks.Include(x => x.Revisions).AsNoTracking();

    public async Task<IReadOnlyList<CustomerProductPriceBook>> GetCustomerPricesAsync(Guid customerId, CancellationToken token, bool forUpdate = false)
        => await Books(forUpdate).Where(x => x.CustomerId == customerId).ToArrayAsync(token);

    public async Task<IReadOnlyList<CustomerProductPriceBook>> GetProductPricesAsync(IReadOnlyCollection<Guid> productIds, CancellationToken token, bool forUpdate = false)
        => await Books(forUpdate).Where(x => productIds.Contains(x.ProductId)).ToArrayAsync(token);

    public Task<CustomerProductPriceBook?> GetByPriceOperationAsync(Guid operationId, CancellationToken token)
        => Books(false).SingleOrDefaultAsync(x => x.Revisions.Any(r => r.OperationId == operationId), token);

    public async Task AddPriceBookAsync(CustomerProductPriceBook book, CancellationToken token)
        => await db.CustomerProductPriceBooks.AddAsync(book, token);

    public Task AddPriceRevisionAsync(CustomerProductPriceBook book, CustomerProductPriceRevision revision, CancellationToken token)
    {
        book.Revisions.Add(revision);
        db.CustomerProductPriceRevisions.Add(revision);
        return Task.CompletedTask;
    }

    public Task<ProductConsolidationOperation?> GetConsolidationAsync(Guid operationId, CancellationToken token)
        => db.ProductConsolidationOperations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == operationId, token);

    public async Task AddConsolidationAsync(ProductConsolidationOperation operation, CancellationToken token)
        => await db.ProductConsolidationOperations.AddAsync(operation, token);

    public Task SaveChangesAsync(CancellationToken token) => db.SaveChangesAsync(token);
}
