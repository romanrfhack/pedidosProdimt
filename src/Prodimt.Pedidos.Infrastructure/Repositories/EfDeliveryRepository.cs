using Microsoft.EntityFrameworkCore;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Infrastructure.Persistence;
namespace Prodimt.Pedidos.Infrastructure.Repositories;

public sealed class EfDeliveryRepository(PedidosDbContext db) : IDeliveryRepository
{
    public async Task<IReadOnlyList<DeliveryRoute>> GetRoutesAsync(CancellationToken token)
        => await db.DeliveryRoutes.OrderBy(x => x.Code).ToArrayAsync(token);
    public Task<DeliveryRoute?> GetRouteAsync(string code, CancellationToken token)
        => db.DeliveryRoutes.SingleOrDefaultAsync(x => x.Code == code, token);
    public async Task<IReadOnlyList<DeliveryRouteDayDriver>> GetDayDriversAsync(DateOnly date, CancellationToken token)
        => await db.DeliveryRouteDayDrivers.Where(x => x.Date == date).ToArrayAsync(token);
    public async Task<IReadOnlyList<AdminUser>> GetDriversAsync(CancellationToken token)
    {
        var permissionIds = db.SecurityPermissions.Where(x => x.IsActive && x.Key == "delivery.operate.own").Select(x => x.Id);
        var roles = from ur in db.SecurityUserRoles join r in db.SecurityRoles on ur.RoleId equals r.Id
                    join rp in db.SecurityRolePermissions on r.Id equals rp.RoleId
                    where r.IsActive && permissionIds.Contains(rp.PermissionId) select ur.UserId;
        var direct = db.SecurityUserPermissions.Where(x => permissionIds.Contains(x.PermissionId)).Select(x => x.UserId);
        return await db.AdminUsers.AsNoTracking().Where(x => x.IsActive && !x.IsDeleted && (roles.Contains(x.Id) || direct.Contains(x.Id)))
            .OrderBy(x => x.DisplayName).ToArrayAsync(token);
    }
    public Task<CustomerDeliveryLocation?> GetLocationAsync(Guid customerId, CancellationToken token)
        => db.CustomerDeliveryLocations.SingleOrDefaultAsync(x => x.CustomerId == customerId, token);
    public async Task<IReadOnlyList<DeliveryTrip>> GetTripsAsync(DateOnly date, CancellationToken token)
        => await db.DeliveryTrips.Where(x => x.Date == date).OrderBy(x => x.RouteCode).ThenBy(x => x.TripNumber).ToArrayAsync(token);
    public Task<DeliveryTrip?> GetTripAsync(Guid id, CancellationToken token)
        => db.DeliveryTrips.SingleOrDefaultAsync(x => x.Id == id, token);
    public async Task<IReadOnlyList<DeliveryAssignment>> GetAssignmentsAsync(Guid tripId, CancellationToken token)
        => await db.DeliveryAssignments.Where(x => x.TripId == tripId).OrderBy(x => x.RouteStopOrder).ThenBy(x => x.CustomerName).ThenBy(x => x.ProductName).ToArrayAsync(token);
    public async Task<IReadOnlyList<DeliveryAssignment>> GetOrderAssignmentsAsync(Guid orderId, CancellationToken token)
        => await db.DeliveryAssignments.Where(x => x.OrderId == orderId).ToArrayAsync(token);
    public Task<DeliveryAssignment?> GetAssignmentAsync(Guid id, CancellationToken token)
        => db.DeliveryAssignments.SingleOrDefaultAsync(x => x.Id == id, token);
    public async Task<IReadOnlyList<DeliveryEvent>> GetEventsAsync(Guid tripId, CancellationToken token)
        => (await db.DeliveryEvents.AsNoTracking().Where(x => x.TripId == tripId).ToArrayAsync(token))
            .OrderByDescending(x => x.OccurredAt).ThenBy(x => x.Id).ToArray();
    public async Task AddRouteAsync(DeliveryRoute item, CancellationToken token) => await db.DeliveryRoutes.AddAsync(item, token);
    public async Task AddDayDriverAsync(DeliveryRouteDayDriver item, CancellationToken token)
        => await db.DeliveryRouteDayDrivers.AddAsync(item, token);
    public async Task AddLocationAsync(CustomerDeliveryLocation item, CancellationToken token) => await db.CustomerDeliveryLocations.AddAsync(item, token);
    public async Task AddTripAsync(DeliveryTrip item, CancellationToken token) => await db.DeliveryTrips.AddAsync(item, token);
    public async Task AddAssignmentAsync(DeliveryAssignment item, CancellationToken token) => await db.DeliveryAssignments.AddAsync(item, token);
    public async Task AddEventAsync(DeliveryEvent item, CancellationToken token) => await db.DeliveryEvents.AddAsync(item, token);
}
