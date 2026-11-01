namespace Prodimt.Pedidos.Domain.Entities;

// The assignment can follow a consolidated product without rewriting its revisions.
public sealed class CustomerProductPriceBook
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public Guid ProductId { get; set; }
    public int Version { get; set; }
    public List<CustomerProductPriceRevision> Revisions { get; set; } = [];

    public CustomerProductPriceRevision? At(DateOnly serviceDate) => Revisions
        .Where(x => x.EffectiveFrom <= serviceDate)
        .OrderByDescending(x => x.EffectiveFrom).ThenByDescending(x => x.Version).FirstOrDefault();
}
