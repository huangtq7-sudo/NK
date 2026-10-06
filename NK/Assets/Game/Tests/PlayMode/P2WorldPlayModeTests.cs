using System.Collections;
using System.Linq;
using Cysharp.Threading.Tasks;
using Naraka.Boot;
using Naraka.Features.Character.Model;
using Naraka.Features.Character.View;
using Naraka.Features.Loading.Controller;
using Naraka.Features.Loading.View;
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
