namespace Prodimt.Pedidos.Application.AdminOperations;

public sealed record MachineWorkerResponse(Guid Id, string Name, bool IsActive);

public sealed record MachineTeamSlotResponse(int Position, string Label, Guid? RegularWorkerId, string? RegularWorkerName,
    Guid? WorkerId, string? WorkerName, string Status);

public sealed record MachineTeamHistoryResponse(int Position, string Label, string Action,
    string? WorkerName, Guid? OtherMachineId, DateTimeOffset CreatedAt, string? ActorName);

public sealed record MachineTeamDestinationResponse(Guid Id, string Name, int Number);

public sealed record MachineTeamResponse(Guid MachineId, DateOnly Date, DateOnly Today, DateOnly? EffectiveDate,
    string Version, bool CanEditDaily, bool CanManageWorkers, IReadOnlyList<MachineTeamSlotResponse> Slots,
    IReadOnlyList<MachineWorkerResponse> Workers, IReadOnlyList<MachineTeamHistoryResponse> History,
    IReadOnlyList<MachineTeamDestinationResponse> Destinations);

public sealed record RegisterMachineWorkerRequest(Guid OperationId, string Name);
public sealed record MachineTeamTemplateSlotRequest(int Position, Guid? WorkerId);
public sealed record SaveMachineTeamTemplateRequest(Guid OperationId, DateOnly EffectiveDate,
    IReadOnlyList<MachineTeamTemplateSlotRequest> Slots, string? ExpectedVersion);
public sealed record ChangeMachineTeamRequest(Guid OperationId, DateOnly Date, int Position, string Action,
    Guid? WorkerId, Guid? OtherMachineId, int? OtherPosition, string? ExpectedVersion);
