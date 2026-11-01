namespace Prodimt.Pedidos.Domain.Entities;

public sealed class ProductConsolidationOperation
{
    public Guid Id { get; set; }
    public string PayloadHash { get; set; } = string.Empty;
    public DateTimeOffset CompletedAt { get; set; }
    public string ResultJson { get; set; } = string.Empty;
}
