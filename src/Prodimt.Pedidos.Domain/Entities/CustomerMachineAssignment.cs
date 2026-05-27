namespace Prodimt.Pedidos.Domain.Entities;

public sealed class CustomerMachineAssignment
{
    public Guid CustomerId { get; set; }

    public Guid MachineId { get; set; }

    public bool IsDefault { get; set; }

    public string? Notes { get; set; }
}
