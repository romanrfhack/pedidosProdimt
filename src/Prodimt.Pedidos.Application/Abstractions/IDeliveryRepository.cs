using Prodimt.Pedidos.Domain.Entities;
namespace Prodimt.Pedidos.Application.Abstractions;

public interface IDeliveryRepository
{
    Task<IReadOnlyList<DeliveryRoute>> GetRoutesAsync(CancellationToken token);
    Task<DeliveryRoute?> GetRouteAsync(string code, CancellationToken token);
    Task<IReadOnlyList<AdminUser>> GetDriversAsync(CancellationToken token);
    Task<CustomerDeliveryLocation?> GetLocationAsync(Guid customerId, CancellationToken token);
    Task<IReadOnlyList<DeliveryTrip>> GetTripsAsync(DateOnly date, CancellationToken token);
    Task<DeliveryTrip?> GetTripAsync(Guid id, CancellationToken token);
    Task<IReadOnlyList<DeliveryAssignment>> GetAssignmentsAsync(Guid tripId, CancellationToken token);
    Task<IReadOnlyList<DeliveryAssignment>> GetOrderAssignmentsAsync(Guid orderId, CancellationToken token);
    Task<DeliveryAssignment?> GetAssignmentAsync(Guid id, CancellationToken token);
    Task<IReadOnlyList<DeliveryEvent>> GetEventsAsync(Guid tripId, CancellationToken token);
    Task AddRouteAsync(DeliveryRoute route, CancellationToken token);
    Task AddLocationAsync(CustomerDeliveryLocation location, CancellationToken token);
    Task AddTripAsync(DeliveryTrip trip, CancellationToken token);
    Task AddAssignmentAsync(DeliveryAssignment assignment, CancellationToken token);
    Task AddEventAsync(DeliveryEvent entry, CancellationToken token);
}
