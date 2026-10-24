namespace Prodimt.Pedidos.Domain.Entities;

public sealed class CustomerFrequentProduct
{
    public Guid CustomerId { get; set; }

    public Guid ProductId { get; set; }

    public decimal? DefaultQuantity { get; set; }

    public int SortOrder { get; set; }

    public Guid? PreferredMachineId { get; set; }

    // Explicit interpretation position; SortOrder is only a visual preference.
    public int? InterpretationPosition { get; set; }

    public bool IsActive { get; set; } = true;
}
