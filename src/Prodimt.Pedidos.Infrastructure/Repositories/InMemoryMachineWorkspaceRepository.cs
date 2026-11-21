using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Infrastructure.Repositories;

public sealed class InMemoryMachineWorkspaceRepository : IMachineWorkspaceRepository
{
    public Task<IReadOnlyList<Guid>> GetMachineIdsAsync(Guid userId, CancellationToken token) =>
        Task.FromResult<IReadOnlyList<Guid>>([]);
    public Task<IReadOnlyList<Guid>> GetOperatorIdsAsync(Guid machineId, CancellationToken token) =>
        Task.FromResult<IReadOnlyList<Guid>>([]);
    public Task<IReadOnlyList<MachineProductionNotice>> GetNoticesAsync(Guid machineId, DateOnly date, CancellationToken token) =>
        Task.FromResult<IReadOnlyList<MachineProductionNotice>>([]);
    public Task ReplaceOperatorsAsync(Guid machineId, IReadOnlyList<Guid> userIds, CancellationToken token) =>
        throw new NotSupportedException();
    public Task AddNoticeAsync(MachineProductionNotice notice, CancellationToken token) =>
        throw new NotSupportedException();
}
