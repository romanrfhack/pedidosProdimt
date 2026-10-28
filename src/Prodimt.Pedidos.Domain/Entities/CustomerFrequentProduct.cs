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

    private string? _captureAlias;
    public string? CaptureAlias
    {
        get => _captureAlias;
        set => _captureAlias = Services.OrderCaptureAlias.Normalize(value);
    }

    public bool IsActive { get; set; } = true;
}
