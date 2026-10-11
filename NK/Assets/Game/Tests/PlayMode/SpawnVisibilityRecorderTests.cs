using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using Cysharp.Threading.Tasks;
using Naraka.Boot;
using Naraka.Features.Character.Model;
using Naraka.Features.Character.View;
using Naraka.Features.Loading.Controller;
using Naraka.Features.World.Controller;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VContainer;

namespace Naraka.P2.PlayMode.Tests
{
    /// <summary>
    /// 出场动画"看不到"的运行期测量（诊断，不是验收测试）。
    ///
    /// 从大厅走真实流程进入地图一，逐帧记录：真实帧时长、游戏帧时长、加载界面是否还盖着、
    /// 业务出场状态走了多久、Animator 实际播到片段的哪里、相机在哪。
    /// 它回答的问题是：玩家第一次能看到画面时，出场动画已经播到哪儿了。
    ///
    /// 标成 <see cref="ExplicitAttribute"/>：常规全量测试不跑它，只在指名运行时执行，
    /// 不断言任何东西，只写出 <c>artifacts/ci/unity/pre-p4-spawn-visibility.csv</c>。
    /// </summary>
    [Explicit("诊断：只在指名运行时执行。")]
    public sealed class SpawnVisibilityRecorderTests
    {
        private const string OutputPath = "artifacts/ci/unity/pre-p4-spawn-visibility.csv";
        private const string ShotDirectory = "artifacts/ci/unity/pre-p4-spawn-shots";
        private static readonly int[] ShotFrames = { 0, 1, 3, 10, 30, 60, 100, 140, 180 };
        private int _previousFrameRate;

        [SetUp]
        public void SetUp()
        {
            DestroyPersistentRoot();
            _previousFrameRate = Application.targetFrameRate;
            Application.targetFrameRate = 60;
        }

        [TearDown]
        public void TearDown()
        {
            Application.targetFrameRate = _previousFrameRate;
            DestroyPersistentRoot();
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator RecordSpawnFromLobbyToMap01()
        {
            SceneManager.LoadScene("SampleScene", LoadSceneMode.Single);
            yield return null;
            yield return null;
            Assert.That(AppRootLifetimeScope.Instance, Is.Not.Null);
            yield return null;

            var container = AppRootLifetimeScope.Instance.Container;
            var world = container.Resolve<IWorldFlowController>();
            var loading = container.Resolve<ILoadingController>();
            var entering = world.EnterMap01Async(default).Preserve();

            var csv = new StringBuilder();
            csv.AppendLine("frame,realtime,unscaledDt,dt,loadingVisible,progress,scene,hasPlayer,action,actionAnim," +
                           "animatorState,normalizedTime,animatorSpeed,camX,camY,camZ,playerY,camToPlayer,rendererVisible,timeScale");
            var start = Time.realtimeSinceStartup;
            var frame = 0;
            var spawnSeen = false;
            var hiddenAt = -1;
            var deadline = start + 60f;
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                frame++;
                var player = Object.FindObjectOfType<PlayerCharacterView>();
                var animator = player != null ? player.GetComponentInChildren<Animator>() : null;
                var projector = player != null ? player.GetComponentInChildren<PlayerAnimatorProjector>() : null;
                var info = animator != null ? animator.GetCurrentAnimatorStateInfo(0) : default;
                var state = animator != null ? StateName(info.shortNameHash) : "-";
                var cam = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
                var action = player != null ? player.State.Action : ActionState.None;
                if (action == ActionState.SpawnLobbyToMap01)
                {
                    spawnSeen = true;
                }

                csv.AppendLine(string.Join(",",
                    frame,
                    F(Time.realtimeSinceStartup - start),
                    F(Time.unscaledDeltaTime),
                    F(Time.deltaTime),
                    loading.Current.IsVisible ? 1 : 0,
                    F(loading.Current.Progress),
                    SceneManager.GetActiveScene().name,
                    player != null ? 1 : 0,
                    action,
                    projector != null ? projector.CurrentAnimation.ToString() : "-",
                    state,
                    F(info.normalizedTime),
                    F(animator != null ? animator.speed : 0f),
                    F(cam.x), F(cam.y), F(cam.z),
                    F(player != null ? player.transform.position.y : 0f),
                    F(player != null ? Vector3.Distance(cam, player.transform.position) : 0f),
                    AnyVisible(player) ? 1 : 0,
                    F(Time.timeScale)));

                if (hiddenAt < 0 && !loading.Current.IsVisible && player != null)
                {
                    hiddenAt = frame;
                }

                // 加载界面消失后的若干时刻截一张游戏画面：玩家实际看到的是什么。
                // 只在有图形设备时有效（-nographics 下 WaitForEndOfFrame 不会返回）。
                if (hiddenAt >= 0 && !Application.isBatchMode && System.Array.IndexOf(ShotFrames, frame - hiddenAt) >= 0)
                {
                    yield return new WaitForEndOfFrame();
                    var shot = ScreenCapture.CaptureScreenshotAsTexture();
                    Directory.CreateDirectory(ShotDirectory);
                    File.WriteAllBytes(
                        Path.Combine(ShotDirectory, $"spawn-{frame - hiddenAt:000}.png"), shot.EncodeToPNG());
                    Object.Destroy(shot);
                }

                if (spawnSeen && action != ActionState.SpawnLobbyToMap01 && entering.Status.IsCompleted())
                {
                    // 出场结束后再多录一秒，看交还 Idle 那一段。
                    for (var i = 0; i < 60; i++)
                    {
                        yield return null;
                    }

                    break;
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath) ?? ".");
            File.WriteAllText(OutputPath, csv.ToString(), new UTF8Encoding(false));
            Debug.Log($"[出场可见性] 已写出 {OutputPath}");
        }

        private static bool AnyVisible(PlayerCharacterView player)
        {
            if (player == null)
            {
                return false;
            }

            foreach (var renderer in player.GetComponentsInChildren<Renderer>())
            {
                if (renderer.isVisible)
                {
                    return true;
                }
            }

            return false;
        }

        private static string StateName(int hash)
        {
            foreach (var field in typeof(PlayerAnimatorProjector.StateNames).GetFields())
            {
                if (field.GetValue(null) is string name && Animator.StringToHash(name) == hash)
                {
                    return name;
                }
            }

            return hash.ToString(CultureInfo.InvariantCulture);
        }

        private static string F(float value) => value.ToString("0.0000", CultureInfo.InvariantCulture);

        private static void DestroyPersistentRoot()
        {
            foreach (var root in Object.FindObjectsOfType<AppRootLifetimeScope>())
            {
                Object.DestroyImmediate(root.gameObject);
            }
        }
    }
}
