namespace Prodimt.Pedidos.Domain.Entities;

public sealed class Machine
{
    public Guid Id { get; set; }

    public int Number { get; set; }

    public string? Name { get; set; }

    public bool IsActive { get; set; } = true;
}
