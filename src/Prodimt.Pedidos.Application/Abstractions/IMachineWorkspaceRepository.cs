using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Application.Abstractions;

public interface IMachineWorkspaceRepository
{
    Task<IReadOnlyList<Guid>> GetMachineIdsAsync(Guid userId, CancellationToken token);
    Task<IReadOnlyList<Guid>> GetOperatorIdsAsync(Guid machineId, CancellationToken token);
    Task ReplaceOperatorsAsync(Guid machineId, IReadOnlyList<Guid> userIds, CancellationToken token);
    Task<IReadOnlyList<MachineProductionNotice>> GetNoticesAsync(Guid machineId, DateOnly date, CancellationToken token);
    Task AddNoticeAsync(MachineProductionNotice notice, CancellationToken token);
}
