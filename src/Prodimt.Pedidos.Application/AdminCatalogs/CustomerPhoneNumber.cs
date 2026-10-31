namespace Prodimt.Pedidos.Application.AdminCatalogs;

public static class CustomerPhoneNumber
{
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 64)
            throw new ArgumentException("Guarda un teléfono válido del cliente antes de crear el enlace.", nameof(value));

        var text = value.Trim();
        var digits = new System.Text.StringBuilder();
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c is >= '0' and <= '9') digits.Append(c);
            else if (c == '+' && i == 0 || c is ' ' or '-' or '(' or ')') continue;
            else throw new ArgumentException("El teléfono contiene caracteres no válidos.", nameof(value));
        }
        if (digits.Length is < 8 or > 15 || digits[0] == '0')
            throw new ArgumentException("Captura un teléfono de 8 a 15 dígitos con código de país.", nameof(value));
        if (text[0] == '+') return "+" + digits;
        if (digits.Length == 10) return "+52" + digits;
        if (digits.Length == 12 && digits.ToString().StartsWith("52", StringComparison.Ordinal))
            return "+" + digits;
        throw new ArgumentException("Para un número internacional, incluye + y el código de país.", nameof(value));
    }
}
