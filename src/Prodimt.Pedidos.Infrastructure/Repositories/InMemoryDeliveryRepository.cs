using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Domain.Entities;
namespace Prodimt.Pedidos.Infrastructure.Repositories;

// The existing demo operation store refuses mutations; never pretend these are durable.
public sealed class InMemoryDeliveryRepository : IDeliveryRepository
{
    public Task<IReadOnlyList<DeliveryRoute>> GetRoutesAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<DeliveryRoute>>([]);
    public Task<DeliveryRoute?> GetRouteAsync(string code, CancellationToken token) => Task.FromResult<DeliveryRoute?>(null);
    public Task<IReadOnlyList<DeliveryRouteDayDriver>> GetDayDriversAsync(DateOnly date, CancellationToken token)
        => Task.FromResult<IReadOnlyList<DeliveryRouteDayDriver>>([]);
    public Task<IReadOnlyList<AdminUser>> GetDriversAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<AdminUser>>([]);
    public Task<CustomerDeliveryLocation?> GetLocationAsync(Guid id, CancellationToken token) => Task.FromResult<CustomerDeliveryLocation?>(null);
    public Task<IReadOnlyList<DeliveryTrip>> GetTripsAsync(DateOnly date, CancellationToken token) => Task.FromResult<IReadOnlyList<DeliveryTrip>>([]);
    public Task<DeliveryTrip?> GetTripAsync(Guid id, CancellationToken token) => Task.FromResult<DeliveryTrip?>(null);
    public Task<IReadOnlyList<DeliveryAssignment>> GetAssignmentsAsync(Guid id, CancellationToken token) => Task.FromResult<IReadOnlyList<DeliveryAssignment>>([]);
    public Task<IReadOnlyList<DeliveryAssignment>> GetOrderAssignmentsAsync(Guid id, CancellationToken token) => Task.FromResult<IReadOnlyList<DeliveryAssignment>>([]);
    public Task<DeliveryAssignment?> GetAssignmentAsync(Guid id, CancellationToken token) => Task.FromResult<DeliveryAssignment?>(null);
    public Task<IReadOnlyList<DeliveryEvent>> GetEventsAsync(Guid id, CancellationToken token) => Task.FromResult<IReadOnlyList<DeliveryEvent>>([]);
    public Task AddRouteAsync(DeliveryRoute value, CancellationToken token) => throw new NotSupportedException();
    public Task AddDayDriverAsync(DeliveryRouteDayDriver item, CancellationToken token) => throw new NotSupportedException();
    public Task AddLocationAsync(CustomerDeliveryLocation value, CancellationToken token) => throw new NotSupportedException();
    public Task AddTripAsync(DeliveryTrip value, CancellationToken token) => throw new NotSupportedException();
    public Task AddAssignmentAsync(DeliveryAssignment value, CancellationToken token) => throw new NotSupportedException();
    public Task AddEventAsync(DeliveryEvent value, CancellationToken token) => throw new NotSupportedException();
}
