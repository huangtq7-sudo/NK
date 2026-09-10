using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using Naraka.Features.Lobby.Controller;
using UnityEngine.SceneManagement;

namespace Naraka.Infrastructure.Scene
{
    /// <summary>
    /// 通过Unity SceneManager实现地图加载。业务层只依赖ILobbySceneGateway。
    /// </summary>
    public sealed class UnityLobbySceneGateway : ILobbySceneGateway
    {
        public async UniTask LoadMapAsync(string sceneName, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                throw new ArgumentException("场景名不能为空。", nameof(sceneName));
            }

            if (!IsSceneInBuild(sceneName))
            {
                throw new InvalidOperationException(
                    $"场景 '{sceneName}' 未加入 Build Settings 的 Scenes In Build。");
            }

            var operation = SceneManager.LoadSceneAsync(sceneName);
            if (operation == null)
            {
                throw new InvalidOperationException($"无法加载场景 '{sceneName}'，请检查场景名称。");
            }

            operation.allowSceneActivation = true;
            await operation.ToUniTask(cancellationToken: cancellationToken);
        }

        private static bool IsSceneInBuild(string sceneName)
        {
            for (var i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
            {
                var path = SceneUtility.GetScenePathByBuildIndex(i);
                if (string.Equals(Path.GetFileNameWithoutExtension(path), sceneName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
