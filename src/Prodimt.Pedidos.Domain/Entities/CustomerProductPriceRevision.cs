namespace Prodimt.Pedidos.Domain.Entities;

// Append-only. A correction, including one on the same effective date, is another revision.
public sealed class CustomerProductPriceRevision
{
    public Guid Id { get; private set; }
    public Guid PriceBookId { get; private set; }
    public int Version { get; private set; }
    public decimal? UnitPrice { get; private set; }
    public string CurrencyCode { get; private set; } = "MXN";
    public string UnitCode { get; private set; } = string.Empty;
    public string ProductNameSnapshot { get; private set; } = string.Empty;
    public DateOnly EffectiveFrom { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }
    public string ActorId { get; private set; } = string.Empty;
    public string? ActorDisplayName { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public Guid OperationId { get; private set; }
    public string PayloadHash { get; private set; } = string.Empty;

    public static CustomerProductPriceRevision Create(Guid bookId, int version, decimal? price,
        string unitCode, string productName, DateOnly effectiveFrom, DateTimeOffset recordedAt,
        string? actorId, string? actorDisplayName, string reason, Guid operationId, string payloadHash)
    {
        if (bookId == Guid.Empty || version < 1 || operationId == Guid.Empty)
            throw new ArgumentException("La referencia de cambio de precio es obligatoria.");
        if (price is < 0 or > 999999999.9999m || price is { } amount && decimal.Round(amount, 4) != amount)
            throw new ArgumentException("El precio debe ser positivo o cero, con hasta cuatro decimales.");
        if (!ProductUnitCodes.IsSupported(unitCode))
            throw new ArgumentException("Confirma la unidad del producto antes de asignar precio.");
        if (string.IsNullOrWhiteSpace(actorId) || actorId.Length > 200 || actorDisplayName?.Length > 200)
            throw new ArgumentException("Se requiere la identidad del administrador que cambia el precio.");
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500)
            throw new ArgumentException("Indica el motivo del cambio (hasta 500 caracteres).");
        if (effectiveFrom < new DateOnly(2000, 1, 1) || effectiveFrom > new DateOnly(2100, 12, 31))
            throw new ArgumentException("Indica una fecha de vigencia entre 2000 y 2100.");
        return new CustomerProductPriceRevision
        {
            Id = Guid.NewGuid(), PriceBookId = bookId, Version = version, UnitPrice = price,
            UnitCode = unitCode, ProductNameSnapshot = productName, EffectiveFrom = effectiveFrom,
            RecordedAt = recordedAt, ActorId = actorId.Trim(), ActorDisplayName = actorDisplayName,
            Reason = reason.Trim(), OperationId = operationId, PayloadHash = payloadHash
        };
    }
}
