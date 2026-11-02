namespace Prodimt.Pedidos.Domain.Entities;

// Commercial follow-up, independent of authorization/review of the order.
public sealed class CustomerProductSetupRequest
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public Guid ProductId { get; set; }
    public string ProductNameSnapshot { get; set; } = string.Empty;
    public string UnitCode { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
    public bool IsResolved { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastRequestedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public string? ResolvedBy { get; set; }
    public string? ResolvedByName { get; set; }
    public List<CustomerProductSetupOrder> Orders { get; set; } = [];
}

// Link to the order/product, rather than a replaceable order-line identifier.
public sealed class CustomerProductSetupOrder
{
    public Guid SetupRequestId { get; set; }
    public Guid OrderId { get; set; }
    public string Source { get; set; } = string.Empty;
    public DateTimeOffset RequestedAt { get; set; }
    public string? ActorId { get; set; }
    public string? ActorDisplayName { get; set; }
}
