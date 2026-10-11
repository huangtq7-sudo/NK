using System.Collections;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Naraka.Boot;
using Naraka.Features.Character.Model;
using Naraka.Features.Character.View;
using Naraka.Features.Loading.Controller;
using Naraka.Features.Loading.View;
using Naraka.Features.Lobby.Controller;
using Naraka.Features.Lobby.Model;
using Naraka.Core.Application.Bootstrap;
using Naraka.Features.World.Controller;
using Naraka.Features.World.View;
using Naraka.Infrastructure.Camera;
using Naraka.Infrastructure.Input;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using VContainer;
using Naraka.Core.Application.Scenes;

namespace Naraka.P2.PlayMode.Tests
{
    /// <summary>
    /// P2 世界流转的 PlayMode 验收。
    ///
    /// 这些测试走真实路径：真实场景加载、真实 CharacterController、真实 Cinemachine
    /// 与真实 Input System 虚拟设备，包括 2 秒最短加载时长本身。
    /// </summary>
    public sealed class P2WorldPlayModeTests
    {
        /// <summary>最短加载时长本身就是 2 秒，超时必须留出足够余量。</summary>
        private const float SceneLoadTimeoutSeconds = 30f;

        /// <summary>
        /// Input System 的测试夹具。批处理模式下没有真实输入后端，
        /// 直接 QueueStateEvent 的事件根本不会被处理；夹具会装上测试用运行时，
        /// 排队的事件才会在 dynamic update 里生效（下一帧可见）。
        ///
        /// 夹具必须在加载 Bootstrap 场景之前建立：它会重置输入系统，
        /// 之后游戏自己的 Action Map 才会绑定到这里创建的虚拟设备。
        /// </summary>
        private InputTestFixture _input;

        private Keyboard _keyboard;
        private Mouse _mouse;

        [SetUp]
        public void SetUp()
        {
            // 先清掉可能从上一组测试残留下来的持久化根。它的输入提供者已经启用了
            // 共享的 InputActionAsset；如果带着这份状态去重置输入系统，
            // Action Map 会留在旧的绑定上，之后新建的虚拟设备一个都读不到。
            DestroyPersistentRoot();

            _input = new InputTestFixture();
            _input.Setup();
            _keyboard = InputSystem.AddDevice<Keyboard>();
            _mouse = InputSystem.AddDevice<Mouse>();
        }

        [TearDown]
        public void TearDown()
        {
            // 持久化根跨场景存活，必须显式清掉，否则下一个测试会看到上一条命的世界。
            DestroyPersistentRoot();

            _input?.TearDown();
            _input = null;
            _keyboard = null;
            _mouse = null;
        }

        private static void DestroyPersistentRoot()
        {
            foreach (var root in Object.FindObjectsOfType<AppRootLifetimeScope>())
            {
                Object.DestroyImmediate(root.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator BootSceneStillStartsAndHostsExactlyOnePersistentRoot()
        {
            yield return LoadBootScene();

            var roots = Object.FindObjectsOfType<AppRootLifetimeScope>();
            Assert.That(roots.Length, Is.EqualTo(1), "Bootstrap 场景必须只有一个持久化组合根。");
            Assert.That(roots[0].Container, Is.Not.Null, "持久化根的容器必须已经构建。");

            var gameScopes = Object.FindObjectsOfType<GameLifetimeScope>();
            Assert.That(gameScopes.Length, Is.EqualTo(1));
            Assert.That(gameScopes[0].Parent, Is.SameAs(roots[0]), "大厅 Scope 必须挂在持久化根之下。");
        }

        [UnityTest]
        public IEnumerator ReloadingBootstrapDoesNotAccumulateASecondRoot()
        {
            yield return LoadBootScene();
            yield return LoadBootScene();

            Assert.That(
                Object.FindObjectsOfType<AppRootLifetimeScope>().Length, Is.EqualTo(1),
                "重复进入 Bootstrap 不得累积第二个组合根。");
        }

        [UnityTest]
        public IEnumerator LoadingScreenLivesOnThePersistentRootAndSurvivesSceneChanges()
        {
            yield return LoadBootScene();

            var loadingView = Object.FindObjectOfType<LoadingView>();
            Assert.That(loadingView, Is.Not.Null);
            Assert.That(
                loadingView.GetComponentInParent<AppRootLifetimeScope>(), Is.Not.Null,
                "加载界面必须挂在持久化根上，否则切场景会丢失。");

            var document = loadingView.GetComponent<UIDocument>();
            Assert.That(document, Is.Not.Null);
            Assert.That(
                document.rootVisualElement.Q<VisualElement>("LoadingScreen"), Is.Not.Null);
            Assert.That(document.rootVisualElement.Q<VisualElement>("LoadingBarFill"), Is.Not.Null);

            yield return EnterMap01();

            Assert.That(
                Object.FindObjectsOfType<LoadingView>().Length, Is.EqualTo(1),
                "切换场景后加载界面既不能丢失也不能重复。");
        }

        [UnityTest]
        public IEnumerator RealLoadingProgressAdvancesWhileEnteringMap01()
        {
            yield return LoadBootScene();
            var loading = Resolve<ILoadingController>();
            var sawVisible = false;
            var maxProgress = 0f;

            var world = Resolve<IWorldFlowController>();
            var task = world.EnterMap01Async(default).Preserve();

            // 按真实时间等，不按帧数：最短显示时长是 2 秒的真实时间，
            // 用帧数当预算会在快机器上刚好在 2 秒边界上超时。
            var deadline = Time.realtimeSinceStartup + SceneLoadTimeoutSeconds;
            while (!task.Status.IsCompleted() && Time.realtimeSinceStartup < deadline)
            {
                var state = loading.Current;
                if (state.IsVisible)
                {
                    sawVisible = true;
                    maxProgress = Mathf.Max(maxProgress, state.Progress);
                }

                yield return null;
            }

            Assert.That(task.Status.IsCompleted(), Is.True, "进入地图一没有在超时前完成。");

            Assert.That(sawVisible, Is.True, "开始游戏必须显示 LoadingScreen。");
            Assert.That(maxProgress, Is.GreaterThan(0f), "真实加载进度必须推进。");
            Assert.That(loading.Current.HasError, Is.False);
            Assert.That(
                SceneManager.GetActiveScene().name, Is.EqualTo(WorldSceneNames.Map01Task));
        }

        [UnityTest]
        public IEnumerator TheLoadingLabelStartsAtZeroAndClimbsThroughRealRenderFrames()
        {
            // 这条测试读的是**真实 LoadingView 的 Label 文本**，不是 Controller 的状态，
            // 因为用户看到的是前者。它每帧采一次，覆盖的是用户实测到的两种现象：
            // 进度条"一次性显示完成"，以及"第一次看到就已经在 50% 左右"。
            //
            // 场景用的是正式 Map01_Task（High Elves），不缩小规模、不换回灰盒。
            yield return LoadBootScene();

            var view = Object.FindObjectOfType<LoadingView>(true);
            Assert.That(view, Is.Not.Null, "持久化 App Root 上必须有 LoadingView。");

            var document = view.GetComponent<UIDocument>();
            Assert.That(document, Is.Not.Null);
            var label = document.rootVisualElement.Q<Label>("LoadingPercentLabel");
            var screen = document.rootVisualElement.Q<VisualElement>("LoadingScreen");
            Assert.That(label, Is.Not.Null, "LoadingScreen.uxml 必须有 LoadingPercentLabel。");
            Assert.That(screen, Is.Not.Null);

            var samples = new System.Collections.Generic.List<int>();
            var fullWhileSceneNotReady = 0;

            var world = Resolve<IWorldFlowController>();
            var task = world.EnterMap01Async(default).Preserve();

            var deadline = Time.realtimeSinceStartup + SceneLoadTimeoutSeconds;
            while (!task.Status.IsCompleted() && Time.realtimeSinceStartup < deadline)
            {
                if (screen.resolvedStyle.display != DisplayStyle.None &&
                    TryReadPercent(label.text, out var percent))
                {
                    // 只在值变化时记录：同一个值连续多帧不代表进度跳动。
                    if (samples.Count == 0 || samples[samples.Count - 1] != percent)
                    {
                        samples.Add(percent);
                    }

                    if (percent >= 100 &&
                        SceneManager.GetActiveScene().name != WorldSceneNames.Map01Task)
                    {
                        fullWhileSceneNotReady++;
                    }
                }

                yield return null;
            }

            Assert.That(task.Status.IsCompleted(), Is.True, "进入地图一没有在超时前完成。");
            Assert.That(samples, Is.Not.Empty, "一帧都没有观察到加载界面上的百分比。");

            // 1) 第一帧必须是 0%。
            Assert.That(
                samples[0], Is.Zero,
                $"第一个观察到的百分比是 {samples[0]}%，必须从 0% 开始。" +
                $"完整序列：{string.Join(", ", samples)}");

            // 2) 必须出现 1%–99% 的中间值，否则就是"一次性加载完成"。
            var intermediate = samples.Count(v => v >= 1 && v <= 99);
            Assert.That(
                intermediate, Is.GreaterThan(0),
                "没有观察到任何 1%–99% 的中间值，进度条是一次性跳到完成的。" +
                $"完整序列：{string.Join(", ", samples)}");

            // 3) 必须出现 100%，并且至少占住一帧（上面按变化记录，出现即说明被画过）。
            Assert.That(
                samples, Does.Contain(100),
                $"从未观察到 100%。完整序列：{string.Join(", ", samples)}");

            // 4) 单调不回退。
            for (var i = 1; i < samples.Count; i++)
            {
                Assert.That(
                    samples[i], Is.GreaterThanOrEqualTo(samples[i - 1]),
                    $"百分比回退了：{samples[i - 1]}% -> {samples[i]}%。" +
                    $"完整序列：{string.Join(", ", samples)}");
            }

            // 5) 场景真正就绪之前不得显示 100%。
            Assert.That(
                fullWhileSceneNotReady, Is.Zero,
                "场景还没切到 Map01_Task 就已经显示 100%。");
        }

        private static bool TryReadPercent(string text, out int percent)
        {
            percent = 0;
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            var trimmed = text.EndsWith("%") ? text.Substring(0, text.Length - 1) : text;
            return int.TryParse(trimmed, out percent);
        }

        [UnityTest]
        public IEnumerator PlayerSpawnsAtTheEntryPointAndPlaysTheLobbyBurst()
        {
            yield return LoadBootScene();
            yield return EnterMap01();

            var player = Object.FindObjectOfType<PlayerCharacterView>();
            Assert.That(player, Is.Not.Null, "地图一必须生成正式角色。");

            var spawn = Object.FindObjectsOfType<PlayerSpawnPoint>()
                .First(p => p.Kind == PlayerSpawnPoint.SpawnKind.Entry);
            Assert.That(
                Vector3.Distance(player.transform.position, spawn.transform.position),
                Is.LessThan(1.5f),
                "角色必须生成在出生点附近。");

            yield return WaitUntil(
                () => player.State.Action == ActionState.SpawnLobbyToMap01, 6f);
            Assert.That(
                player.State.Action, Is.EqualTo(ActionState.SpawnLobbyToMap01),
                "从大厅进入地图一必须播放 Burst02 出场状态。");
            Assert.That(player.State.Flags.HasFlag(PlayerOverlayFlags.InputLocked), Is.True,
                "出场期间输入必须锁定。");
        }

        [UnityTest]
        public IEnumerator InputIsRestoredAfterTheSpawnAnimation()
        {
            yield return LoadBootScene();
            yield return EnterMap01();
            var player = Object.FindObjectOfType<PlayerCharacterView>();

            yield return WaitUntil(() => player.State.Action == ActionState.None, 8f);

            Assert.That(player.State.Action, Is.EqualTo(ActionState.None));
            Assert.That(player.State.Locomotion, Is.EqualTo(LocomotionState.Idle),
                "出场结束后必须进入默认 Idle。");
            Assert.That(player.State.Flags.HasFlag(PlayerOverlayFlags.InputLocked), Is.False,
                "出场结束后必须恢复输入。");

            var input = Object.FindObjectOfType<NarakaPlayerInputProvider>();
            Assert.That(input.IsPlayerInputEnabled, Is.True, "Player Action Map 必须重新启用。");
        }

        [UnityTest]
        public IEnumerator Map01SpawnAnimationActuallyPlaysInTheAnimator()
        {
            yield return LoadBootScene();
            yield return EnterMap01();
            var player = Object.FindObjectOfType<PlayerCharacterView>();

            // 业务状态进了出场态之后，Animator 必须真的在播 Burst02，而不是停在 Idle。
            yield return WaitUntil(
                () => player.State.Action == ActionState.SpawnLobbyToMap01, 6f, "map01-burst-state");

            var animator = player.GetComponentInChildren<Animator>();
            Assert.That(animator, Is.Not.Null);
            var expected = Animator.StringToHash(
                PlayerAnimatorProjector.StateNames.SpawnBurstLobbyToMap01);

            // 交叉淡入需要一两帧，等它落到目标 State。
            yield return WaitUntil(
                () => animator.GetCurrentAnimatorStateInfo(0).shortNameHash == expected,
                3f,
                "map01-burst-animator");

            // 动作全程都必须留在这个 State 上，而且 Animator 的播放倍率要跟着业务速度走。
            var spawn = player.Tuning.Reaction.SpawnLobbyToMap01;
            var sawMainSpeed = false;
            var sawRecoverySpeed = false;
            var deadline = Time.realtimeSinceStartup + (spawn.RealDurationSeconds * 0.85f);
            while (Time.realtimeSinceStartup < deadline)
            {
                Assert.That(
                    animator.GetCurrentAnimatorStateInfo(0).shortNameHash, Is.EqualTo(expected),
                    "出场动画播放途中 Animator 不得离开 Burst02。");
                if (Mathf.Abs(animator.speed - spawn.Playback.MainSpeed) < 0.01f)
                {
                    sawMainSpeed = true;
                }

                if (Mathf.Abs(animator.speed - spawn.Playback.RecoverySpeed) < 0.01f)
                {
                    sawRecoverySpeed = true;
                }

                yield return null;
            }

            Assert.That(sawMainSpeed, Is.True, "应当观察到主体播放倍率。");
            Assert.That(sawRecoverySpeed, Is.True, "应当观察到后摇播放倍率。");

            yield return WaitUntil(() => player.State.Action == ActionState.None, 8f, "map01-burst-end");
            Assert.That(player.State.Locomotion, Is.EqualTo(LocomotionState.Idle));
        }

        [UnityTest]
        public IEnumerator SpawnWaitsForTheLoadingScreenAndTheCameraGlidesInFromAboveBehind()
        {
            yield return LoadBootScene();
            var loading = Resolve<ILoadingController>();
            var world = Resolve<IWorldFlowController>();
            var task = world.EnterMap01Async(default).Preserve();

            // 加载界面还盖着的时候，出场动作一帧都不能开始：那几帧玩家看不见。
            var deadline = Time.realtimeSinceStartup + SceneLoadTimeoutSeconds;
            PlayerCharacterView player = null;
            while (!task.Status.IsCompleted() && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                player = Object.FindObjectOfType<PlayerCharacterView>();
                if (player != null && loading.Current.IsVisible)
                {
                    Assert.That(
                        player.State.Action, Is.Not.EqualTo(ActionState.SpawnLobbyToMap01),
                        "加载界面还没撤掉，出场动作就开始了。");
                }
            }

            Assert.That(task.Status.IsCompleted(), Is.True, "进入地图一没有在超时前完成。");
            player = Object.FindObjectOfType<PlayerCharacterView>();
            Assert.That(player, Is.Not.Null);
            var rig = Object.FindObjectOfType<ThirdPersonCameraRig>();
            Assert.That(rig, Is.Not.Null);
            var settings = rig.Settings;

            yield return WaitUntil(
                () => player.State.Action == ActionState.SpawnLobbyToMap01, 1f, "spawn-after-loading");
            Assert.That(loading.Current.IsVisible, Is.False);

            // 出场色幕：加载界面撤掉的那一刻出现，白色、约 60% 不透明，一路变淡，约 1.5 秒后消失。
            var entry = Object.FindObjectOfType<WorldSceneEntry>();
            var fade = entry.EntryFade;
            Assert.That(fade, Is.Not.Null, "进入任务界面必须播放出场色幕。");
            Assert.That(fade.Alpha, Is.EqualTo(0.6f).Within(0.08f), "色幕从半透明开始。");
            Assert.That(fade.Color.r, Is.EqualTo(1f).Within(0.001f));
            Assert.That(fade.Color.b, Is.EqualTo(1f).Within(0.001f));
            var fadeStarted = Time.time;

            // 动作从头开始播：Animator 落到出场 State 时片段进度还在开头。
            var animator = player.GetComponentInChildren<Animator>();
            var burst = Animator.StringToHash(PlayerAnimatorProjector.StateNames.SpawnBurstLobbyToMap01);
            yield return WaitUntil(
                () => animator.GetCurrentAnimatorStateInfo(0).shortNameHash == burst, 1f, "spawn-animator");
            Assert.That(animator.GetCurrentAnimatorStateInfo(0).normalizedTime, Is.LessThan(0.1f),
                "玩家第一眼看到的必须是出场动作的开头。");

            // 镜头从后上方拉回常规位置：俯仰角与距离一路收小，出场结束时回到常规值。
            Assert.That(rig.IsIntroActive, Is.True, "出场开始时镜头应处在出场镜头里。");
            var pivotHeight = settings.PivotHeight;
            var camera = Camera.main;
            float Elevation()
            {
                var pivot = player.transform.position + (Vector3.up * pivotHeight);
                var offset = camera.transform.position - pivot;
                return Mathf.Asin(Mathf.Clamp(offset.y / Mathf.Max(0.001f, offset.magnitude), -1f, 1f)) *
                       Mathf.Rad2Deg;
            }

            yield return null;
            var firstElevation = Elevation();
            var firstDistance = rig.CurrentDistance;
            Assert.That(firstElevation, Is.GreaterThan(settings.IntroPitch - 6f), "起点必须在角色后上方。");
            Assert.That(firstDistance, Is.GreaterThan(settings.IntroDistance - 1.5f), "起点必须比常规更远。");

            var previousElevation = firstElevation;
            var previousDistance = firstDistance;
            var previousAlpha = 1f;
            var fadeGoneAt = -1f;
            while (player.State.Action == ActionState.SpawnLobbyToMap01)
            {
                yield return null;
                if (fade != null)
                {
                    Assert.That(fade.Alpha, Is.LessThanOrEqualTo(previousAlpha + 0.001f), "色幕只能越来越透明。");
                    previousAlpha = fade.Alpha;
                }
                else if (fadeGoneAt < 0f)
                {
                    fadeGoneAt = Time.time - fadeStarted;
                }

                var elevation = Elevation();
                var distance = rig.CurrentDistance;
                Assert.That(elevation, Is.LessThanOrEqualTo(previousElevation + 0.5f), "镜头必须一路往下收，不能来回晃。");
                Assert.That(distance, Is.LessThanOrEqualTo(previousDistance + 0.01f), "镜头必须一路拉近。");
                previousElevation = elevation;
                previousDistance = distance;
            }

            Assert.That(fadeGoneAt, Is.GreaterThan(1.2f).And.LessThan(2.0f), "色幕约 1.5 秒变成完全透明后消失。");

            // 交还操作的那一刻镜头已经落定。
            yield return null;
            yield return null;
            Assert.That(rig.IsIntroActive, Is.False, "出场结束时镜头应已回到常规跟随。");
            Assert.That(rig.CurrentDistance, Is.EqualTo(settings.Distance).Within(0.01f));
            Assert.That(Elevation(), Is.EqualTo(settings.InitialPitch).Within(3f), "落定在常规俯仰角。");
        }

        [UnityTest]
        public IEnumerator StartingFromTheLobbyButtonStillPlaysTheTaskSceneEntrance()
        {
            // 走大厅"开始游戏"的真实路径，并且像 GameLifetimeScope 一样让大厅随 Bootstrap 场景一起被释放。
            // 之前的出场测试直接调用世界流转，绕过了大厅，所以一直没抓到：大厅把自己的生命周期令牌
            // 传进了切换，场景激活时大厅被释放，切换在途中取消了自己，到达状态被清空，出场全部不播。
            yield return LoadBootScene();
            var lobby = new LobbyController(
                new LobbyModel(),
                Resolve<ILobbySceneGateway>(),
                WorldMapIds.Map01,
                new PendingLobbyAccountGateway(),
                new PendingLobbyProfileGateway(),
                Resolve<IServerCapabilities>());
            lobby.Enter("player-one", 42);
            DisposeWithScene.Attach(lobby);

            var task = lobby.StartGameAsync(CancellationToken.None).Preserve();
            var deadline = Time.realtimeSinceStartup + SceneLoadTimeoutSeconds;
            while (!task.Status.IsCompleted() && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(task.Status.IsCompleted(), Is.True, "从大厅进入任务界面没有在超时前完成。");
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(WorldSceneNames.Map01Task));

            var player = Object.FindObjectOfType<PlayerCharacterView>();
            Assert.That(player, Is.Not.Null);
            yield return WaitUntil(
                () => player.State.Action == ActionState.SpawnLobbyToMap01, 2f, "lobby-spawn");

            Assert.That(Object.FindObjectOfType<WorldSceneEntry>().EntryFade, Is.Not.Null,
                "从大厅进入任务界面必须播放出场色幕。");
            Assert.That(Object.FindObjectOfType<ThirdPersonCameraRig>().IsIntroActive, Is.True,
                "从大厅进入任务界面必须播放出场镜头。");
        }

        private sealed class PendingLobbyAccountGateway : ILobbyAccountGateway
        {
            public Cysharp.Threading.Tasks.UniTask<LobbyAccountSummaryResult> RequestAccountSummaryAsync(
                CancellationToken cancellationToken) =>
                new Cysharp.Threading.Tasks.UniTaskCompletionSource<LobbyAccountSummaryResult>().Task;
        }

        private sealed class PendingLobbyProfileGateway : ILobbyProfileGateway
        {
            public Cysharp.Threading.Tasks.UniTask<LobbyProfileResult> RequestProfileAsync(
                CancellationToken cancellationToken) =>
                new Cysharp.Threading.Tasks.UniTaskCompletionSource<LobbyProfileResult>().Task;

            public Cysharp.Threading.Tasks.UniTask<LobbyProfileResult> SetAppearanceAsync(
                string avatarId, string avatarFrameId, CancellationToken cancellationToken) =>
                new Cysharp.Threading.Tasks.UniTaskCompletionSource<LobbyProfileResult>().Task;
        }

        [UnityTest]
        public IEnumerator StalledFramesDoNotLetTheSpawnAnimationRunAheadOfTheStateMachine()
        {
            yield return LoadBootScene();
            yield return EnterMap01();
            var player = Object.FindObjectOfType<PlayerCharacterView>();
            var animator = player.GetComponentInChildren<Animator>();
            var burst = Animator.StringToHash(PlayerAnimatorProjector.StateNames.SpawnBurstLobbyToMap01);
            yield return WaitUntil(
                () => player.State.Action == ActionState.SpawnLobbyToMap01 &&
                      animator.GetCurrentAnimatorStateInfo(0).shortNameHash == burst &&
                      !animator.IsInTransition(0),
                6f, "spawn-burst");

            var clipSeconds = animator.GetCurrentAnimatorStateInfo(0).length;
            var before = animator.GetCurrentAnimatorStateInfo(0).normalizedTime * clipSeconds;
            const int stalledFrames = 8;
            try
            {
                // 每帧固定 0.25 秒：模拟进地图后头几帧的卡顿。
                Time.captureDeltaTime = 0.25f;
                for (var i = 0; i < stalledFrames; i++)
                {
                    yield return null;
                }
            }
            finally
            {
                Time.captureDeltaTime = 0f;
            }

            var after = animator.GetCurrentAnimatorStateInfo(0).normalizedTime * clipSeconds;
            var expected = stalledFrames * PlayerCore.MaxDeltaSeconds *
                           player.Tuning.Reaction.SpawnLobbyToMap01.Playback.MainSpeed;

            // 不对齐时 Animator 会走 8 × 0.25 = 2.0 秒片段时间，业务只走 0.8 秒。
            Assert.That(after - before, Is.EqualTo(expected).Within(0.12f),
                "卡顿帧里 Animator 只能走业务走过的片段时间。");
            Assert.That(player.State.Action, Is.EqualTo(ActionState.SpawnLobbyToMap01));
        }

        [UnityTest]
        public IEnumerator AttacksDoNotMoveThePlayer()
        {
            yield return LoadBootScene();
            yield return EnterMap01();
            var player = Object.FindObjectOfType<PlayerCharacterView>();
            yield return WaitUntil(() => player.State.Action == ActionState.None, 12f);

            var start = player.transform.position;

            // 点一下左键：一段普通攻击。整段结束后坐标必须没变。
            _input.Press(_mouse.leftButton);
            yield return null;
            yield return null;
            _input.Release(_mouse.leftButton);
            yield return WaitUntil(
                () => player.State.Action == ActionState.AttackCombo1, 3f, "combo1-start");
            yield return WaitUntil(
                () => player.State.Action == ActionState.None, 8f, "combo1-end");

            var planar = new Vector2(
                player.transform.position.x - start.x, player.transform.position.z - start.z);
            Assert.That(
                planar.magnitude, Is.LessThan(0.2f),
                $"攻击前后坐标必须不变，实际移动了 {planar.magnitude:0.00}。");
        }

        [UnityTest]
        public IEnumerator WasdDrivesTheCharacterController()
        {
            yield return LoadBootScene();
            yield return EnterMap01();
            var player = Object.FindObjectOfType<PlayerCharacterView>();
            yield return WaitUntil(() => player.State.Action == ActionState.None, 8f);

            var start = player.transform.position;
            _input.Press(_keyboard[Key.W]);
            yield return null;
            yield return null;
            yield return WaitUntil(
                () => player.State.Locomotion == LocomotionState.Walk, 2f);

            var deadline = Time.realtimeSinceStartup + 0.5f;
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            var moved = Vector3.Distance(
                new Vector3(start.x, 0f, start.z),
                new Vector3(player.transform.position.x, 0f, player.transform.position.z));
            _input.Release(_keyboard[Key.W]);
            yield return null;

            Assert.That(moved, Is.GreaterThan(0.5f), "按 W 必须让 CharacterController 真的前进。");
        }

        [UnityTest]
        public IEnumerator EveryDirectionKeyTurnsTheCharacterAndMovesIt()
        {
            yield return LoadBootScene();
            yield return EnterMap01();
            var player = Object.FindObjectOfType<PlayerCharacterView>();
            yield return WaitUntil(() => player.State.Action == ActionState.None, 8f);

            var rig = Object.FindObjectOfType<ThirdPersonCameraRig>();
            Assert.That(rig, Is.Not.Null);

            // 四个方向键都必须既改朝向又移动，而且都走 Walk 状态与前进动画。
            foreach (var probe in new[]
                     {
                         (Key: Key.W, Offset: 0f),
                         (Key: Key.D, Offset: 90f),
                         (Key: Key.S, Offset: 180f),
                         (Key: Key.A, Offset: -90f)
                     })
            {
                var startPosition = player.transform.position;
                _input.Press(_keyboard[probe.Key]);
                yield return null;
                yield return null;
                yield return WaitUntil(
                    () => player.State.Locomotion == LocomotionState.Walk, 2f,
                    $"{probe.Key}-walk");

                // 转向需要一点时间，按住 0.5 秒足够转到位并走出一段距离。
                var deadline = Time.realtimeSinceStartup + 0.5f;
                while (Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }

                var facing = player.transform.eulerAngles.y;
                var moved = Vector3.Distance(
                    new Vector3(startPosition.x, 0f, startPosition.z),
                    new Vector3(player.transform.position.x, 0f, player.transform.position.z));

                _input.Release(_keyboard[probe.Key]);
                yield return null;
                yield return null;

                Assert.That(
                    moved, Is.GreaterThan(0.4f),
                    $"{probe.Key} 必须让角色真的移动，而不只是转身。");
                var expected = rig.Yaw + probe.Offset;
                Assert.That(
                    Mathf.Abs(Mathf.DeltaAngle(facing, expected)), Is.LessThan(25f),
                    $"{probe.Key} 的朝向必须相对摄像机（期望 {expected:0}，实际 {facing:0}）。");

                // 等停止动作走完再测下一个方向。
                yield return WaitUntil(
                    () => player.State.Locomotion == LocomotionState.Idle, 3f, $"{probe.Key}-idle");
            }
        }

        [UnityTest]
        public IEnumerator MovementDirectionFollowsTheCameraYaw()
        {
            yield return LoadBootScene();
            yield return EnterMap01();
            var player = Object.FindObjectOfType<PlayerCharacterView>();
            yield return WaitUntil(() => player.State.Action == ActionState.None, 8f);
            var rig = Object.FindObjectOfType<ThirdPersonCameraRig>();

            // 先把镜头转开一大截，再按 W：角色应该朝镜头的新方向走，而不是旧方向。
            for (var i = 0; i < 25; i++)
            {
                _input.Set(_mouse.delta, new Vector2(40f, 0f));
                yield return null;
            }

            var cameraYaw = rig.Yaw;
            Assert.That(
                Mathf.Abs(Mathf.DeltaAngle(cameraYaw, player.transform.eulerAngles.y)),
                Is.GreaterThan(20f),
                "测试前提：镜头必须已经转离角色朝向。");

            _input.Press(_keyboard[Key.W]);
            yield return null;
            yield return null;
            var deadline = Time.realtimeSinceStartup + 0.6f;
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            var facing = player.transform.eulerAngles.y;
            _input.Release(_keyboard[Key.W]);
            yield return null;

            Assert.That(
                Mathf.Abs(Mathf.DeltaAngle(facing, rig.Yaw)), Is.LessThan(25f),
                "按 W 时角色必须转到摄像机正前方，移动方向相对摄像机。");
        }

        [UnityTest]
        public IEnumerator CameraStaysCenteredOnThePlayerAndMouseDoesNotRotateTheCharacter()
        {
            yield return LoadBootScene();
            yield return EnterMap01();
            var player = Object.FindObjectOfType<PlayerCharacterView>();
            yield return WaitUntil(() => player.State.Action == ActionState.None, 8f);

            var rig = Object.FindObjectOfType<ThirdPersonCameraRig>();
            Assert.That(rig, Is.Not.Null);
            Assert.That(rig.Target, Is.SameAs(player.transform), "相机必须绑定到当前玩家。");

            var startYaw = player.transform.eulerAngles.y;
            var startRigYaw = rig.Yaw;

            // 鼠标位移每帧都要重新给：delta 控件在每帧结束后归零。
            for (var i = 0; i < 20; i++)
            {
                _input.Set(_mouse.delta, new Vector2(30f, 0f));
                yield return null;
            }

            Assert.That(
                Mathf.Abs(rig.Yaw - startRigYaw), Is.GreaterThan(5f),
                "鼠标必须能围绕角色旋转镜头。");
            Assert.That(
                Mathf.Abs(Mathf.DeltaAngle(startYaw, player.transform.eulerAngles.y)),
                Is.LessThan(1f),
                "鼠标转动镜头不得直接旋转角色。");

            // 轴心必须贴在角色身上，不能脱离。
            var pivot = rig.transform.Find("CameraPivot");
            Assert.That(pivot, Is.Not.Null);
            var planar = new Vector2(
                pivot.position.x - player.transform.position.x,
                pivot.position.z - player.transform.position.z);
            Assert.That(planar.magnitude, Is.LessThan(0.5f), "镜头中心点不能脱离角色。");
        }

        [UnityTest]
        public IEnumerator PortalTriggersOnceAndLoadsMap02WithItsOwnBurst()
        {
            yield return LoadBootScene();
            yield return EnterMap01();
            var player = Object.FindObjectOfType<PlayerCharacterView>();
            yield return WaitUntil(() => player.State.Action == ActionState.None, 8f);

            var portal = Object.FindObjectOfType<MapPortal>();
            Assert.That(portal, Is.Not.Null, "地图一必须有固定传送门。");

            // 直接把角色放到传送门里，避免测试依赖走路路径。
            player.TeleportTo(portal.transform.position, player.transform.rotation);
            yield return WaitUntil(() => portal == null || portal.IsLocked, 3f, "portal-locked");

            yield return WaitUntil(
                () => SceneManager.GetActiveScene().name == WorldSceneNames.Map02Combat,
                SceneLoadTimeoutSeconds);
            Assert.That(
                SceneManager.GetActiveScene().name, Is.EqualTo(WorldSceneNames.Map02Combat));

            yield return WaitUntil(
                () => Object.FindObjectOfType<PlayerCharacterView>() != null, 6f, "map02-player");
            var arrived = Object.FindObjectOfType<PlayerCharacterView>();
            Assert.That(arrived, Is.Not.Null);

            // 场景入口是异步的：出场状态在生成完成后才写入，不能在切图完成的同一帧断言。
            yield return WaitUntil(
                () => arrived.State.Action == ActionState.SpawnMap01ToMap02, 6f, "map02-burst");
            Assert.That(
                arrived.State.Action, Is.EqualTo(ActionState.SpawnMap01ToMap02),
                "从地图一进入地图二必须播放 Burst01。");

            // 战斗界面有出场镜头（从角色后上方拉回），但没有任务界面的出场色幕。
            Assert.That(Object.FindObjectOfType<ThirdPersonCameraRig>().IsIntroActive, Is.True,
                "战斗界面必须播放出场镜头。");
            Assert.That(Object.FindObjectOfType<EntryScreenFade>(), Is.Null,
                "出场色幕只属于任务界面，战斗界面不得播放。");

            yield return WaitUntil(() => arrived.State.Action == ActionState.None, 8f);
            Assert.That(arrived.State.Flags.HasFlag(PlayerOverlayFlags.InputLocked), Is.False);

            Assert.That(
                Object.FindObjectsOfType<PlayerCharacterView>().Length, Is.EqualTo(1),
                "切换地图后不得出现重复玩家。");
            Assert.That(
                Object.FindObjectsOfType<ThirdPersonCameraRig>().Length, Is.EqualTo(1),
                "切换地图后不得出现重复相机。");
            Assert.That(
                Object.FindObjectsOfType<AppRootLifetimeScope>().Length, Is.EqualTo(1),
                "切换地图后不得出现重复组合根。");
            Assert.That(
                Object.FindObjectsOfType<WorldSceneLifetimeScope>().Length, Is.EqualTo(1),
                "每张地图只能有一个场景 Scope。");
        }

        [UnityTest]
        public IEnumerator DeathReturnsToMap01ExactlyOnceAndRespawnsWithTheBaseline()
        {
            yield return LoadBootScene();
            yield return EnterMap01();
            var player = Object.FindObjectOfType<PlayerCharacterView>();
            yield return WaitUntil(() => player.State.Action == ActionState.None, 8f);

            // 重生保护会挡伤害，先等它过去再打死。
            yield return WaitUntil(
                () => !player.State.Flags.HasFlag(PlayerOverlayFlags.SpawnProtection), 6f);

            // 原始伤害要先过防御再扣护甲，"刚好等于生命加护甲"是打不死的。
            player.ApplyDebugRawDamage(
                (player.State.MaxHealth + player.State.MaxArmor)
                * (100f + player.State.Defense) / 100f);
            Assert.That(player.State.Reaction, Is.EqualTo(ReactionState.Death));

            var world = Resolve<IWorldFlowController>();
            yield return WaitUntil(
                () => world.Current.Arrival == WorldArrival.DeathToMap01,
                SceneLoadTimeoutSeconds, "death-arrival");

            yield return WaitUntil(
                () => SceneManager.GetActiveScene().name == WorldSceneNames.Map01Task &&
                      Object.FindObjectOfType<PlayerCharacterView>() != null &&
                      Object.FindObjectOfType<PlayerCharacterView>().State.IsAlive,
                SceneLoadTimeoutSeconds, "death-respawn");

            var revived = Object.FindObjectOfType<PlayerCharacterView>();
            Assert.That(revived.State.IsAlive, Is.True);
            Assert.That(revived.State.Health, Is.EqualTo(revived.State.MaxHealth), "生命恢复 100%。");
            Assert.That(
                revived.State.Armor, Is.EqualTo(revived.State.MaxArmor * 0.5f).Within(0.01f),
                "护甲恢复 50%。");
            Assert.That(revived.State.Stamina, Is.EqualTo(20f).Within(0.01f), "体力恢复至 20。");
            Assert.That(revived.State.SkillFCooldownRemaining, Is.EqualTo(0f));
            Assert.That(revived.State.SkillVCooldownRemaining, Is.EqualTo(0f));

            Assert.That(
                Object.FindObjectsOfType<PlayerCharacterView>().Length, Is.EqualTo(1),
                "死亡返回后不得出现重复玩家。");

            var respawn = Object.FindObjectsOfType<PlayerSpawnPoint>()
                .First(p => p.Kind == PlayerSpawnPoint.SpawnKind.Respawn);
            Assert.That(
                Vector3.Distance(revived.transform.position, respawn.transform.position),
                Is.LessThan(1.5f),
                "死亡后必须在地图一的重生点复活。");
        }

        private static T Resolve<T>()
        {
            var root = AppRootLifetimeScope.Instance;
            Assert.That(root, Is.Not.Null, "持久化根不存在。");
            return root.Container.Resolve<T>();
        }

        [UnityTest]
        public IEnumerator SkillVHoldsThenDashesVisiblyAlongAValidatedPathInTheFormalTaskScene()
        {
            yield return LoadBootScene();
            yield return EnterMap01();
            var player = Object.FindObjectOfType<PlayerCharacterView>();
            yield return WaitUntil(() => player.State.Action == ActionState.None, 12f, "spawn-burst");

            var body = player.GetComponent<CharacterController>();
            var mask = DashPathResolver.ObstacleMaskFor(player.gameObject.layer);
            var v = player.Tuning.SkillV.Action;
            var windup = v.DisplacementStartClipSeconds / v.Playback.MainSpeed;
            var dash = (v.DisplacementClipSeconds - v.DisplacementStartClipSeconds) / v.Playback.MainSpeed;

            // 正式地图前方是什么并不固定，所以不硬编码"一定冲满 17.838 米"，
            // 而是在按下之前用同一个校验器预测终点：蓄势期间角色不动也不转身，预测与实际必须一致。
            Physics.SyncTransforms();
            var start = player.transform.position;
            var predicted = DashPathResolver.Resolve(body, player.transform.forward, v.ForwardDisplacement, mask);
            var expectedTravel = Horizontal(predicted - start);
            Assert.That(expectedTravel, Is.GreaterThan(5f), "测试前提：出生点正前方要有足够的冲刺空间。");

            _input.Press(_keyboard[Key.V]);
            yield return null;
            yield return null;
            _input.Release(_keyboard[Key.V]);

            var castAt = -1f;
            var firstMoveAt = -1f;
            var lastMoveAt = -1f;
            var movingFrames = 0;
            var movedDuringWindup = 0f;
            var previous = player.transform.position;
            var deadline = Time.realtimeSinceStartup + v.RealDurationSeconds + 3f;
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                if (player.State.Action == ActionState.SkillV)
                {
                    if (castAt < 0f)
                    {
                        castAt = Time.time;
                    }
                }
                else if (castAt >= 0f)
                {
                    break;
                }

                var now = player.transform.position;
                var step = Horizontal(now - previous);
                if (castAt >= 0f && Time.time - castAt < windup - 0.05f)
                {
                    movedDuringWindup = Mathf.Max(movedDuringWindup, Horizontal(now - start));
                }

                if (step > 0.001f)
                {
                    movingFrames++;
                    if (firstMoveAt < 0f)
                    {
                        firstMoveAt = Time.time;
                    }

                    lastMoveAt = Time.time;
                }

                previous = now;
            }

            Assert.That(castAt, Is.GreaterThanOrEqualTo(0f), "按 V 必须进入 V 技能。");
            Assert.That(movedDuringWindup, Is.LessThan(0.05f), "蓄势期间必须原地不动。");
            Assert.That(firstMoveAt - castAt, Is.EqualTo(windup).Within(0.06f), "冲刺必须在蓄势结束时起步。");
            Assert.That(lastMoveAt - firstMoveAt, Is.EqualTo(dash).Within(0.06f), "冲刺持续约 0.2 秒。");
            Assert.That(movingFrames, Is.GreaterThanOrEqualTo(5), "冲刺要看得见轨迹，不能一两帧闪过去。");
            Assert.That(
                Horizontal(player.transform.position - predicted), Is.LessThan(0.1f),
                "实际终点必须与校验器预测的终点一致。");
            Assert.That(
                DashPathResolver.Fits(body, player.transform.position, mask), Is.True,
                "终点不得卡进任何东西。");

            yield return WaitUntil(() => body.isGrounded, 2f, "grounded-after-dash");
        }

        private static float Horizontal(Vector3 delta) => new Vector2(delta.x, delta.z).magnitude;

        private IEnumerator LoadBootScene()
        {
            SceneManager.LoadScene("SampleScene", LoadSceneMode.Single);
            yield return null;
            yield return null;

            var root = AppRootLifetimeScope.Instance;
            Assert.That(root, Is.Not.Null, "Bootstrap 场景缺少持久化 App Root。");
            yield return null;
        }

        private IEnumerator EnterMap01()
        {
            var world = Resolve<IWorldFlowController>();
            var task = world.EnterMap01Async(default).Preserve();
            var deadline = Time.realtimeSinceStartup + SceneLoadTimeoutSeconds;
            while (!task.Status.IsCompleted() && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(task.Status.IsCompleted(), Is.True, "进入地图一没有在超时前完成。");
            Assert.That(
                SceneManager.GetActiveScene().name, Is.EqualTo(WorldSceneNames.Map01Task),
                "地图一必须加载成功。");
            yield return WaitUntil(() => Object.FindObjectOfType<PlayerCharacterView>() != null, 6f);
        }

        private static IEnumerator WaitUntil(
            System.Func<bool> condition, float timeoutSeconds, string label = null)
        {
            var deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (!condition() && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(
                condition(), Is.True,
                $"等待条件在 {timeoutSeconds} 秒内没有满足（{label ?? "未命名"}）。");
        }

        private IEnumerator HoldKey(Key key, float seconds)
        {
            _input.Press(_keyboard[key]);
            // 按下要一帧之后才在 dynamic update 里生效。
            yield return null;
            yield return null;

            var deadline = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            _input.Release(_keyboard[key]);
            yield return null;
            yield return null;
        }
    }
}
