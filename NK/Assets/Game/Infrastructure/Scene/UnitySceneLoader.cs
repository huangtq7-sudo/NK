using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using Naraka.Core.Application.Scenes;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Naraka.Infrastructure.Scene
{
    /// <summary>
    /// 基于 Unity <c>SceneManager.LoadSceneAsync</c> 的场景加载。
    /// 本阶段明确不引入 Addressables；业务层只依赖 <see cref="ISceneLoader"/>，
    /// 因此后续迁移不需要改动 Controller。
    /// </summary>
    public sealed class UnitySceneLoader : ISceneLoader
    {
        /// <summary>Unity 在 allowSceneActivation=false 时，progress 最高停在这个值。</summary>
        private const float ActivationThreshold = 0.9f;

        public ISceneLoadOperation BeginLoad(string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                throw new ArgumentException("场景名不能为空。", nameof(sceneName));
            }

            if (!IsSceneInBuild(sceneName))
            {
                var error = $"场景 '{sceneName}' 未加入 Build Settings 的 Scenes In Build。";
                Debug.LogError(error);
                throw new InvalidOperationException(error);
            }

            AsyncOperation operation;
            try
            {
                operation = SceneManager.LoadSceneAsync(sceneName);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                throw;
            }

            if (operation == null)
            {
                var error = $"无法加载场景 '{sceneName}'，请检查场景名称。";
                Debug.LogError(error);
                throw new InvalidOperationException(error);
            }

            // 激活时机交给调用方：加载条必须先走完最短显示时长才允许切换场景。
            operation.allowSceneActivation = false;
            return new Operation(operation, sceneName);
        }

        public static bool IsSceneInBuild(string sceneName)
        {
            for (var i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
            {
                var path = SceneUtility.GetScenePathByBuildIndex(i);
                if (string.Equals(
                        Path.GetFileNameWithoutExtension(path), sceneName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private sealed class Operation : ISceneLoadOperation
        {
            private readonly AsyncOperation _operation;
            private readonly string _sceneName;

            public Operation(AsyncOperation operation, string sceneName)
            {
                _operation = operation;
                _sceneName = sceneName;
            }

            /// <summary>把 Unity 的 0–0.9 换算成 0–1，避免进度条永远停在 90%。</summary>
            public float Progress
            {
                get
                {
                    var raw = _operation.progress / ActivationThreshold;
                    return raw < 0f ? 0f : raw > 1f ? 1f : raw;
                }
            }

            public bool IsReadyToActivate => _operation.progress >= ActivationThreshold;

            public void Activate() => _operation.allowSceneActivation = true;

            public async UniTask WaitForCompletionAsync(CancellationToken cancellationToken)
            {
                try
                {
                    await _operation.ToUniTask(cancellationToken: cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    Debug.LogError($"场景 '{_sceneName}' 加载失败。");
                    Debug.LogException(exception);
                    throw;
                }
            }
        }
    }
}
