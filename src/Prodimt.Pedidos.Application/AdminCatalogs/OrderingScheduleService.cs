using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.AdminOrders;
using Prodimt.Pedidos.Application.CustomerOrders;
using Prodimt.Pedidos.Domain.Entities;
namespace Prodimt.Pedidos.Application.AdminCatalogs;

public sealed record ScheduleUpdate(TimeOnly? Cutoff, int? ExpectedVersion = null);
public sealed record RouteScheduleResponse(string RouteCode, TimeOnly? Cutoff, int Version);
public sealed record CustomerScheduleResponse(Guid CustomerId, string CustomerName, string? RouteCode, TimeOnly? Cutoff, int Version);
public sealed record OrderingSchedulesResponse(IReadOnlyList<RouteScheduleResponse> Routes, IReadOnlyList<CustomerScheduleResponse> Customers);
public sealed class OrderingScheduleService(IOrderingScheduleRepository schedules, ICustomerRepository customers,
    IAuditLogRepository audit, IDateTimeProvider clock)
{
    public async Task<OrderingSchedulesResponse> GetAsync(CancellationToken token)
    {
        var clients = await customers.GetAllAsync(token);
        var configured = (await schedules.GetAllAsync(token)).ToDictionary(x => x.RouteCode, StringComparer.OrdinalIgnoreCase);
        var routes = clients.Select(x => x.RouteCode).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim().ToUpperInvariant())
            .Concat(configured.Keys).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).Select(x =>
                new RouteScheduleResponse(x, configured.GetValueOrDefault(x)?.AutomaticConfirmationCutoff, configured.GetValueOrDefault(x)?.Version ?? 0)).ToArray();
        return new(routes, clients.Where(x => x.IsActive).OrderBy(x => x.Name).Select(x => new CustomerScheduleResponse(x.Id, x.Name, x.RouteCode, x.AutomaticConfirmationCutoff, x.OrderingScheduleVersion)).ToArray());
    }
    public async Task SetRouteAsync(string code, ScheduleUpdate request, AdminActorContext actor, CancellationToken token)
    {
        Validate(request.Cutoff);
        var key = code.Trim().ToUpperInvariant();
        if (key.Length is 0 or > 80) throw new ArgumentException("La ruta debe tener de 1 a 80 caracteres.");
        var route = await schedules.GetAsync(key, token);
        if (request.ExpectedVersion != (route?.Version ?? 0)) throw new CatalogConflictException("El horario de ruta cambió. Actualiza antes de guardar.");
        if (route is null) { route = new() { RouteCode = key }; await schedules.AddAsync(route, token); }
        var previous = route.AutomaticConfirmationCutoff;
        route.AutomaticConfirmationCutoff = request.Cutoff; route.Version++;
        await audit.AddAsync(CatalogAudit.Create("RouteOrderSchedule", Guid.Empty, "RouteScheduleChanged", clock.Now, actor,
            "Horario de confirmación automática de ruta actualizado.", new { routeCode = key, previous, request.Cutoff }), token);
        await schedules.SaveChangesAsync(token);
    }
    public async Task SetCustomerAsync(Guid id, ScheduleUpdate request, AdminActorContext actor, CancellationToken token)
    {
        Validate(request.Cutoff);
        await customers.ExecuteOrderingProfileUpdateAsync(async inner =>
        {
            var customer = await customers.GetByIdForUpdateAsync(id, inner) ?? throw new KeyNotFoundException("El cliente no existe.");
            if (request.ExpectedVersion != customer.OrderingScheduleVersion)
                throw new CatalogConflictException("El horario del cliente cambió. Actualiza antes de guardar.");
            customer.OrderingScheduleVersion++;
            var previous = customer.AutomaticConfirmationCutoff;
            customer.AutomaticConfirmationCutoff = request.Cutoff; customer.UpdatedAt = clock.Now;
            await audit.AddAsync(CatalogAudit.Create("Customer", id, "CustomerScheduleChanged", clock.Now, actor,
                "Horario particular de confirmación automática actualizado.", new { previous, request.Cutoff }), inner);
            await customers.SaveChangesAsync(inner); return true;
        }, token);
    }
    private static void Validate(TimeOnly? cutoff)
    {
        if (cutoff is { } time && (time >= DeliveryDatePolicy.Cutoff || time.Ticks % TimeSpan.TicksPerMinute != 0))
            throw new ArgumentException("La hora límite debe ser anterior a las 18:00 y expresarse en horas y minutos.");
    }
}
