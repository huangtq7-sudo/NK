using System.Globalization;

namespace Naraka.ConfigCompiler.Csv;

/// <summary>一条配置校验错误。始终携带源文件与行号，便于直接回到 Excel 修改。</summary>
public sealed record ConfigDiagnostic(string FileName, int LineNumber, string Message)
{
    public override string ToString() => LineNumber > 0
        ? string.Format(CultureInfo.InvariantCulture, "{0}({1}): {2}", FileName, LineNumber, Message)
        : string.Format(CultureInfo.InvariantCulture, "{0}: {1}", FileName, Message);
}

/// <summary>
/// 收集全部校验错误后一次性报告。逐条抛异常会让使用者每修一个错就要重跑一次，
/// 而配置表的错误通常成批出现。
/// </summary>
public sealed class DiagnosticBag
{
    private readonly List<ConfigDiagnostic> _diagnostics = new();

    public IReadOnlyList<ConfigDiagnostic> Diagnostics => _diagnostics;

    public bool HasErrors => _diagnostics.Count > 0;

    public void Add(string fileName, int lineNumber, string message) =>
        _diagnostics.Add(new ConfigDiagnostic(fileName, lineNumber, message));

    /// <summary>按文件名与行号稳定排序，使同一份错误输入总是产生同样的报告顺序。</summary>
    public IReadOnlyList<ConfigDiagnostic> Sorted() => _diagnostics
        .OrderBy(diagnostic => diagnostic.FileName, StringComparer.Ordinal)
        .ThenBy(diagnostic => diagnostic.LineNumber)
        .ThenBy(diagnostic => diagnostic.Message, StringComparer.Ordinal)
        .ToArray();
}
