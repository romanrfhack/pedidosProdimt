namespace Prodimt.Pedidos.Domain.Entities;

public sealed class MachineWorker
{
    public Guid Id { get; init; }
    public string Name { get; init; } = null!;
    public bool IsActive { get; init; } = true;
}

public sealed class MachineTeamTemplate
{
    public Guid Id { get; init; }
    public Guid MachineId { get; init; }
    public DateOnly EffectiveDate { get; init; }
    public Guid? OperatorId { get; set; }
    public Guid? FirstCollectorId { get; set; }
    public Guid? SecondCollectorId { get; set; }
    public Guid? PackerId { get; set; }
    public int Revision { get; set; } = 1;
}

public sealed class MachineTeamChange
{
    public Guid Id { get; init; }
    public Guid MachineId { get; init; }
    public DateOnly Date { get; init; }
    public int Position { get; init; }
    public int Revision { get; init; }
    public string Action { get; init; } = null!;
    public string Status { get; init; } = null!;
    public Guid? WorkerId { get; init; }
    public Guid? PreviousWorkerId { get; init; }
    public Guid? OtherMachineId { get; init; }
    public int? OtherPosition { get; init; }
    public Guid? TransferId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public string? ActorName { get; init; }
}
