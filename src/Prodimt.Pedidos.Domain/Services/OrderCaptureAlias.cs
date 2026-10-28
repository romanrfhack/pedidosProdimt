namespace Prodimt.Pedidos.Domain.Services;

public static class OrderCaptureAlias
{
    public static string? Normalize(string? value)
    {
        var alias = value?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(alias)) return null;
        if (alias.Length > 16 || alias.Any(c => c is < 'A' or > 'Z'))
            throw new ArgumentException("La abreviatura debe tener entre 1 y 16 letras, sin espacios ni números.");
        return alias;
    }
}
