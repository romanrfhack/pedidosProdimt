using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.AdminOrders;
using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Application.AdminCatalogs;

public sealed record ChangeCustomerProductPriceRequest(decimal? UnitPrice, DateOnly EffectiveFrom,
    string Reason, int ExpectedVersion, Guid OperationId, string ExpectedUnitCode);

public sealed record CustomerProductPriceRevisionResponse(Guid Id, int Version, decimal? UnitPrice,
    string CurrencyCode, string UnitCode, string ProductName, DateOnly EffectiveFrom,
    DateOnly? EffectiveUntil, DateTimeOffset RecordedAt, string ActorId, string? ActorDisplayName,
    string Reason, Guid OperationId, bool SupersededForDate = false);

public sealed record CustomerProductPriceResponse(Guid CustomerId, Guid ProductId, string ProductName,
    string? UnitCode, int Version, DateOnly AsOfDate, decimal? UnitPrice, string CurrencyCode,
    Guid? AppliedRevisionId, IReadOnlyList<CustomerProductPriceRevisionResponse> History, bool Replayed = false, bool Unchanged = false);

internal static class CommercialHash
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public static string Of<T>(T value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(value, JsonOptions)))).ToLowerInvariant();
}

public sealed class CustomerProductPricingService(ICustomerRepository customers, IProductRepository products,
    ICommercialCatalogRepository commercial, IAuditLogRepository audit, IDateTimeProvider clock)
{
    public async Task<IReadOnlyList<CustomerProductPriceResponse>> GetAsync(Guid customerId, DateOnly? asOf, CancellationToken token)
    {
        _ = await customers.GetByIdAsync(customerId, token) ?? throw new KeyNotFoundException("El cliente no existe.");
        var books = await commercial.GetCustomerPricesAsync(customerId, token);
        var configured = await customers.GetAllFrequentProductsAsync(customerId, token);
        var ids = configured.Select(x => x.ProductId).Concat(books.Select(x => x.ProductId)).Distinct().ToArray();
        var catalog = await products.GetByIdsIncludingInactiveAsync(ids, token);
        return catalog.OrderBy(x => x.Name).Select(product => Map(customerId, product,
            books.SingleOrDefault(x => x.ProductId == product.Id), asOf ?? clock.Today)).ToArray();
    }

    public async Task<CustomerProductPriceResponse> ChangeAsync(Guid customerId, Guid productId,
        ChangeCustomerProductPriceRequest request, AdminActorContext? actor, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.OperationId == Guid.Empty || request.ExpectedVersion < 0)
            throw new ArgumentException("Indica referencia y version esperada del precio.");
        var hash = CommercialHash.Of(new { customerId, productId,
            price = request.UnitPrice?.ToString("0.####", CultureInfo.InvariantCulture), request.EffectiveFrom,
            reason = request.Reason?.Trim(), request.ExpectedVersion, request.ExpectedUnitCode });
        try
        {
            return await products.ExecuteCatalogUpdateAsync(async ct =>
            {
                if (await ReplayAsync(request.OperationId, hash, ct) is { } replay) return replay;
                var customer = await customers.GetByIdAsync(customerId, ct);
                if (customer is null || !customer.IsActive) throw new ArgumentException("El cliente no existe o esta inactivo.");
                var product = await products.GetByIdForUpdateAsync(productId, ct);
                if (product is null || !product.IsActive || product.MergedIntoProductId is not null)
                    throw new ArgumentException("Selecciona el producto compartido activo.");
                if (product.UnitCode is null || !string.Equals(product.UnitCode, request.ExpectedUnitCode, StringComparison.Ordinal))
                    throw new CatalogConflictException("La unidad del producto cambio o esta sin confirmar. Recarga antes de fijar precio.");
                if (!(await customers.GetAllFrequentProductsAsync(customerId, ct)).Any(x => x.ProductId == productId))
                    throw new ArgumentException("Guarda primero este producto en la configuracion del cliente.");
                var book = (await commercial.GetCustomerPricesAsync(customerId, ct, true)).SingleOrDefault(x => x.ProductId == productId);
                if ((book?.Version ?? 0) != request.ExpectedVersion)
                    throw new CatalogConflictException("El precio cambio desde que lo abriste. Recarga y revisa el historial.");
                var isNew = book is null;
                book ??= new CustomerProductPriceBook { Id = Guid.NewGuid(), CustomerId = customerId, ProductId = productId };
                var previous = book.At(request.EffectiveFrom);
                var revision = CustomerProductPriceRevision.Create(book.Id, checked(book.Version + 1), request.UnitPrice,
                    product.UnitCode, product.Name, request.EffectiveFrom, clock.Now, actor?.ActorId,
                    actor?.ActorDisplayName, request.Reason ?? string.Empty, request.OperationId, hash);
                var latest = book.Revisions.OrderByDescending(x => x.Version).FirstOrDefault();
                if (latest is not null && latest.UnitPrice == revision.UnitPrice && latest.UnitCode == revision.UnitCode
                    && latest.EffectiveFrom == revision.EffectiveFrom && latest.Reason == revision.Reason)
                    return Map(customerId, product, book, clock.Today) with { Unchanged = true };
                book.Version = revision.Version;
                if (isNew) await commercial.AddPriceBookAsync(book, ct);
                await commercial.AddPriceRevisionAsync(book, revision, ct);
                await audit.AddAsync(CatalogAudit.Create("CustomerProductPrice", book.Id, "CustomerProductPriceChanged",
                    revision.RecordedAt, actor, "Precio por cliente-producto actualizado con historial.",
                    new { customerId, productId, revision.Id, revision.OperationId, revision.Version,
                        before = previous?.UnitPrice, after = revision.UnitPrice, revision.UnitCode,
                        // The complete reason belongs to the immutable revision. Repeating it
                        // in the bounded audit JSON can overflow after Unicode escaping.
                        revision.CurrencyCode, revision.EffectiveFrom }), ct);
                await commercial.SaveChangesAsync(ct);
                return Map(customerId, product, book, clock.Today);
            }, token);
        }
        catch (CatalogConflictException)
        {
            if (await ReplayAsync(request.OperationId, hash, token) is { } replay) return replay;
            throw;
        }
    }

    private async Task<CustomerProductPriceResponse?> ReplayAsync(Guid operationId, string hash, CancellationToken token)
    {
        var book = await commercial.GetByPriceOperationAsync(operationId, token);
        if (book is null) return null;
        if (book.Revisions.Single(x => x.OperationId == operationId).PayloadHash != hash)
            throw new CatalogConflictException("La referencia de cambio ya se uso con otro contenido.");
        var product = await products.GetByIdAsync(book.ProductId, token) ?? throw new KeyNotFoundException("El producto no existe.");
        return Map(book.CustomerId, product, book, clock.Today) with { Replayed = true };
    }

    private static CustomerProductPriceResponse Map(Guid customerId, Product product, CustomerProductPriceBook? book, DateOnly date)
    {
        var revision = book?.At(date);
        var applicable = revision?.UnitCode == product.UnitCode ? revision : null;
        var history = book?.Revisions.OrderByDescending(x => x.Version).Select(x =>
            new CustomerProductPriceRevisionResponse(x.Id, x.Version, x.UnitPrice, x.CurrencyCode, x.UnitCode,
                x.ProductNameSnapshot, x.EffectiveFrom,
                book.Revisions.Where(next => next.EffectiveFrom > x.EffectiveFrom)
                    .Select(next => (DateOnly?)next.EffectiveFrom.AddDays(-1)).Min(),
                x.RecordedAt, x.ActorId, x.ActorDisplayName, x.Reason, x.OperationId,
                book.Revisions.Any(next => next.EffectiveFrom == x.EffectiveFrom && next.Version > x.Version))).ToArray() ?? [];
        return new(customerId, product.Id, product.Name, product.UnitCode, book?.Version ?? 0, date,
            applicable?.UnitPrice, "MXN", applicable?.UnitPrice is not null ? applicable.Id : null, history);
    }
}
