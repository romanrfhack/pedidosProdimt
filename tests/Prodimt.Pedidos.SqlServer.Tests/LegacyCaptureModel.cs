using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.SqlServer.Tests;

// Historical repair tools intentionally accept exactly eleven migrations. Their
// fixtures must keep that schema/model instead of weakening the shipped guards.
public sealed class LegacyCaptureModelCustomizer(ModelCustomizerDependencies dependencies) : ModelCustomizer(dependencies)
{
    public override void Customize(ModelBuilder builder, DbContext context)
    {
        base.Customize(builder, context);
        var product = builder.Entity<Product>();
        foreach (var fk in product.Metadata.GetForeignKeys().Where(x => x.Properties.Any(p => p.Name == nameof(Product.MergedIntoProductId))).ToArray())
            product.Metadata.RemoveForeignKey(fk);
        product.Ignore(x => x.MergedIntoProductId);
        foreach (var constraint in product.Metadata.GetCheckConstraints().Where(x => x.Name == "CK_Products_Consolidated").ToArray())
            product.Metadata.RemoveCheckConstraint(constraint.Name!);
        var line = builder.Entity<OrderLine>();
        foreach (var fk in line.Metadata.GetForeignKeys().Where(x => x.Properties.Any(p => p.Name == nameof(OrderLine.PriceRevisionId))).ToArray())
            line.Metadata.RemoveForeignKey(fk);
        line.Ignore(x => x.UnitPriceSnapshot); line.Ignore(x => x.CurrencyCodeSnapshot);
        line.Ignore(x => x.PriceRevisionId); line.Ignore(x => x.PriceEffectiveFrom);
        builder.Ignore<CustomerProductPriceRevision>(); builder.Ignore<CustomerProductPriceBook>();
        builder.Ignore<ProductConsolidationOperation>();
        // I12 token storage did not exist in the pinned eleven-migration repair target.
        var access = builder.Entity<CustomerAccessToken>();
        access.Ignore(x => x.ProtectedToken);
        access.Ignore(x => x.MetadataVersion);
        access.Ignore(x => x.IsReminderLink);
        builder.Entity<CustomerFrequentProduct>().Ignore(x => x.UseCustomerDefaultMachine);
        builder.Ignore<CustomerProductSetupRequest>(); builder.Ignore<CustomerProductSetupOrder>();
    }
}

// Historical repair fixtures have no I16 tables. New-product writes stay disabled
// instead of weakening their eleven-migration guard or shipping this adapter.
public sealed class LegacyCustomerProductSetupRepository : ICustomerProductSetupRepository
{
    public Task<int> CountOpenAsync(CancellationToken token) => Task.FromResult(0);
    public Task<IReadOnlyList<CustomerProductSetupRequest>> GetOpenAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<CustomerProductSetupRequest>>([]);
    public Task<CustomerProductSetupRequest?> GetAsync(Guid id, CancellationToken token) => Task.FromResult<CustomerProductSetupRequest?>(null);
    public Task<CustomerProductSetupRequest?> GetByPairAsync(Guid customerId, Guid productId, CancellationToken token) => Task.FromResult<CustomerProductSetupRequest?>(null);
    public Task<IReadOnlyList<Order>> GetOrdersAsync(IReadOnlyCollection<Guid> orderIds, CancellationToken token) => Task.FromResult<IReadOnlyList<Order>>([]);
    public Task AddAsync(CustomerProductSetupRequest request, CancellationToken token) => throw new NotSupportedException();
    public Task SaveChangesAsync(CancellationToken token) => Task.CompletedTask;
}

// The historical portal/capture has no pricing dependency. Never published with the API.
public sealed class LegacyCommercialCatalogRepository : ICommercialCatalogRepository
{
    public Task<IReadOnlyList<CustomerProductPriceQuote>> GetQuotesAsync(Guid id, IReadOnlyCollection<Guid> products, DateOnly date, CancellationToken token)
        => Task.FromResult<IReadOnlyList<CustomerProductPriceQuote>>([]);
    public Task<IReadOnlyList<CustomerProductPriceBook>> GetCustomerPricesAsync(Guid id, CancellationToken token, bool forUpdate = false)
        => Task.FromResult<IReadOnlyList<CustomerProductPriceBook>>([]);
    public Task<IReadOnlyList<CustomerProductPriceBook>> GetProductPricesAsync(IReadOnlyCollection<Guid> ids, CancellationToken token, bool forUpdate = false)
        => Task.FromResult<IReadOnlyList<CustomerProductPriceBook>>([]);
    public Task<CustomerProductPriceBook?> GetByPriceOperationAsync(Guid id, CancellationToken token) => Task.FromResult<CustomerProductPriceBook?>(null);
    public Task AddPriceBookAsync(CustomerProductPriceBook book, CancellationToken token) => throw new NotSupportedException();
    public Task AddPriceRevisionAsync(CustomerProductPriceBook book, CustomerProductPriceRevision revision, CancellationToken token) => throw new NotSupportedException();
    public Task<ProductConsolidationOperation?> GetConsolidationAsync(Guid id, CancellationToken token) => Task.FromResult<ProductConsolidationOperation?>(null);
    public Task AddConsolidationAsync(ProductConsolidationOperation operation, CancellationToken token) => throw new NotSupportedException();
    public Task SaveChangesAsync(CancellationToken token) => Task.CompletedTask;
}
