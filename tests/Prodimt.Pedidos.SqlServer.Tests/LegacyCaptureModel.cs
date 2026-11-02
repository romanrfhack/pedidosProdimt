using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Infrastructure.Persistence;

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
        builder.Entity<AdminUser>().Ignore(x => x.IsDeleted);
        builder.Entity<AdminUser>().Ignore(x => x.SessionRevision);
        builder.Ignore<SecurityRole>(); builder.Ignore<SecurityPermission>();
        builder.Ignore<SecurityUserRole>(); builder.Ignore<SecurityRolePermission>();
        builder.Ignore<SecurityUserPermission>(); builder.Ignore<SecurityState>();
    }
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

// Test-only authorization adapter for the deliberately pinned eleven-migration
// repair fixture. Never registered by the product or by current-schema tests.
public sealed class LegacySecurityStore(PedidosDbContext db) : Prodimt.Pedidos.Application.Security.ISecurityStore
{
    public async Task<IReadOnlyList<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (!await db.AdminUsers.AsNoTracking().AnyAsync(x => x.Id == userId && x.IsActive, cancellationToken)) return [];
        return Prodimt.Pedidos.Application.Security.SecurityDefaults.Permissions.Select(x => x.Key).ToArray();
    }
    public Task<Prodimt.Pedidos.Application.Security.SecurityData> ReadAsync(CancellationToken cancellationToken)
        => throw new NotSupportedException("Historical repair fixture has no I18 administration.");
    public Task<T> ExecuteAsync<T>(Guid expectedRevision, Func<Prodimt.Pedidos.Application.Security.SecurityData, T> mutation, CancellationToken cancellationToken)
        => throw new NotSupportedException("Historical repair fixture has no I18 administration.");
}
