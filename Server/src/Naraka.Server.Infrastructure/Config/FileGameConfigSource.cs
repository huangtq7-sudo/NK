using Naraka.Server.Application.Config;

namespace Naraka.Server.Infrastructure.Config;

/// <summary>
/// 从发布目录的 <c>Config/</c> 读取规范配置生成物。
/// 文件访问留在 Infrastructure，Application 只处理已经读到内存的 JSON 正文。
/// </summary>
public sealed class FileGameConfigSource
{
    public const string DirectoryName = "Config";
    public const string CatalogFileName = "naraka-config.json";

    private readonly string _baseDirectory;

    public FileGameConfigSource(string? baseDirectory = null) =>
        _baseDirectory = string.IsNullOrWhiteSpace(baseDirectory) ? AppContext.BaseDirectory : baseDirectory;

    public string CatalogPath => Path.Combine(_baseDirectory, DirectoryName, CatalogFileName);

    public GameConfigLoadResult Load(string expectedSchemaVersion)
    {
        var path = CatalogPath;
        if (!File.Exists(path))
        {
            // 只报告相对文件名，不把完整部署路径写进可能外泄的日志。
            return new GameConfigLoadResult(
                GameConfigLoadStatus.Missing,
                null,
                $"未找到配置生成物 {DirectoryName}/{CatalogFileName}。请先运行 Naraka.ConfigCompiler。");
        }

        string json;
        try
        {
            json = File.ReadAllText(path, System.Text.Encoding.UTF8);
        }
        catch (IOException exception)
        {
            return new GameConfigLoadResult(
                GameConfigLoadStatus.Missing,
                null,
                $"读取 {DirectoryName}/{CatalogFileName} 失败：{exception.Message}");
        }

        return GameConfig.Load(json, expectedSchemaVersion);
    }
}
