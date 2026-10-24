using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.AdminOrders;

namespace Prodimt.Pedidos.Application.AdminCatalogs;

public static class OrderingProfileInvalidation
{
    // Product identity, unit or availability changes invalidate the meaning approved for every referencing profile.
    // All repositories in a request share the same unit of work; the caller commits once with its product change.
    public static async Task InvalidateForProductAsync(
        Guid productId,
        ICustomerRepository customers,
        IAuditLogRepository auditLogs,
        DateTimeOffset now,
        AdminActorContext? actor,
        CancellationToken cancellationToken)
    {
        var affectedCustomers = await customers.GetCustomersReferencingProductForUpdateAsync(productId, cancellationToken);
        foreach (var customer in affectedCustomers)
        {
            customer.RecordOrderingProfileChange(approved: false, now);
            await auditLogs.AddAsync(CatalogAudit.Create(
                CatalogEntityTypes.Customer, customer.Id, "OrderingProfileInvalidated", now, actor,
                "Perfil pendiente de validacion por cambio en el catalogo de productos.",
                new { productId, customer.OrderingProfileVersion }), cancellationToken);
        }
    }
}
