using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Application.Abstractions;

public interface IMachineTeamRepository
{
    Task<IReadOnlyList<MachineWorker>> GetWorkersAsync(CancellationToken token);
    Task<IReadOnlyList<MachineTeamTemplate>> GetTemplatesAsync(CancellationToken token);
    Task<IReadOnlyList<MachineTeamChange>> GetChangesAsync(DateOnly date, CancellationToken token);
    Task AddWorkerAsync(MachineWorker worker, CancellationToken token);
    Task AddTemplateAsync(MachineTeamTemplate template, CancellationToken token);
    Task AddChangeAsync(MachineTeamChange change, CancellationToken token);
}
