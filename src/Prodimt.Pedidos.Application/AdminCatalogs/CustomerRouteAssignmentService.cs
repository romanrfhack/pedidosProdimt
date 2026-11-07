using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.AdminOrders;
using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Application.AdminCatalogs;

public sealed record CustomerRouteOption(string Code, string Name);
public sealed record UpdateCustomerRouteRequest(string? RouteCode, int? RouteStopOrder, DateTimeOffset? ExpectedUpdatedAt);

public sealed class CustomerRouteAssignmentService(ICustomerRepository customers, IDeliveryRepository delivery,
    IAuditLogRepository audit, IDateTimeProvider clock)
{
    public Task<IReadOnlyList<CustomerRouteAssignment>> GetAssignmentsAsync(CancellationToken token)
        => customers.GetRouteAssignmentsAsync(token);

    public async Task<IReadOnlyList<CustomerRouteOption>> GetActiveRoutesAsync(CancellationToken token)
        => (await delivery.GetRoutesAsync(token)).Where(x => x.IsActive)
            .Select(x => new CustomerRouteOption(x.Code, x.Name)).ToArray();

    public Task<CustomerRouteAssignment> UpdateAsync(Guid customerId, UpdateCustomerRouteRequest request,
        AdminActorContext actor, CancellationToken token)
        => customers.ExecuteOrderingProfileUpdateAsync(async inner =>
        {
            if (request.ExpectedUpdatedAt is not { } expected)
                throw new ArgumentException("Indica la versión de la ficha que estás editando.", nameof(request));
            var customer = await customers.GetByIdForUpdateAsync(customerId, inner)
                ?? throw new KeyNotFoundException("El cliente no existe.");
            EnsureCurrent(customer, expected);

            var route = await ResolveRouteCodeAsync(customer, request.RouteCode, delivery, inner);
            var stop = route is null ? null : request.RouteStopOrder;
            var previousRouteCode = customer.RouteCode;
            var previousRouteStopOrder = customer.RouteStopOrder;
            if (previousRouteCode != route || previousRouteStopOrder != stop)
            {
                var now = clock.Now;
                customer.SetRouteAssignment(route, stop, NextUpdateStamp(customer, now));
                await audit.AddAsync(CatalogAudit.Create(CatalogEntityTypes.Customer, customerId,
                    CatalogAuditEventTypes.CustomerRouteAssignmentChanged, now, actor, "Asignación de ruta del cliente actualizada.",
                    new { previousRouteCode, previousRouteStopOrder, customer.RouteCode, customer.RouteStopOrder }), inner);
                await customers.SaveChangesAsync(inner);
            }

            return new CustomerRouteAssignment(customer.Id, customer.Name, customer.RouteCode,
                customer.RouteStopOrder, customer.UpdatedAt, customer.IsActive);
        }, token);

    internal static async Task<string?> ResolveRouteCodeAsync(Customer customer, string? routeCode,
        IDeliveryRepository delivery, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(routeCode)) return null;
        var code = routeCode.Trim();
        if (string.Equals(customer.RouteCode?.Trim(), code, StringComparison.OrdinalIgnoreCase))
            return customer.RouteCode;

        if (code.Length > 80) throw new ArgumentException("El código de ruta admite hasta 80 caracteres.", nameof(routeCode));
        if (!customer.IsActive) throw new ArgumentException("Activa al cliente antes de asignarlo a otra ruta.", nameof(customer));
        var route = await delivery.GetRouteAsync(code.ToUpperInvariant(), token);
        if (route is not { IsActive: true })
            throw new ArgumentException("Selecciona una ruta de reparto activa.", nameof(routeCode));
        return route.Code;
    }

    internal static void EnsureCurrent(Customer customer, DateTimeOffset expected)
    {
        if (customer.UpdatedAt != expected)
            throw new CatalogConflictException("La ficha del cliente cambió. Recarga antes de guardar.");
    }

    internal static DateTimeOffset NextUpdateStamp(Customer customer, DateTimeOffset now)
        => now > customer.UpdatedAt ? now : customer.UpdatedAt.AddTicks(1);
}
