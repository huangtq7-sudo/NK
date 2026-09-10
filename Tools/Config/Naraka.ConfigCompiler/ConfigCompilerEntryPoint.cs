namespace Naraka.ConfigCompiler;

/// <summary>命令行入口。抽成公开类型，测试可以直接驱动同一条编译路径。</summary>
public static class ConfigCompilerEntryPoint
{
    public const int ExitSuccess = 0;
    public const int ExitValidationFailed = 1;
    public const int ExitOutputStale = 2;
    public const int ExitUsage = 64;

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        string? repositoryRoot = null;
        var checkOnly = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--check":
                    checkOnly = true;
                    break;

                case "--repo" when i + 1 < args.Length:
                    repositoryRoot = args[++i];
                    break;

                default:
                    error.WriteLine($"未知参数：{args[i]}");
                    error.WriteLine("用法：Naraka.ConfigCompiler [--repo <仓库根目录>] [--check]");
                    return ExitUsage;
            }
        }

        repositoryRoot ??= FindRepositoryRoot(AppContext.BaseDirectory);
        if (repositoryRoot is null)
        {
            error.WriteLine("未能定位仓库根目录（应包含 global.json）。请显式传入 --repo。");
            return ExitUsage;
        }

        var paths = new ConfigBuildPaths(repositoryRoot);
        var result = ConfigBuild.Run(paths, checkOnly);

        if (result.Diagnostics.Count > 0)
        {
            error.WriteLine($"配置校验失败，共 {result.Diagnostics.Count} 条错误：");
            foreach (var diagnostic in result.Diagnostics)
            {
                error.WriteLine("  " + diagnostic);
            }

            return ExitValidationFailed;
        }

        if (result.Differences.Count > 0)
        {
            error.WriteLine("生成物与源表不一致，请重新运行配置编译器并提交生成物：");
            foreach (var difference in result.Differences)
            {
                error.WriteLine("  " + difference);
            }

            return ExitOutputStale;
        }

        output.WriteLine(checkOnly
            ? $"配置检查通过：ConfigVersion={result.Emitted!.ConfigVersion}，SchemaVersion={result.Emitted.SchemaVersion}。"
            : $"配置生成完成：ConfigVersion={result.Emitted!.ConfigVersion}，SchemaVersion={result.Emitted.SchemaVersion}。");
        return ExitSuccess;
    }

    private static string? FindRepositoryRoot(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "global.json")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
