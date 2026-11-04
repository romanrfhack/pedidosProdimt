namespace Prodimt.Pedidos.Domain.Services;

/// <summary>Shared input and persistence limits. Validate before mutating a tracked order.</summary>
public static class OrderNoteLimits
{
    public const int Delivery = 500;
    public const int Internal = 1000;
    public const int Line = 500;

    public static void Validate(string? deliveryNotes, string? internalNotes)
    {
        if (deliveryNotes?.Length > Delivery)
            throw new ArgumentException($"Las notas de entrega admiten hasta {Delivery} caracteres.", nameof(deliveryNotes));
        if (internalNotes?.Length > Internal)
            throw new ArgumentException($"Las notas internas admiten hasta {Internal} caracteres.", nameof(internalNotes));
    }
}
