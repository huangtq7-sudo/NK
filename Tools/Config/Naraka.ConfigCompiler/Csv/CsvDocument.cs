using System.Globalization;
using System.Text;

namespace Naraka.ConfigCompiler.Csv;

/// <summary>
/// 一行 CSV 数据。行号是源表中的物理行号（含表头），因此校验错误可以直接指向 Excel 中的行。
/// </summary>
public sealed class CsvRecord
{
    private readonly IReadOnlyDictionary<string, int> _columns;
    private readonly string[] _values;

    internal CsvRecord(string fileName, int lineNumber, IReadOnlyDictionary<string, int> columns, string[] values)
    {
        FileName = fileName;
        LineNumber = lineNumber;
        _columns = columns;
        _values = values;
    }

    public string FileName { get; }

    public int LineNumber { get; }

    public string GetString(string column, DiagnosticBag diagnostics)
    {
        if (!_columns.TryGetValue(column, out var index))
        {
            diagnostics.Add(FileName, LineNumber, $"缺少列 '{column}'。");
            return string.Empty;
        }

        return _values[index];
    }

    public string GetRequiredString(string column, DiagnosticBag diagnostics)
    {
        var value = GetString(column, diagnostics);
        if (value.Length == 0)
        {
            diagnostics.Add(FileName, LineNumber, $"列 '{column}' 不能为空。");
        }

        return value;
    }

    public int GetInt32(string column, DiagnosticBag diagnostics)
    {
        var raw = GetString(column, diagnostics);
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        diagnostics.Add(FileName, LineNumber, $"列 '{column}' 需要整数，实际为 '{raw}'。");
        return 0;
    }

    public long GetInt64(string column, DiagnosticBag diagnostics)
    {
        var raw = GetString(column, diagnostics);
        if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        diagnostics.Add(FileName, LineNumber, $"列 '{column}' 需要长整数，实际为 '{raw}'。");
        return 0L;
    }

    public float GetSingle(string column, DiagnosticBag diagnostics)
    {
        var raw = GetString(column, diagnostics);
        if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        diagnostics.Add(FileName, LineNumber, $"列 '{column}' 需要小数，实际为 '{raw}'。");
        return 0f;
    }

    /// <summary>布尔列统一使用 1/0，避免 Excel 在不同语言环境写出 TRUE/真/VERDADERO。</summary>
    public bool GetBoolean(string column, DiagnosticBag diagnostics)
    {
        var raw = GetString(column, diagnostics);
        return raw switch
        {
            "1" => true,
            "0" => false,
            _ => ReportInvalidBoolean(column, raw, diagnostics)
        };
    }

    private bool ReportInvalidBoolean(string column, string raw, DiagnosticBag diagnostics)
    {
        diagnostics.Add(FileName, LineNumber, $"列 '{column}' 只接受 1 或 0，实际为 '{raw}'。");
        return false;
    }
}

/// <summary>
/// 最小实现的 RFC 4180 读取器。刻意不引入第三方 CSV 依赖：源表格式受本仓库控制，
/// 而配置生成必须是可审计、确定性的过程。
/// </summary>
public sealed class CsvDocument
{
    private CsvDocument(string fileName, IReadOnlyList<string> header, IReadOnlyList<CsvRecord> records)
    {
        FileName = fileName;
        Header = header;
        Records = records;
    }

    public string FileName { get; }

    public IReadOnlyList<string> Header { get; }

    public IReadOnlyList<CsvRecord> Records { get; }

    /// <summary>
    /// 以固定 UTF-8 读取源表。Excel 保存的 UTF-8 CSV 带 BOM，这里统一剥离，
    /// 因此同一份内容无论是否带 BOM 都会生成完全一致的结果。
    /// </summary>
    public static CsvDocument Load(string path, DiagnosticBag diagnostics)
    {
        var fileName = Path.GetFileName(path);
        if (!File.Exists(path))
        {
            diagnostics.Add(fileName, 0, "源表文件不存在。");
            return new CsvDocument(fileName, Array.Empty<string>(), Array.Empty<CsvRecord>());
        }

        var text = new UTF8Encoding(false).GetString(StripBom(File.ReadAllBytes(path)));
        var lines = SplitLines(text);
        if (lines.Count == 0)
        {
            diagnostics.Add(fileName, 0, "源表为空，至少需要一行表头。");
            return new CsvDocument(fileName, Array.Empty<string>(), Array.Empty<CsvRecord>());
        }

        var header = ParseLine(lines[0]);
        var columns = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < header.Length; i++)
        {
            var name = header[i];
            if (name.Length == 0)
            {
                diagnostics.Add(fileName, 1, $"第 {i + 1} 列的表头为空。");
                continue;
            }

            if (!columns.TryAdd(name, i))
            {
                diagnostics.Add(fileName, 1, $"表头列名重复：'{name}'。");
            }
        }

        var records = new List<CsvRecord>(lines.Count - 1);
        for (var lineIndex = 1; lineIndex < lines.Count; lineIndex++)
        {
            var line = lines[lineIndex];
            if (line.Length == 0)
            {
                continue;
            }

            var values = ParseLine(line);
            var lineNumber = lineIndex + 1;
            if (values.Length != header.Length)
            {
                diagnostics.Add(
                    fileName,
                    lineNumber,
                    $"列数不匹配：表头 {header.Length} 列，本行 {values.Length} 列。");
                continue;
            }

            records.Add(new CsvRecord(fileName, lineNumber, columns, values));
        }

        return new CsvDocument(fileName, header, records);
    }

    private static byte[] StripBom(byte[] bytes) =>
        bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF
            ? bytes[3..]
            : bytes;

    /// <summary>按 CRLF/LF 拆行并丢弃末尾空行，让 Windows 与 Linux 检出得到相同的行集合。</summary>
    private static List<string> SplitLines(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .ToList();

        while (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return lines;
    }

    private static string[] ParseLine(string line)
    {
        var values = new List<string>();
        var builder = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var current = line[i];
            if (inQuotes)
            {
                if (current != '"')
                {
                    builder.Append(current);
                    continue;
                }

                if (i + 1 < line.Length && line[i + 1] == '"')
                {
                    builder.Append('"');
                    i++;
                    continue;
                }

                inQuotes = false;
                continue;
            }

            switch (current)
            {
                case '"':
                    inQuotes = true;
                    break;
                case ',':
                    values.Add(builder.ToString().Trim());
                    builder.Clear();
                    break;
                default:
                    builder.Append(current);
                    break;
            }
        }

        values.Add(builder.ToString().Trim());
        return values.ToArray();
    }
}
