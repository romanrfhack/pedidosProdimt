namespace Prodimt.Pedidos.Domain.Entities;

/// <summary>Immutable physical output. Allocation does not create more production.</summary>
public sealed class ProductionBatch
{
    public Guid Id { get; set; }
    public DateOnly ServiceDate { get; set; }
    public Guid MachineId { get; set; }
    public Guid ProductId { get; set; }
    public string ProductNameSnapshot { get; set; } = string.Empty;
    public string UnitCode { get; set; } = ProductUnitCodes.Kilogram;
    public decimal Quantity { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? Notes { get; set; }
}
