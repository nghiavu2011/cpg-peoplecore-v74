using System.Text;

namespace PeopleCore.Api.Services;

public static class CsvImportParser
{
    public static async Task<(byte[] Bytes, IReadOnlyList<Dictionary<string, string>> Rows)> ReadAsync(
        IFormFile file,
        IReadOnlySet<string> requiredHeaders,
        int maxRows,
        long maxBytes,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0) throw new InvalidOperationException("EMPTY_IMPORT_FILE");
        if (file.Length > maxBytes) throw new InvalidOperationException("IMPORT_FILE_TOO_LARGE");

        await using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();
        var text = Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
        var records = Parse(text);
        if (records.Count == 0) throw new InvalidOperationException("CSV_HEADER_REQUIRED");

        var headers = records[0].Select(NormalizeHeader).ToArray();
        if (headers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != headers.Length)
            throw new InvalidOperationException("DUPLICATE_CSV_HEADER");
        foreach (var required in requiredHeaders)
            if (!headers.Contains(required, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException($"CSV_HEADER_MISSING:{required}");

        var rows = new List<Dictionary<string, string>>();
        for (var i = 1; i < records.Count; i++)
        {
            if (records[i].All(string.IsNullOrWhiteSpace)) continue;
            if (rows.Count >= maxRows) throw new InvalidOperationException("IMPORT_ROW_LIMIT_EXCEEDED");
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var c = 0; c < headers.Length; c++) map[headers[c]] = c < records[i].Count ? records[i][c].Trim() : string.Empty;
            rows.Add(map);
        }
        return (bytes, rows);
    }

    private static string NormalizeHeader(string value) => value.Trim().Trim('\uFEFF').ToLowerInvariant();

    private static List<List<string>> Parse(string text)
    {
        var result = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (quoted)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else quoted = false;
                }
                else field.Append(ch);
                continue;
            }
            if (ch == '"' && field.Length == 0) { quoted = true; continue; }
            if (ch == ',') { row.Add(field.ToString()); field.Clear(); continue; }
            if (ch == '\r') continue;
            if (ch == '\n') { row.Add(field.ToString()); field.Clear(); result.Add(row); row = new List<string>(); continue; }
            field.Append(ch);
        }
        if (quoted) throw new InvalidOperationException("CSV_UNCLOSED_QUOTE");
        if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); result.Add(row); }
        return result;
    }
}
