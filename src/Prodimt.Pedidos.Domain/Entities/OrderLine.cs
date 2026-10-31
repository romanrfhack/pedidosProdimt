namespace Prodimt.Pedidos.Domain.Entities;

public sealed class OrderLine
{
    public Guid Id { get; set; }

    public Guid OrderId { get; set; }

    public Guid ProductId { get; set; }

    public string? ProductNameSnapshot { get; set; }

    public string? UnitCode { get; set; }

    public decimal Quantity { get; set; }

    public Guid? AssignedMachineId { get; set; }

    public string? Notes { get; set; }
}
