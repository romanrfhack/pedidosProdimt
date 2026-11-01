namespace Prodimt.Pedidos.Domain.Entities;

public sealed class OrderLine
{
    public Guid Id { get; set; }

    public Guid OrderId { get; set; }

    public Guid ProductId { get; set; }

    public string? ProductNameSnapshot { get; set; }

    public string? UnitCode { get; set; }

    public decimal Quantity { get; set; }

    public decimal? UnitPriceSnapshot { get; set; }
    public string? CurrencyCodeSnapshot { get; set; }
    public Guid? PriceRevisionId { get; set; }
    public DateOnly? PriceEffectiveFrom { get; set; }

    public decimal? LineAmount => UnitPriceSnapshot is { } price
        ? decimal.Round(Quantity * price, 2, MidpointRounding.AwayFromZero) : null;

    public Guid? AssignedMachineId { get; set; }

    public string? Notes { get; set; }
}
