using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Application.AdminCatalogs;

public static class ProductIdentity
{
    // Old external codes remain aliases. Never rewrite historical order lines.
    public static Product Resolve(Product product, IReadOnlyList<Product> catalog)
    {
        var seen = new HashSet<Guid>();
        while (product.MergedIntoProductId is { } next)
        {
            if (!seen.Add(product.Id)) throw new ArgumentException("La equivalencia de productos contiene un ciclo.");
            product = catalog.SingleOrDefault(x => x.Id == next)
                ?? throw new ArgumentException("No se encontro el producto compartido de este codigo.");
        }
        return product;
    }
}
