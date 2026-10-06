using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Naraka.EditorTools
{
    /// <summary>
    /// 生成"干净检出要跑起来，到底需要哪些第三方文件"的权威清单。
    ///
    /// 存在的理由是一个真实缺陷：`Map01_Task` 引用了 High Elves 目录里上百个 GUID，
    /// 而那个目录当时被 `.gitignore` 整体排除，Git 里跟踪的文件数是 **0**。
    /// 本机能跑是因为文件在磁盘上，但**干净检出复现不出来** ——
    /// 场景会打开成一片 Missing Prefab。
    ///
    /// 清单由 <see cref="AssetDatabase.GetDependencies"/> 递归求闭包得出，不是手写的：
    /// 手写一定会漏，而漏掉的那一个在干净检出里就是一个缺失的引用。
    ///
    /// 同时把每个资产自己的 `.meta` 与所有父目录的 `.meta` 一并列入 ——
    /// 少了目录 meta，Unity 会给那个目录重新生成一个新 GUID，
    /// 于是所有指向它的引用在别人机器上全部指错。
    /// </summary>
    public static class P23SceneDependencyManifest
    {
        public const string HighElvesRoot = "Assets/Aquarius Fantasy - High Elves";

        public const string HighElvesSourceScene =
            HighElvesRoot + "/Demo Scenes/High Elves Sanctuary/High Elves Sanctuary.unity";

        public const string ManifestPath = "Docs/Scenes/p23-high-elves-dependencies.txt";

        /// <summary>需要走 Git LFS 的扩展名。大二进制进普通 Git 历史会让仓库永久变胖。</summary>
        private static readonly string[] LfsExtensions =
        {
            ".png", ".jpg", ".jpeg", ".tga", ".psd", ".tif", ".tiff",
            ".fbx", ".exr", ".hdr", ".wav", ".ogg", ".mp3", ".mp4", ".unitypackage"
        };

        [MenuItem("NARAKA/Setup/Generate P2.3 Scene Dependency Manifest")]
        public static void Generate()
        {
            var roots = new[]
            {
                HighElvesSourceScene,
                P2SceneSetup.Map01ScenePath
            };

            foreach (var root in roots)
            {
                if (!File.Exists(root))
                {
                    throw new InvalidOperationException($"清单源场景不存在：{root}");
                }
            }

            // 先刷新资产库。GetDependencies 读的是资产库缓存的依赖图，
            // 而场景是被装配工具在上一个会话里改过的。实测过一次：
            // 演示角色已经从 Map01_Task 里删干净了，缓存却还认为它被引用，
            // 于是"运行必需 / 仅重建需要"这两个数字是错的。
            // 清单是可复现性的依据，不能建在可能过期的缓存上。
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            foreach (var root in roots)
            {
                AssetDatabase.ImportAsset(root, ImportAssetOptions.ForceUpdate);
            }

            // 1) 递归依赖闭包。分别求两个根的闭包，因为它们回答的是两个不同的问题：
            //    Map01_Task 的闭包 = **跑起来**需要什么；
            //    源场景的闭包      = **从源场景重建**需要什么（Rebuild 菜单）。
            //    两者的差集里会有演示专用资产（例如演示用的 FPS 角色），
            //    它们不是"盲目提交的无关内容"，而是源场景自身的引用 ——
            //    不带上它们，Rebuild 在干净检出里就会打开成一堆 Missing。
            var runtimeClosure = ClosureOf(P2SceneSetup.Map01ScenePath);
            var rebuildClosure = ClosureOf(HighElvesSourceScene);
            var closure = new SortedSet<string>(runtimeClosure, StringComparer.Ordinal);
            closure.UnionWith(rebuildClosure);

            // 2) 只保留 High Elves 目录里的部分；闭包里属于 Assets/Game 的东西
            //    早就在跟踪了，不是本轮要补的。落在别处的单独报出来，不静默忽略。
            var inHighElves = new SortedSet<string>(StringComparer.Ordinal);
            var elsewhere = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var path in closure)
            {
                if (path.StartsWith(HighElvesRoot + "/", StringComparison.Ordinal))
                {
                    inHighElves.Add(path);
                }
                else if (!path.StartsWith("Assets/Game/", StringComparison.Ordinal) &&
                         !path.StartsWith("Packages/", StringComparison.Ordinal))
                {
                    elsewhere.Add(path);
                }
            }

            // 3) 每个资产的 .meta，以及所有父目录的 .meta。
            var required = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var path in inHighElves)
            {
                required.Add(path);
                required.Add(path + ".meta");
                AddAncestorMetas(path, required);
            }

            // 4) 写清单。
            var rebuildOnly = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var path in inHighElves)
            {
                if (!runtimeClosure.Contains(path))
                {
                    rebuildOnly.Add(path);
                }
            }

            var report = BuildReport(required, inHighElves, elsewhere, rebuildOnly);
            Directory.CreateDirectory(Path.GetDirectoryName(ManifestPath) ?? ".");
            File.WriteAllText(ToRepositoryPath(ManifestPath), report.Manifest, new UTF8Encoding(false));

            Debug.Log(report.Summary);
        }

        private static SortedSet<string> ClosureOf(string root)
        {
            var closure = new SortedSet<string>(StringComparer.Ordinal) { root };
            foreach (var dependency in AssetDatabase.GetDependencies(root, true))
            {
                if (IsProjectAsset(dependency))
                {
                    closure.Add(dependency.Replace('\\', '/'));
                }
            }

            return closure;
        }

        private static void AddAncestorMetas(string assetPath, ISet<string> into)
        {
            var directory = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            while (!string.IsNullOrEmpty(directory) &&
                   directory.StartsWith("Assets", StringComparison.Ordinal) &&
                   directory != "Assets")
            {
                into.Add(directory + ".meta");
                directory = Path.GetDirectoryName(directory)?.Replace('\\', '/');
            }
        }

        private sealed class Report
        {
            public string Manifest;
            public string Summary;
        }

        private static Report BuildReport(
            SortedSet<string> required,
            SortedSet<string> assets,
            SortedSet<string> elsewhere,
            SortedSet<string> rebuildOnly)
        {
            long lfsBytes = 0;
            long plainBytes = 0;
            var missing = new List<string>();
            var byExtension = new SortedDictionary<string, int[]>(StringComparer.Ordinal);
            var lfsCount = 0;

            foreach (var path in required)
            {
                var full = ToRepositoryPath(path);
                if (!File.Exists(full))
                {
                    missing.Add(path);
                    continue;
                }

                var length = new FileInfo(full).Length;
                var extension = Path.GetExtension(path).ToLowerInvariant();
                if (extension == ".meta")
                {
                    extension = ".meta";
                }

                if (!byExtension.TryGetValue(extension, out var tally))
                {
                    tally = new int[1];
                    byExtension[extension] = tally;
                }

                tally[0]++;

                if (NeedsLfs(path))
                {
                    lfsBytes += length;
                    lfsCount++;
                }
                else
                {
                    plainBytes += length;
                }
            }

            var manifest = new StringBuilder();
            manifest.AppendLine("# P2.3 High Elves 运行依赖清单");
            manifest.AppendLine("#");
            manifest.AppendLine("# 由 NARAKA/Setup/Generate P2.3 Scene Dependency Manifest 生成，不要手工编辑。");
            manifest.AppendLine("# 闭包根：");
            manifest.AppendLine("#   " + HighElvesSourceScene);
            manifest.AppendLine("#   " + P2SceneSetup.Map01ScenePath);
            manifest.AppendLine("#");
            manifest.AppendLine($"# 资产 {assets.Count} 个，连 .meta 与父目录 meta 共 {required.Count} 条。");
            manifest.AppendLine(
                $"# 其中 {assets.Count - rebuildOnly.Count} 个是运行 Map01_Task 必需的，" +
                $"{rebuildOnly.Count} 个只是源场景自己的引用（供 Rebuild 用）。");
            manifest.AppendLine($"# 需走 LFS {lfsCount} 个（{Megabytes(lfsBytes)}），普通 Git {Megabytes(plainBytes)}。");
            manifest.AppendLine("#");
            manifest.AppendLine("# 路径相对**仓库根**（含 NK/ 前缀），");
            manifest.AppendLine("# 因此 git 与 Tools/CI/Test-TrackedUnitySceneDependencies.ps1 可以直接使用。");
            manifest.AppendLine();
            foreach (var path in required)
            {
                manifest.AppendLine(ToRepositoryRelative(path));
            }

            var summary = new StringBuilder();
            summary.AppendLine("P2.3 场景依赖闭包：");
            summary.AppendLine($"  闭包资产        {assets.Count}");
            summary.AppendLine($"    运行必需      {assets.Count - rebuildOnly.Count}");
            summary.AppendLine($"    仅重建需要    {rebuildOnly.Count}（源场景自身引用）");
            summary.AppendLine($"    仅重建体积    {Megabytes(SizeOf(rebuildOnly))}");
            summary.AppendLine($"  含 meta 总条目  {required.Count}");
            summary.AppendLine($"  需走 LFS        {lfsCount} 个，{Megabytes(lfsBytes)}");
            summary.AppendLine($"  普通 Git        {Megabytes(plainBytes)}");
            summary.AppendLine("  按扩展名：");
            foreach (var pair in byExtension)
            {
                summary.AppendLine($"    {pair.Key,-14} {pair.Value[0]}");
            }

            if (elsewhere.Count > 0)
            {
                summary.AppendLine(
                    $"  [注意] 闭包里有 {elsewhere.Count} 条既不在 High Elves 也不在 Assets/Game：" +
                    string.Join("、", elsewhere.Take(10)));
            }

            if (missing.Count > 0)
            {
                summary.AppendLine(
                    $"  [警告] {missing.Count} 条清单项在磁盘上不存在：" +
                    string.Join("、", missing.Take(10)));
            }

            summary.AppendLine($"  清单已写出      {ManifestPath}");
            return new Report { Manifest = manifest.ToString(), Summary = summary.ToString() };
        }

        /// <summary>
        /// Unity 资产路径（<c>Assets/...</c>）转成仓库相对路径（<c>NK/Assets/...</c>）。
        /// 仓库根在 Unity 工程目录的上一层，清单消费方是 git 与 CI 脚本，
        /// 它们都以仓库根为基准。
        /// </summary>
        private static string ToRepositoryRelative(string assetPath) =>
            assetPath.StartsWith("Assets/", StringComparison.Ordinal)
                ? "NK/" + assetPath
                : assetPath;

        private static long SizeOf(IEnumerable<string> assetPaths)
        {
            long total = 0;
            foreach (var path in assetPaths)
            {
                var full = ToRepositoryPath(path);
                if (File.Exists(full))
                {
                    total += new FileInfo(full).Length;
                }
            }

            return total;
        }

        public static bool NeedsLfs(string path)
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();
            return Array.IndexOf(LfsExtensions, extension) >= 0;
        }

        /// <summary>内建资源与包内资源不属于本仓库，不能进清单。</summary>
        private static bool IsProjectAsset(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            var normalized = path.Replace('\\', '/');
            return normalized.StartsWith("Assets/", StringComparison.Ordinal) &&
                   !normalized.StartsWith("Assets/InitTestScene", StringComparison.Ordinal);
        }

        /// <summary>
        /// Unity 的资产路径以 <c>Assets/</c> 开头，而仓库根在 Unity 工程目录的上一层，
        /// 因此落盘时要补上 <c>NK/</c>；清单里存的是**仓库相对路径**，
        /// 这样 Git 与 CI 脚本可以直接用。
        /// </summary>
        private static string ToRepositoryPath(string path)
        {
            if (path.StartsWith("Docs/", StringComparison.Ordinal) ||
                path.StartsWith("Tools/", StringComparison.Ordinal))
            {
                return Path.Combine(RepositoryRoot, path);
            }

            return Path.Combine(RepositoryRoot, "NK", path);
        }

        private static string RepositoryRoot
        {
            get
            {
                var project = Directory.GetParent(UnityEngine.Application.dataPath);
                var repository = project == null ? null : Directory.GetParent(project.FullName);
                if (repository == null)
                {
                    throw new InvalidOperationException("无法从 Application.dataPath 定位仓库根目录。");
                }

                return repository.FullName;
            }
        }

        private static string Megabytes(long bytes) => $"{bytes / 1048576.0:F1} MB";
    }
}
