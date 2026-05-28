using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.AdminOrders;
using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Application.AdminCatalogs;

public sealed class AdminProductCatalogService(
    IProductRepository products,
    IAuditLogRepository auditLogs,
    IDateTimeProvider dateTimeProvider)
{
    public async Task<IReadOnlyList<AdminProductResponse>> GetAllAsync(CancellationToken cancellationToken)
    {
        var allProducts = await products.GetAllAsync(cancellationToken);
        return allProducts.Select(MapProduct).ToArray();
    }

    public async Task<AdminProductResponse> GetByIdAsync(Guid productId, CancellationToken cancellationToken)
    {
        var product = await GetRequiredProductAsync(productId, cancellationToken);
        return MapProduct(product);
    }

    public async Task<AdminProductResponse> CreateAsync(
        UpsertAdminProductRequest request,
        AdminActorContext? actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await EnsureNoActiveNameDuplicateAsync(request.Name, excludingProductId: null, cancellationToken);

        var product = Product.Create(request.Name, request.Description);

        await products.AddAsync(product, cancellationToken);
        await auditLogs.AddAsync(CatalogAudit.Create(
            CatalogEntityTypes.Product,
            product.Id,
            CatalogAuditEventTypes.ProductCreated,
            dateTimeProvider.Now,
            actor,
            $"Producto creado: {product.Name}."), cancellationToken);
        await products.SaveChangesAsync(cancellationToken);

        return MapProduct(product);
    }

    public async Task<AdminProductResponse> UpdateAsync(
        Guid productId,
        UpsertAdminProductRequest request,
        AdminActorContext? actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var product = await GetRequiredProductForUpdateAsync(productId, cancellationToken);

        if (product.IsActive)
        {
            await EnsureNoActiveNameDuplicateAsync(request.Name, product.Id, cancellationToken);
        }

        product.Update(request.Name, request.Description);
        await auditLogs.AddAsync(CatalogAudit.Create(
            CatalogEntityTypes.Product,
            product.Id,
            CatalogAuditEventTypes.ProductUpdated,
            dateTimeProvider.Now,
            actor,
            $"Producto actualizado: {product.Name}."), cancellationToken);
        await products.SaveChangesAsync(cancellationToken);

        return MapProduct(product);
    }

    public async Task<AdminProductResponse> ActivateAsync(
        Guid productId,
        AdminActorContext? actor,
        CancellationToken cancellationToken)
    {
        var product = await GetRequiredProductForUpdateAsync(productId, cancellationToken);
        await EnsureNoActiveNameDuplicateAsync(product.Name, product.Id, cancellationToken);

        product.Activate();
        await auditLogs.AddAsync(CatalogAudit.Create(
            CatalogEntityTypes.Product,
            product.Id,
            CatalogAuditEventTypes.ProductActivated,
            dateTimeProvider.Now,
            actor,
            $"Producto activado: {product.Name}."), cancellationToken);
        await products.SaveChangesAsync(cancellationToken);

        return MapProduct(product);
    }

    public async Task<AdminProductResponse> DeactivateAsync(
        Guid productId,
        AdminActorContext? actor,
        CancellationToken cancellationToken)
    {
        var product = await GetRequiredProductForUpdateAsync(productId, cancellationToken);
        product.Deactivate();
        await auditLogs.AddAsync(CatalogAudit.Create(
            CatalogEntityTypes.Product,
            product.Id,
            CatalogAuditEventTypes.ProductDeactivated,
            dateTimeProvider.Now,
            actor,
            $"Producto desactivado: {product.Name}."), cancellationToken);
        await products.SaveChangesAsync(cancellationToken);

        return MapProduct(product);
    }

    private async Task<Product> GetRequiredProductAsync(Guid productId, CancellationToken cancellationToken)
    {
        var product = await products.GetByIdAsync(productId, cancellationToken);

        if (product is null)
        {
            throw new InvalidOperationException("Product was not found.");
        }

        return product;
    }

    private async Task<Product> GetRequiredProductForUpdateAsync(Guid productId, CancellationToken cancellationToken)
    {
        var product = await products.GetByIdForUpdateAsync(productId, cancellationToken);

        if (product is null)
        {
            throw new InvalidOperationException("Product was not found.");
        }

        return product;
    }

    private async Task EnsureNoActiveNameDuplicateAsync(
        string name,
        Guid? excludingProductId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("El nombre del producto es obligatorio.", nameof(name));
        }

        var duplicate = await products.GetActiveByExactNameAsync(name, cancellationToken);

        if (duplicate is not null && duplicate.Id != excludingProductId)
        {
            throw new ArgumentException("Ya existe un producto activo con ese nombre.", nameof(name));
        }
    }

    private static AdminProductResponse MapProduct(Product product)
    {
        return new AdminProductResponse(product.Id, product.Name, product.ExternalCode, product.Description, product.IsActive);
    }
}
