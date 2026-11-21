namespace Prodimt.Pedidos.Domain.Entities;

public sealed class MachineOperator
{
    public Guid MachineId { get; set; }
    public Guid UserId { get; set; }
}

public sealed class ProductionLotHandoff
{
    public Guid Id { get; set; }
    public Guid BatchId { get; set; }
    public decimal Quantity { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
}

public sealed class MachineProductionNotice
{
    public Guid Id { get; set; }
    public Guid MachineId { get; set; }
    public DateOnly ServiceDate { get; set; }
    public Guid OrderId { get; set; }
    public Guid OrderLineId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
