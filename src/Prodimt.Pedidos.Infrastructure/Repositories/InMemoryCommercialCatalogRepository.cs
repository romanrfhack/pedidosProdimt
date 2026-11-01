using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Infrastructure.Repositories;

public sealed class InMemoryCommercialCatalogRepository(InMemoryDataStore store) : ICommercialCatalogRepository
{
    public Task<IReadOnlyList<CustomerProductPriceQuote>> GetQuotesAsync(Guid customerId, IReadOnlyCollection<Guid> ids,
        DateOnly date, CancellationToken token)
        => Task.FromResult<IReadOnlyList<CustomerProductPriceQuote>>(store.PriceBooks
            .Where(x => x.CustomerId == customerId && ids.Contains(x.ProductId))
            .Select(book => (book.ProductId, Revision: book.At(date))).Where(x => x.Revision is not null)
            .Select(x => new CustomerProductPriceQuote(x.ProductId, x.Revision!.Id, x.Revision.UnitPrice,
                x.Revision.CurrencyCode, x.Revision.UnitCode, x.Revision.EffectiveFrom)).ToArray());

    public Task<IReadOnlyList<CustomerProductPriceBook>> GetCustomerPricesAsync(Guid customerId, CancellationToken token, bool forUpdate = false)
        => Task.FromResult<IReadOnlyList<CustomerProductPriceBook>>(store.PriceBooks.Where(x => x.CustomerId == customerId).ToArray());
    public Task<IReadOnlyList<CustomerProductPriceBook>> GetProductPricesAsync(IReadOnlyCollection<Guid> ids, CancellationToken token, bool forUpdate = false)
        => Task.FromResult<IReadOnlyList<CustomerProductPriceBook>>(store.PriceBooks.Where(x => ids.Contains(x.ProductId)).ToArray());
    public Task<CustomerProductPriceBook?> GetByPriceOperationAsync(Guid operationId, CancellationToken token)
        => Task.FromResult(store.PriceBooks.SingleOrDefault(x => x.Revisions.Any(r => r.OperationId == operationId)));
    public Task AddPriceBookAsync(CustomerProductPriceBook book, CancellationToken token)
    { store.PriceBooks.Add(book); return Task.CompletedTask; }
    public Task AddPriceRevisionAsync(CustomerProductPriceBook book, CustomerProductPriceRevision revision, CancellationToken token)
    { book.Revisions.Add(revision); return Task.CompletedTask; }
    public Task<ProductConsolidationOperation?> GetConsolidationAsync(Guid operationId, CancellationToken token)
        => Task.FromResult(store.ProductConsolidations.SingleOrDefault(x => x.Id == operationId));
    public Task AddConsolidationAsync(ProductConsolidationOperation operation, CancellationToken token)
    { store.ProductConsolidations.Add(operation); return Task.CompletedTask; }
    public Task SaveChangesAsync(CancellationToken token) => Task.CompletedTask;
}
