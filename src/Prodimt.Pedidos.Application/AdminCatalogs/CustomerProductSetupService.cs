using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.AdminOrders;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Domain.Enums;
using Prodimt.Pedidos.Domain.Services;

namespace Prodimt.Pedidos.Application.AdminCatalogs;

public sealed record AdditionalCustomerProduct(Guid ProductId, string ProductName, string UnitCode);
public sealed record ProductSetupOrderResponse(Guid OrderId, DateOnly ServiceDate, decimal? Quantity,
    string? UnitCode, string Source, DateTimeOffset RequestedAt, string? RequestedBy,
    bool IsActive, decimal? UnitPrice, bool PricePending);
public sealed record ProductSetupResponse(Guid Id, int Version, Guid CustomerId, string CustomerName,
    Guid ProductId, string ProductName, string UnitCode, bool IsResolved, bool IsConfigured,
    DateTimeOffset CreatedAt, DateTimeOffset LastRequestedAt, IReadOnlyList<ProductSetupOrderResponse> Orders);
public sealed record ProductSetupPricePreview(Guid OrderId, int OrderVersion, Guid? OrderLineId,
    DateOnly ServiceDate, decimal? Quantity, string? UnitCode, bool IsActive,
    decimal? CurrentPrice, decimal? ProposedPrice, Guid? RevisionId, DateOnly? EffectiveFrom, bool ApplyPrice);
public sealed record ProductSetupResolutionPreview(Guid Id, int Version, string Fingerprint, bool CanComplete,
    IReadOnlyList<string> Blockers, IReadOnlyList<ProductSetupPricePreview> Lines);
public sealed record CompleteProductSetupRequest(Guid OperationId, int ExpectedVersion, string PreviewFingerprint, string Reason);
public sealed record CompleteProductSetupResponse(Guid Id, int Version, DateTimeOffset ResolvedAt, int PricedLines, bool Replayed = false);

public sealed class CustomerProductSetupService(ICustomerRepository customers, IProductRepository products,
    ICustomerProductSetupRepository setups, ICommercialCatalogRepository commercial,
    IOrderAuditLogRepository orderAudit, IAuditLogRepository catalogAudit, IOrderOperationStore operations,
    IDateTimeProvider clock)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public Task<int> CountOpenAsync(CancellationToken token) => setups.CountOpenAsync(token);

    public async Task<IReadOnlyList<AdditionalCustomerProduct>> GetAdditionalAsync(Guid customerId, CancellationToken token)
    {
        var customer = await customers.GetByIdAsync(customerId, token);
        if (customer is null || !customer.IsActive) throw new KeyNotFoundException("El cliente no existe o está inactivo.");
        // A disabled relationship is not a route around an explicit catalog restriction.
        var configured = (await customers.GetAllFrequentProductsAsync(customerId, token)).Select(x => x.ProductId).ToHashSet();
        return (await products.GetAllAsync(token)).Where(x => x.IsActive && x.MergedIntoProductId is null
            && x.UnitCode is not null && ProductUnitCodes.IsSupported(x.UnitCode) && !configured.Contains(x.Id))
            .OrderBy(x => x.Name).Select(x => new AdditionalCustomerProduct(x.Id, x.Name, x.UnitCode!)).ToArray();
    }

    public async Task<IReadOnlyList<ResolvedCaptureLine>> ResolveManualAsync(CustomerOrderingProfileSnapshot profile,
        IReadOnlyList<ManualOrderCaptureLine> lines, CancellationToken token)
    {
        if (lines.Any(x => x is null || x.Quantity <= 0 || x.Quantity > 9999999999999999.99m
            || decimal.Round(x.Quantity, 2) != x.Quantity || x.Notes?.Length > 500)
            || lines.Select(x => x.ProductId).Distinct().Count() != lines.Count)
            throw new ArgumentException("Captura productos únicos con cantidades positivas de hasta dos decimales.");
        var profileItems = profile.Items.ToDictionary(x => x.ProductId);
        var catalog = (await products.GetByIdsAsync(lines.Select(x => x.ProductId), token)).ToDictionary(x => x.Id);
        return lines.Select(line =>
        {
            if (!catalog.TryGetValue(line.ProductId, out var product) || !product.IsActive || product.MergedIntoProductId is not null
                || product.UnitCode is null || !ProductUnitCodes.IsSupported(product.UnitCode))
                throw new ArgumentException("El producto debe estar activo en el catálogo y tener unidad confirmada.");
            if (profileItems.TryGetValue(line.ProductId, out var item) && (!item.IsActive || !item.ProductIsActive))
                throw new ArgumentException("Este producto está inactivo en la configuración del cliente.");
            if (product.UnitCode != line.ExpectedUnitCode)
                throw new CatalogConflictException("La unidad de un producto cambió. Revisa sus cantidades antes de confirmar.");
            return new ResolvedCaptureLine(product.Id, product.Name, line.Quantity, product.UnitCode,
                item?.InterpretationPosition ?? 0, item?.CaptureAlias);
        }).ToArray();
    }

    // Called inside the same transaction/receipt as capture. Opening a picker is read-only.
    public async Task RecordAsync(Order order, CustomerOrderingProfileSnapshot profile, AuditActorType actorType,
        string? actorId, string? actorName, CancellationToken token)
    {
        var configured = profile.Items.Select(x => x.ProductId).ToHashSet();
        foreach (var line in order.Lines)
        {
            var request = await setups.GetByPairAsync(order.CustomerId!.Value, line.ProductId, token);
            if (configured.Contains(line.ProductId) && (request is null || request.IsResolved)) continue;
            if (request is null)
            {
                request = new CustomerProductSetupRequest { Id = Guid.NewGuid(), CustomerId = order.CustomerId.Value,
                    ProductId = line.ProductId, ProductNameSnapshot = line.ProductNameSnapshot!, UnitCode = line.UnitCode!,
                    CreatedAt = order.SubmittedAt, LastRequestedAt = order.SubmittedAt };
                await setups.AddAsync(request, token);
            }
            else if (request.Orders.Any(x => x.OrderId == order.Id)) continue;
            else request.Version = checked(request.Version + 1);
            request.IsResolved = false; request.ResolvedAt = null; request.ResolvedBy = null; request.ResolvedByName = null;
            request.LastRequestedAt = order.SubmittedAt;
            request.Orders.Add(new CustomerProductSetupOrder { SetupRequestId = request.Id, OrderId = order.Id,
                Source = actorType == AuditActorType.Customer ? "Customer" : "Admin", RequestedAt = order.SubmittedAt,
                ActorId = actorId, ActorDisplayName = actorName });
            await orderAudit.AddAsync(OrderAuditLog.Create(order, OrderAuditEventType.CustomerProductSetupRequested,
                order.SubmittedAt, actorType, "Producto agregado al pedido; configuración comercial pendiente.", actorId,
                actorName, JsonSerializer.Serialize(new { setupRequestId = request.Id, line.ProductId,
                    line.Quantity, line.UnitCode, pricePending = line.UnitPriceSnapshot is null }, JsonOptions)), token);
        }
        await setups.SaveChangesAsync(token);
    }

    public async Task<IReadOnlyList<ProductSetupResponse>> GetOpenAsync(CancellationToken token)
    {
        var requests = await setups.GetOpenAsync(token);
        var customerLookup = (await customers.GetByIdsAsync(requests.Select(x => x.CustomerId), token)).ToDictionary(x => x.Id);
        var productLookup = (await products.GetByIdsIncludingInactiveAsync(requests.Select(x => x.ProductId), token)).ToDictionary(x => x.Id);
        var orders = (await setups.GetOrdersAsync(requests.SelectMany(x => x.Orders).Select(x => x.OrderId).Distinct().ToArray(), token)).ToDictionary(x => x.Id);
        var result = new List<ProductSetupResponse>();
        foreach (var request in requests)
        {
            var configured = (await customers.GetFrequentProductsAsync(request.CustomerId, token)).Any(x => x.ProductId == request.ProductId);
            result.Add(Map(request, customerLookup.GetValueOrDefault(request.CustomerId)?.Name ?? "Cliente",
                productLookup.GetValueOrDefault(request.ProductId)?.Name ?? request.ProductNameSnapshot, configured, orders));
        }
        return result;
    }

    public async Task<ProductSetupResponse> GetAsync(Guid id, CancellationToken token)
    {
        var request = await RequiredAsync(id, token);
        var customer = await customers.GetByIdAsync(request.CustomerId, token);
        var product = (await products.GetByIdsIncludingInactiveAsync([request.ProductId], token)).SingleOrDefault();
        var orders = (await setups.GetOrdersAsync(request.Orders.Select(x => x.OrderId).ToArray(), token)).ToDictionary(x => x.Id);
        var configured = (await customers.GetFrequentProductsAsync(request.CustomerId, token)).Any(x => x.ProductId == request.ProductId);
        return Map(request, customer?.Name ?? "Cliente", product?.Name ?? request.ProductNameSnapshot, configured, orders);
    }

    public async Task<ProductSetupResolutionPreview> PreviewAsync(Guid id, CancellationToken token)
        => await BuildPreviewAsync(await RequiredAsync(id, token), token);

    public async Task<CompleteProductSetupResponse> CompleteAsync(Guid id, CompleteProductSetupRequest request,
        AdminActorContext actor, CancellationToken token)
    {
        if (request.OperationId == Guid.Empty || request.ExpectedVersion < 1
            || request.PreviewFingerprint?.Length != 64 || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 300)
            throw new ArgumentException("Revisa la configuración e indica un motivo de hasta 300 caracteres.");
        var receipt = await operations.ExecuteAsync($"PRODUCT-SETUP:{id:N}:{request.OperationId:N}",
            Hash(new { id, request, actor.ActorId }), clock.Now,
            ct => customers.ExecuteOrderingProfileUpdateAsync(async inner =>
            {
                var setup = await RequiredAsync(id, inner);
                var preview = await BuildPreviewAsync(setup, inner);
                if (setup.Version != request.ExpectedVersion || preview.Fingerprint != request.PreviewFingerprint)
                    throw new CatalogConflictException("La configuración, tarifa o pedido cambió. Vuelve a revisar la vista previa.");
                if (!preview.CanComplete) throw new ArgumentException(string.Join(" ", preview.Blockers));
                var current = (await setups.GetOrdersAsync(preview.Lines.Where(x => x.ApplyPrice).Select(x => x.OrderId).ToArray(), inner)).ToDictionary(x => x.Id);
                var now = clock.Now;
                foreach (var line in preview.Lines.Where(x => x.ApplyPrice))
                {
                    var order = current[line.OrderId];
                    var target = order.Lines.Single(x => x.Id == line.OrderLineId && x.ProductId == setup.ProductId);
                    // A real zero is already a price. Never reprice a priced historical line.
                    if (target.UnitPriceSnapshot is not null) throw new CatalogConflictException("La partida ya recibió un precio. Revisa de nuevo.");
                    target.UnitPriceSnapshot = line.ProposedPrice; target.CurrencyCodeSnapshot = "MXN";
                    target.PriceRevisionId = line.RevisionId; target.PriceEffectiveFrom = line.EffectiveFrom;
                    order.Version = checked(order.Version + 1);
                    await orderAudit.AddAsync(OrderAuditLog.Create(order, OrderAuditEventType.OrderLineInitialPriceResolved,
                        now, AuditActorType.Admin, "Precio inicial pendiente completado con tarifa por fecha de servicio.",
                        actor.ActorId, actor.ActorDisplayName, JsonSerializer.Serialize(new { setupRequestId = id,
                            orderLineId = target.Id, setup.ProductId, before = (decimal?)null, after = line.ProposedPrice,
                            line.UnitCode, line.RevisionId, line.EffectiveFrom, reason = request.Reason.Trim(),
                            request.OperationId }, JsonOptions)), inner);
                }
                setup.IsResolved = true; setup.ResolvedAt = now; setup.ResolvedBy = actor.ActorId;
                setup.ResolvedByName = actor.ActorDisplayName; setup.Version = checked(setup.Version + 1);
                await catalogAudit.AddAsync(CatalogAudit.Create("CustomerProductSetup", id, "CustomerProductSetupCompleted",
                    now, actor, "Configuración de producto revisada; precios pendientes resueltos.",
                    new { setup.CustomerId, setup.ProductId, request.OperationId, pricedLines = preview.Lines.Count(x => x.ApplyPrice) }), inner);
                await setups.SaveChangesAsync(inner);
                return new CompleteProductSetupResponse(id, setup.Version, now, preview.Lines.Count(x => x.ApplyPrice));
            }, ct), token);
        return receipt.Result with { Replayed = receipt.Replayed };
    }

    private async Task<ProductSetupResolutionPreview> BuildPreviewAsync(CustomerProductSetupRequest request, CancellationToken token)
    {
        var customer = await customers.GetByIdAsync(request.CustomerId, token);
        var product = (await products.GetByIdsIncludingInactiveAsync([request.ProductId], token)).SingleOrDefault();
        var profile = await customers.GetOrderingProfileSnapshotAsync(request.CustomerId, token);
        var configured = profile?.Items.Any(x => x.ProductId == request.ProductId && x.IsActive && x.ProductIsActive) == true;
        var blockers = new List<string>();
        if (request.IsResolved) blockers.Add("La configuración ya fue completada.");
        if (customer is null || !customer.IsActive) blockers.Add("El cliente está inactivo.");
        if (product is null || !product.IsActive || product.MergedIntoProductId is not null || product.UnitCode is null)
            blockers.Add("Revisa el estado y la unidad del producto en el catálogo.");
        if (!configured) blockers.Add("Guarda primero el producto activo en la configuración del cliente.");
        var orders = await setups.GetOrdersAsync(request.Orders.Select(x => x.OrderId).ToArray(), token);
        var lines = new List<ProductSetupPricePreview>();
        foreach (var order in orders.OrderBy(x => x.Id))
        {
            var line = order.Lines.SingleOrDefault(x => x.ProductId == request.ProductId);
            var active = IsActive(order) && line is { Quantity: > 0 };
            var quote = active ? (await commercial.GetQuotesAsync(request.CustomerId, [request.ProductId], order.OrderDate, token)).SingleOrDefault() : null;
            var applicable = quote is { UnitPrice: not null } && line!.UnitCode == quote.UnitCode && product?.UnitCode == line.UnitCode;
            if (active && !applicable) blockers.Add($"Falta tarifa vigente con la misma unidad para la entrega {order.OrderDate:yyyy-MM-dd}.");
            lines.Add(new(order.Id, order.Version, line?.Id, order.OrderDate, line?.Quantity, line?.UnitCode, active,
                line?.UnitPriceSnapshot, applicable ? quote!.UnitPrice : null, applicable ? quote!.RevisionId : null,
                applicable ? quote!.EffectiveFrom : null, active && line!.UnitPriceSnapshot is null && applicable));
        }
        if (!lines.Any(x => x.IsActive))
        {
            var quote = (await commercial.GetQuotesAsync(request.CustomerId, [request.ProductId], clock.Today, token)).SingleOrDefault();
            if (quote?.UnitPrice is null || quote.UnitCode != product?.UnitCode)
                blockers.Add("Define una tarifa vigente para completar la configuración, aunque los pedidos ya no estén activos.");
        }
        var unique = blockers.Distinct().ToArray();
        var fingerprint = Hash(new { request.Id, request.Version, request.IsResolved, customerActive = customer?.IsActive,
            productActive = product?.IsActive, merged = product?.MergedIntoProductId, product?.UnitCode,
            profileVersion = profile?.OrderingProfileVersion, configured, lines, blockers = unique });
        return new(request.Id, request.Version, fingerprint, unique.Length == 0, unique, lines);
    }

    private async Task<CustomerProductSetupRequest> RequiredAsync(Guid id, CancellationToken token)
        => await setups.GetAsync(id, token) ?? throw new KeyNotFoundException("No se encontró la configuración pendiente.");
    private static bool IsActive(Order order) => order.Status is OrderStatus.Submitted or OrderStatus.Accepted or OrderStatus.PendingAdminReview;
    private static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, JsonOptions)))).ToLowerInvariant();
    private static ProductSetupResponse Map(CustomerProductSetupRequest request, string customerName, string productName,
        bool configured, IReadOnlyDictionary<Guid, Order> orders)
        => new(request.Id, request.Version, request.CustomerId, customerName, request.ProductId, productName, request.UnitCode,
            request.IsResolved, configured, request.CreatedAt, request.LastRequestedAt, request.Orders.OrderByDescending(x => x.RequestedAt).Select(link =>
            {
                var order = orders.GetValueOrDefault(link.OrderId);
                var line = order?.Lines.SingleOrDefault(x => x.ProductId == request.ProductId);
                var active = order is not null && IsActive(order) && line is { Quantity: > 0 };
                return new ProductSetupOrderResponse(link.OrderId, order?.OrderDate ?? DateOnly.MinValue, line?.Quantity,
                    line?.UnitCode, link.Source, link.RequestedAt, link.ActorDisplayName, active, line?.UnitPriceSnapshot,
                    active && line!.UnitPriceSnapshot is null);
            }).ToArray());
}
