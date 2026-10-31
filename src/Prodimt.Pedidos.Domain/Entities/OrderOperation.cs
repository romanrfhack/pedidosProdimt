namespace Prodimt.Pedidos.Domain.Entities;

// Durable receipt: the business changes and this result commit in one transaction.
public sealed class OrderOperation
{
    public string OperationKey { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty;
    public string ResultJson { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
