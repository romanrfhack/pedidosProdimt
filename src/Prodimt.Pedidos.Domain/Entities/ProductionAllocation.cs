namespace Prodimt.Pedidos.Domain.Entities;

/// <summary>A durable link from physical output to one order line; reversals retain the original link.</summary>
public sealed class ProductionAllocation
{
    public Guid Id { get; set; }
    public Guid BatchId { get; set; }
    public Guid OrderId { get; set; }
    // Kept as historical identity even if a reversed line is later corrected.
    public Guid OrderLineId { get; set; }
    public string CustomerNameSnapshot { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal ReleasedQuantity { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset? ReversedAt { get; set; }
    public string? ReversedBy { get; set; }
    public string? ReverseReason { get; set; }
}
