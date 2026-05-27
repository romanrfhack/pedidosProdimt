using Prodimt.Pedidos.Domain.Enums;

namespace Prodimt.Pedidos.Domain.Entities;

public sealed class SalesChannel
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public SalesChannelType Type { get; set; }

    public bool IsInternal { get; set; }
}
