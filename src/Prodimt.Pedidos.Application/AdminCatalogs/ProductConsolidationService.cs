using System.Globalization;
using System.Text;
using System.Text.Json;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.AdminOrders;
using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Application.AdminCatalogs;

public sealed record ConsolidationProduct(Guid Id, string Name, string? UnitCode, string? Description = null);
public sealed record ProductDuplicateGroup(string SuggestedName, Guid SuggestedTargetId, IReadOnlyList<ConsolidationProduct> Products);
public sealed record PreviewProductConsolidationRequest(Guid TargetProductId, IReadOnlyList<Guid> SourceProductIds,
    string SharedName, string? Description = null);
public sealed record ApplyProductConsolidationRequest(Guid TargetProductId, IReadOnlyList<Guid> SourceProductIds,
    string SharedName, string? Description, string Fingerprint, string Reason, Guid OperationId);
public sealed record ConsolidationCustomer(Guid CustomerId, string Name, int ProfileVersion);
public sealed record ProductConsolidationPreview(Guid TargetProductId, string SharedName, string? UnitCode,
    IReadOnlyList<ConsolidationProduct> Sources, IReadOnlyList<ConsolidationCustomer> Customers,
    int PriceAssignments, string Fingerprint, IReadOnlyList<string> Errors)
{
    public bool CanApply => Errors.Count == 0;
}
public sealed record ProductConsolidationResponse(Guid OperationId, Guid TargetProductId, string SharedName,
    int ArchivedProducts, int UpdatedProfiles, int PriceAssignments, bool Replayed = false);

public sealed class ProductConsolidationService(IProductRepository products, ICustomerRepository customers,
    ICommercialCatalogRepository commercial, IAuditLogRepository audit, IDateTimeProvider clock, ICustomerProductSetupRepository? setups = null)
{
    private sealed record Profile(Customer Customer, IReadOnlyList<CustomerFrequentProduct> Items);
    private sealed record Plan(ProductConsolidationPreview Preview, Product[] Products, Profile[] Profiles,
        IReadOnlyList<CustomerProductPriceBook> Books);
    private sealed record Receipt(ProductConsolidationResponse Response, object? Before = null, string? Reason = null);

    public async Task<IReadOnlyList<ProductDuplicateGroup>> GetCandidatesAsync(CancellationToken token)
    {
        var customerNames = (await customers.GetAllAsync(token)).Select(x => Normalize(x.Name)).ToHashSet();
        var catalog = await products.GetAllAsync(token);
        string BaseName(Product product)
        {
            var name = product.Name.Trim();
            var start = name.LastIndexOf(" (", StringComparison.Ordinal);
            return start > 0 && name.EndsWith(')') && customerNames.Contains(Normalize(name[(start + 2)..^1]))
                ? name[..start].Trim() : name;
        }
        return catalog.Where(x => x.IsActive && x.MergedIntoProductId is null && x.UnitCode is not null)
            .GroupBy(x => (Name: Normalize(BaseName(x)), x.UnitCode)).Where(x => x.Count() > 1)
            .Select(group =>
            {
                var rows = group.OrderBy(x => x.Name.Length).ThenBy(x => x.Id).ToArray();
                return new ProductDuplicateGroup(BaseName(rows[0]), rows[0].Id,
                    rows.Select(x => new ConsolidationProduct(x.Id, x.Name, x.UnitCode, x.Description)).ToArray());
            }).OrderBy(x => x.SuggestedName).ToArray();
    }

    public Task<ProductConsolidationPreview> PreviewAsync(PreviewProductConsolidationRequest request, CancellationToken token)
        => products.ExecuteCatalogUpdateAsync(async ct => (await BuildAsync(request, ct)).Preview, token);

    public async Task<ProductConsolidationResponse> ApplyAsync(ApplyProductConsolidationRequest request,
        AdminActorContext? actor, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.SourceProductIds is null || request.SharedName is null || request.OperationId == Guid.Empty || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 500
            || string.IsNullOrWhiteSpace(actor?.ActorId))
            throw new ArgumentException("Indica referencia, motivo e identidad administrativa para consolidar.");
        var hash = CommercialHash.Of(new { request.TargetProductId,
            sources = request.SourceProductIds.Order().ToArray(), name = request.SharedName.Trim(),
            description = Clean(request.Description), request.Fingerprint, reason = request.Reason.Trim() });
        try
        {
            return await products.ExecuteCatalogUpdateAsync(async ct =>
            {
                if (await ReplayAsync(request.OperationId, hash, ct) is { } replay) return replay;
                var plan = await BuildAsync(new(request.TargetProductId, request.SourceProductIds,
                    request.SharedName, request.Description), ct);
                if (plan.Preview.Fingerprint != request.Fingerprint)
                    throw new CatalogConflictException("Productos, configuraciones o precios cambiaron. Revisa otra vista previa.");
                if (!plan.Preview.CanApply) throw new ArgumentException(string.Join(" ", plan.Preview.Errors));
                var before = new
                {
                    products = plan.Products.Select(x => new { x.Id, x.Name, x.Description, x.ExternalCode, x.UnitCode, x.IsActive }).ToArray(),
                    profiles = plan.Profiles.Select(x => new { x.Customer.Id, x.Customer.OrderingProfileVersion,
                        x.Customer.IsOrderProfileApproved, items = x.Items }).ToArray(),
                    prices = plan.Books.Select(x => new { x.Id, x.CustomerId, x.ProductId, x.Version }).ToArray()
                };
                var sourceIds = request.SourceProductIds.ToHashSet();
                var nameChanged = plan.Products.Single(x => x.Id == request.TargetProductId).Name != request.SharedName.Trim();
                var changedProfiles = plan.Profiles.Where(x => nameChanged || x.Items.Any(item => sourceIds.Contains(item.ProductId))).ToArray();
                foreach (var product in plan.Products)
                {
                    if (product.Id == request.TargetProductId) product.Update(request.SharedName, request.Description);
                    else { product.MergedIntoProductId = request.TargetProductId; product.Deactivate(); }
                }
                foreach (var profile in changedProfiles)
                {
                    var replacement = profile.Items.Select(x => new CustomerFrequentProduct
                    {
                        CustomerId = x.CustomerId, ProductId = sourceIds.Contains(x.ProductId) ? request.TargetProductId : x.ProductId,
                        DefaultQuantity = x.DefaultQuantity, SortOrder = x.SortOrder, InterpretationPosition = x.InterpretationPosition,
                        CaptureAlias = x.CaptureAlias, PreferredMachineId = x.PreferredMachineId, IsActive = x.IsActive,
                        UseCustomerDefaultMachine = x.UseCustomerDefaultMachine
                    }).ToArray();
                    await customers.ReplaceFrequentProductsAsync(profile.Customer.Id, replacement, ct);
                    profile.Customer.RecordOrderingProfileChange(false, clock.Now);
                }
                foreach (var book in plan.Books) book.ProductId = request.TargetProductId;
                var response = new ProductConsolidationResponse(request.OperationId, request.TargetProductId,
                    request.SharedName.Trim(), sourceIds.Count, changedProfiles.Length, plan.Books.Count);
                await commercial.AddConsolidationAsync(new ProductConsolidationOperation
                {
                    Id = request.OperationId, PayloadHash = hash, CompletedAt = clock.Now,
                    ResultJson = JsonSerializer.Serialize(new Receipt(response, before, request.Reason.Trim()), CommercialHash.JsonOptions)
                }, ct);
                await audit.AddAsync(CatalogAudit.Create("ProductConsolidation", request.OperationId, "ProductsConsolidated",
                    clock.Now, actor, "Productos consolidados; pedidos historicos y revisiones de precio conservados.",
                    new { request.TargetProductId, response.SharedName, response.ArchivedProducts,
                        // Full reason/evidence remain in the immutable operation receipt.
                        response.UpdatedProfiles, response.PriceAssignments }), ct);
                // Profile replacements and every other tracked change share this SQL transaction/commit.
                await customers.SaveChangesAsync(ct);
                await commercial.SaveChangesAsync(ct);
                return response;
            }, token);
        }
        catch (CatalogConflictException)
        {
            if (await ReplayAsync(request.OperationId, hash, token) is { } replay) return replay;
            throw;
        }
    }

    private async Task<Plan> BuildAsync(PreviewProductConsolidationRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.SourceProductIds is null || request.SourceProductIds.Count is < 1 or > 100 || request.TargetProductId == Guid.Empty
            || request.SourceProductIds.Any(x => x == Guid.Empty || x == request.TargetProductId)
            || request.SourceProductIds.Distinct().Count() != request.SourceProductIds.Count)
            throw new ArgumentException("Selecciona un destino y de uno a cien productos distintos para consolidar.");
        if (string.IsNullOrWhiteSpace(request.SharedName) || request.SharedName.Trim().Length > 120 || request.Description?.Length > 500)
            throw new ArgumentException("Indica nombre compartido (hasta 120 caracteres) y descripcion opcional de hasta 500.");
        var ids = request.SourceProductIds.Append(request.TargetProductId).Order().ToArray();
        var selected = new List<Product>();
        foreach (var id in ids)
            selected.Add(await products.GetByIdForUpdateAsync(id, token) ?? throw new ArgumentException("Un producto ya no existe."));
        var target = selected.Single(x => x.Id == request.TargetProductId);
        var errors = new List<string>();
        if (setups is not null && (await setups.GetOpenAsync(token)).Any(x => ids.Contains(x.ProductId)))
            errors.Add("Completa primero las configuraciones pendientes de estos productos antes de consolidarlos.");
        if (selected.Any(x => !x.IsActive || x.MergedIntoProductId is not null)) errors.Add("Selecciona solamente productos activos sin consolidar.");
        if (target.UnitCode is null || !ProductUnitCodes.IsSupported(target.UnitCode) || selected.Any(x => x.UnitCode != target.UnitCode))
            errors.Add("Todos los productos deben tener la misma unidad confirmada. No se convierten kilos, piezas o paquetes.");
        var duplicate = (await products.GetAllAsync(token)).FirstOrDefault(x => x.IsActive
            && !ids.Contains(x.Id) && Normalize(x.Name) == Normalize(request.SharedName));
        if (duplicate is not null) errors.Add("Ya existe ese nombre: incluye ese producto como destino.");
        var affected = new Dictionary<Guid, Customer>();
        foreach (var product in selected)
            foreach (var customer in await customers.GetCustomersReferencingProductForUpdateAsync(product.Id, token))
                affected[customer.Id] = customer;
        var profiles = new List<Profile>();
        foreach (var customer in affected.Values.OrderBy(x => x.Id))
        {
            var items = await customers.GetAllFrequentProductsAsync(customer.Id, token);
            if (items.Count(x => ids.Contains(x.ProductId)) > 1)
                errors.Add($"{customer.Name}: tiene mas de uno de estos productos. Revisa sus posiciones antes de consolidar.");
            profiles.Add(new(customer, items));
        }
        var books = await commercial.GetProductPricesAsync(ids, token, true);
        foreach (var collision in books.GroupBy(x => x.CustomerId).Where(x => x.Count() > 1))
            errors.Add($"El cliente {collision.Key} tiene historiales de precio para mas de un producto seleccionado; no se fusionan automaticamente.");
        var fingerprint = CommercialHash.Of(new
        {
            request.TargetProductId, name = request.SharedName.Trim(), description = Clean(request.Description),
            products = selected.OrderBy(x => x.Id).Select(x => new { x.Id, x.Name, x.Description, x.UnitCode, x.ExternalCode, x.IsActive, x.MergedIntoProductId }),
            profiles = profiles.Select(x => new { x.Customer.Id, x.Customer.OrderingProfileVersion,
                x.Customer.IsOrderProfileApproved, items = x.Items.OrderBy(i => i.ProductId) }),
            prices = books.OrderBy(x => x.Id).Select(x => new { x.Id, x.CustomerId, x.ProductId, x.Version })
        });
        return new(new(request.TargetProductId, request.SharedName.Trim(), target.UnitCode,
            selected.Where(x => x.Id != target.Id).Select(x => new ConsolidationProduct(x.Id, x.Name, x.UnitCode, x.Description)).ToArray(),
            profiles.Where(x => target.Name != request.SharedName.Trim() || x.Items.Any(i => request.SourceProductIds.Contains(i.ProductId)))
                .Select(x => new ConsolidationCustomer(x.Customer.Id, x.Customer.Name, x.Customer.OrderingProfileVersion)).ToArray(),
            books.Count, fingerprint, errors), selected.ToArray(), profiles.ToArray(), books);
    }

    private async Task<ProductConsolidationResponse?> ReplayAsync(Guid id, string hash, CancellationToken token)
    {
        var receipt = await commercial.GetConsolidationAsync(id, token);
        if (receipt is null) return null;
        if (receipt.PayloadHash != hash) throw new CatalogConflictException("La referencia de consolidacion ya tiene otro contenido.");
        return JsonSerializer.Deserialize<Receipt>(receipt.ResultJson, CommercialHash.JsonOptions)!.Response with { Replayed = true };
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string Normalize(string value) => string.Join(' ', new string(value.Normalize(NormalizationForm.FormD)
        .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray())
        .ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
