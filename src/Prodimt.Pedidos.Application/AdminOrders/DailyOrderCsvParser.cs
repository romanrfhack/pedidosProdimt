using System.Text;
using Prodimt.Pedidos.Application.AdminImports;

namespace Prodimt.Pedidos.Application.AdminOrders;

internal static class DailyOrderCsvParser
{
    private static readonly string[] RequiredHeaders =
        ["externalOrderKey", "serviceDate", "customerExternalCode", "action", "productExternalCode", "quantity", "unitCode", "notes"];

    public static IReadOnlyList<CsvImportRow> Parse(string content)
    {
        if (string.IsNullOrWhiteSpace(content) || Encoding.UTF8.GetByteCount(content) > 2 * 1024 * 1024)
            throw new ArgumentException("El CSV debe contener pedidos y no exceder 2 MB.");
        content = content.TrimStart('\uFEFF');
        var records = new List<(int Number, string[] Fields)>();
        var fields = new List<string>();
        var text = new StringBuilder();
        var quoted = false;
        var afterQuote = false;
        var line = 1;
        var recordLine = 1;
        for (var index = 0; index < content.Length; index++)
        {
            var value = content[index];
            if (quoted)
            {
                if (value == '"')
                {
                    if (index + 1 < content.Length && content[index + 1] == '"') { text.Append('"'); index++; }
                    else { quoted = false; afterQuote = true; }
                }
                else { text.Append(value); if (value == '\n') line++; }
                continue;
            }
            if (value == '"')
            {
                if (text.Length != 0 || afterQuote) throw new ArgumentException($"CSV mal formado en fila {line}: comillas inesperadas.");
                quoted = true;
                continue;
            }
            if (afterQuote && value != ',' && value != '\r' && value != '\n')
                throw new ArgumentException($"CSV mal formado en fila {line}: texto despues de comillas.");
            if (value is ',' or '\r' or '\n')
            {
                fields.Add(text.ToString().Trim()); text.Clear(); afterQuote = false;
                if (value == ',') continue;
                records.Add((recordLine, fields.ToArray())); fields.Clear();
                if (value == '\r' && index + 1 < content.Length && content[index + 1] == '\n') index++;
                line++; recordLine = line;
                continue;
            }
            text.Append(value);
        }
        if (quoted) throw new ArgumentException("CSV mal formado: faltan comillas de cierre.");
        if (text.Length > 0 || fields.Count > 0 || afterQuote) { fields.Add(text.ToString().Trim()); records.Add((recordLine, fields.ToArray())); }
        if (records.Count < 2) throw new ArgumentException("El CSV no contiene partidas.");
        var headers = records[0].Fields;
        if (headers.Length != RequiredHeaders.Length || headers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != headers.Length ||
            RequiredHeaders.Any(x => !headers.Contains(x, StringComparer.OrdinalIgnoreCase)))
            throw new ArgumentException("Encabezados requeridos: " + string.Join(",", RequiredHeaders));
        var rows = new List<CsvImportRow>();
        foreach (var record in records.Skip(1))
        {
            if (record.Fields.All(string.IsNullOrWhiteSpace)) continue;
            if (record.Fields.Length != headers.Length) throw new ArgumentException($"La fila {record.Number} tiene un numero incorrecto de columnas.");
            rows.Add(new(record.Number, headers.Select((header, index) => (header, record.Fields[index]))
                .ToDictionary(x => x.header, x => x.Item2, StringComparer.OrdinalIgnoreCase), false));
        }
        if (rows.Count == 0 || rows.Count > 2000) throw new ArgumentException("El CSV debe contener de 1 a 2000 partidas.");
        return rows;
    }
}
