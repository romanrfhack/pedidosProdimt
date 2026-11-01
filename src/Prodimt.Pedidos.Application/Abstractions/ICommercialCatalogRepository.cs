using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Application.Abstractions;

public sealed record CustomerProductPriceQuote(Guid ProductId, Guid RevisionId, decimal? UnitPrice,
    string CurrencyCode, string UnitCode, DateOnly EffectiveFrom);

public interface ICommercialCatalogRepository
{
    Task<IReadOnlyList<CustomerProductPriceQuote>> GetQuotesAsync(Guid customerId, IReadOnlyCollection<Guid> productIds, DateOnly serviceDate, CancellationToken token);
    Task<IReadOnlyList<CustomerProductPriceBook>> GetCustomerPricesAsync(Guid customerId, CancellationToken token, bool forUpdate = false);
    Task<IReadOnlyList<CustomerProductPriceBook>> GetProductPricesAsync(IReadOnlyCollection<Guid> productIds, CancellationToken token, bool forUpdate = false);
    Task<CustomerProductPriceBook?> GetByPriceOperationAsync(Guid operationId, CancellationToken token);
    Task AddPriceBookAsync(CustomerProductPriceBook book, CancellationToken token);
    Task AddPriceRevisionAsync(CustomerProductPriceBook book, CustomerProductPriceRevision revision, CancellationToken token);
    Task<ProductConsolidationOperation?> GetConsolidationAsync(Guid operationId, CancellationToken token);
    Task AddConsolidationAsync(ProductConsolidationOperation operation, CancellationToken token);
    Task SaveChangesAsync(CancellationToken token);
}
