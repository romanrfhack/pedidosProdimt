using System.Globalization;
using System.Text.RegularExpressions;
using Prodimt.Pedidos.Application.Abstractions;

namespace Prodimt.Pedidos.Application.AdminOrders;

public sealed record ResolvedCaptureLine(Guid ProductId, string ProductName, decimal Quantity,
    string UnitCode, int InterpretationPosition, string? CaptureAlias);

public static class OrderCaptureParser
{
    private static readonly Regex Term = new(@"\A\s*(\d+(?:[.,]\d{1,2})?)\s*([A-Za-z]{1,16})?\s*\z",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public static IReadOnlyList<ResolvedCaptureLine> Parse(string text, CustomerOrderingProfileSnapshot profile)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 240)
            throw new ArgumentException("Escribe el pedido con cantidades separadas por + (hasta 240 caracteres).");
        var items = profile.Items.Where(x => x.IsActive).ToArray();
        if (items.Length == 0 || items.Length > 200)
            throw new ArgumentException("Configura los productos habituales del cliente antes de usar captura rápida.");
        if (items.Any(x => !x.ProductIsActive || x.UnitCode is null))
            throw new ArgumentException("Confirma la unidad y el estado de todos los productos del perfil, o usa captura manual con productos confirmados.");
        if (!items.Select(x => x.InterpretationPosition).OrderBy(x => x)
                .SequenceEqual(Enumerable.Range(1, items.Length).Select(x => (int?)x)))
            throw new ArgumentException("Confirma las posiciones 1, 2, 3… del cliente en su configuración.");
        var positions = items.ToDictionary(x => x.InterpretationPosition!.Value);
        var aliases = new Dictionary<string, CustomerOrderingProfileItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items.Where(x => x.CaptureAlias is not null))
            if (!aliases.TryAdd(item.CaptureAlias!, item))
                throw new ArgumentException("Hay una abreviatura repetida en el perfil. Corrígela en Configurar.");
        var terms = text.Split('+');
        if (terms.Length > 200) throw new ArgumentException("El pedido contiene demasiadas cantidades.");
        var result = new List<ResolvedCaptureLine>();
        var seen = new HashSet<Guid>();
        var position = 0;
        foreach (var raw in terms)
        {
            var match = Term.Match(raw);
            if (!match.Success || !decimal.TryParse(match.Groups[1].Value.Replace(',', '.'),
                    NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var quantity)
                    || quantity > 9999999999999999.99m)
                throw new ArgumentException("Usa cantidades positivas o cero, con hasta dos decimales, separadas por +. Ejemplo: 13+7.");
            CustomerOrderingProfileItem? item;
            if (match.Groups[2].Success)
            {
                var alias = match.Groups[2].Value.ToUpperInvariant();
                if (!aliases.TryGetValue(alias, out item))
                    throw new ArgumentException($"La abreviatura {alias} no está configurada para este cliente. Asóciala a su producto en Configurar o usa captura manual.");
            }
            else if (!positions.TryGetValue(++position, out item))
                throw new ArgumentException($"El cliente no tiene un producto en la posición {position}. Revisa la captura o usa el modo manual.");
            if (!seen.Add(item.ProductId))
                throw new ArgumentException("El mismo producto aparece más de una vez. Captura su cantidad final una sola vez o usa el modo manual.");
            if (quantity > 0)
                result.Add(new(item.ProductId, item.ProductName, quantity, item.UnitCode!, item.InterpretationPosition!.Value, item.CaptureAlias));
        }
        if (result.Count == 0) throw new ArgumentException("Captura al menos una cantidad mayor que cero. Usa No pedir hoy sólo si el cliente lo confirmó.");
        return result.OrderBy(x => x.InterpretationPosition).ToArray();
    }
}
