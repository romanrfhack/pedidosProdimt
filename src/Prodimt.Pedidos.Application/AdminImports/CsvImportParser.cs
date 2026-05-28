using System.Text;

namespace Prodimt.Pedidos.Application.AdminImports;

public sealed class CsvImportParser
{
    public CsvParseResult Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var rawRows = ParseRows(content);
        if (rawRows.Count == 0)
        {
            return new CsvParseResult([], []);
        }

        var headers = rawRows[0].Fields
            .Select(x => x.Trim())
            .ToArray();

        var rows = rawRows
            .Skip(1)
            .Select(rawRow =>
            {
                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (var index = 0; index < headers.Length; index++)
                {
                    values[headers[index]] = index < rawRow.Fields.Count
                        ? rawRow.Fields[index].Trim()
                        : string.Empty;
                }

                return new CsvImportRow(
                    rawRow.RowNumber,
                    values,
                    values.Values.All(string.IsNullOrWhiteSpace));
            })
            .ToArray();

        return new CsvParseResult(headers, rows);
    }

    private static List<RawCsvRow> ParseRows(string content)
    {
        var rows = new List<RawCsvRow>();
        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        var rowNumber = 1;
        var currentRowNumber = 1;
        var hasAnyCharacterInRow = false;

        for (var index = 0; index < content.Length; index++)
        {
            var character = content[index];
            hasAnyCharacterInRow = true;

            if (inQuotes)
            {
                if (character == '"')
                {
                    if (index + 1 < content.Length && content[index + 1] == '"')
                    {
                        current.Append('"');
                        index++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(character);
                }

                continue;
            }

            if (character == '"')
            {
                inQuotes = true;
                continue;
            }

            if (character == ',')
            {
                fields.Add(current.ToString());
                current.Clear();
                continue;
            }

            if (character == '\r' || character == '\n')
            {
                if (character == '\r' && index + 1 < content.Length && content[index + 1] == '\n')
                {
                    index++;
                }

                AddRow(rows, fields, current, currentRowNumber);
                rowNumber++;
                currentRowNumber = rowNumber;
                hasAnyCharacterInRow = false;
                continue;
            }

            current.Append(character);
        }

        if (hasAnyCharacterInRow || fields.Count > 0 || current.Length > 0)
        {
            AddRow(rows, fields, current, currentRowNumber);
        }

        return rows;
    }

    private static void AddRow(
        ICollection<RawCsvRow> rows,
        ICollection<string> fields,
        StringBuilder current,
        int rowNumber)
    {
        fields.Add(current.ToString());
        rows.Add(new RawCsvRow(rowNumber, fields.ToArray()));
        fields.Clear();
        current.Clear();
    }

    private sealed record RawCsvRow(int RowNumber, IReadOnlyList<string> Fields);
}

public sealed record CsvParseResult(
    IReadOnlyList<string> Headers,
    IReadOnlyList<CsvImportRow> Rows);

public sealed record CsvImportRow(
    int RowNumber,
    IReadOnlyDictionary<string, string> Values,
    bool IsEmpty)
{
    public string Get(string header)
    {
        return Values.TryGetValue(header, out var value) ? value : string.Empty;
    }
}
