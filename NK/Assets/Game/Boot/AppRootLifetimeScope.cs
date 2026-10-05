using Naraka.Core.Application.Config;
using Naraka.Core.Application.Scenes;
using Naraka.Core.Application.Timing;
using Naraka.Features.Character.Controller;
using Naraka.Features.Character.Model;
using Naraka.Features.Character.View;
using Naraka.Features.Combat.Controller;
using Naraka.Features.Combat.View;
using Naraka.Features.CombatHud.Controller;
using Naraka.Features.Loading.Controller;
using Naraka.Features.Loading.View;
using Naraka.Features.Lobby.Controller;
using Naraka.Features.Monster.Controller;
using Naraka.Features.World.Controller;
using Naraka.Infrastructure.Camera;
using Naraka.Infrastructure.Config;
using Naraka.Infrastructure.Input;
using Naraka.Infrastructure.Scene;
using Naraka.Infrastructure.Timing;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Naraka.Boot
{
    /// <summary>
    /// 跨场景持久化的组合根。
    ///
    /// 它只承载真正需要活过场景切换的能力：时钟、场景加载、加载界面、输入、
    /// 第三人称相机、玩家状态与命中结算。大厅的十几个业务控制器仍然留在
    /// <see cref="GameLifetimeScope"/> 里随 Bootstrap 场景卸载，不会被带进战斗场景。
    ///
    /// 为什么必须持久化：加载界面要在"卸载大厅"和"地图已就绪"之间一直可见。
    /// 如果它随场景销毁，玩家会在切换的瞬间看到一段黑屏，而不是加载进度。
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    public sealed class AppRootLifetimeScope : LifetimeScope
    {
        /// <summary>登录后进入大厅、以及地图之间切换时加载界面的最短显示时长（秒）。</summary>
        [SerializeField] private float minimumLoadingSeconds = 2f;

        [SerializeField] private PlayerTuningAsset playerTuning;

        [SerializeField] private NarakaPlayerInputProvider inputProvider;

        [SerializeField] private ThirdPersonCameraRig cameraRig;

        private static AppRootLifetimeScope _instance;

        /// <summary>当前生效的持久化根。没有时为 null。</summary>
        public static AppRootLifetimeScope Instance => _instance;

        protected override void Awake()
        {
            // 重新加载 Bootstrap 场景会带来第二个持久化根。生产流程不会这么做，
            // 但 PlayMode 测试会，而"场景切换后不得出现重复组合根"本来就是硬要求。
            // 先停用再销毁：FindObjectOfType 不返回停用对象，因此这一帧里
            // 子 Scope 的 FindParent 不会挂到一个正在销毁的父容器上。
            if (_instance != null && _instance != this)
            {
                gameObject.SetActive(false);
                Destroy(gameObject);
                return;
            }

            _instance = this;

            // 必须在 base.Awake 之前标记持久化：容器构建时子对象已经要活下来了。
            if (transform.parent == null)
            {
                DontDestroyOnLoad(gameObject);
            }

            base.Awake();
        }

        protected override void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }

            base.OnDestroy();
        }

        protected override void Configure(IContainerBuilder builder)
        {
            builder.Register<IGameClock, UnityGameClock>(Lifetime.Singleton);
            builder.Register<ISceneLoader, UnitySceneLoader>(Lifetime.Singleton);

            builder.Register<LoadingController>(Lifetime.Singleton)
                .AsSelf()
                .As<ILoadingController>()
                .WithParameter("minimumSeconds", (double)minimumLoadingSeconds);

            // 世界流转同时充当大厅的场景网关：大厅按钮因此不需要知道加载界面的存在。
            builder.Register<WorldFlowController>(Lifetime.Singleton)
                .AsSelf()
                .As<IWorldFlowController>()
                .As<ILobbySceneGateway>();

            builder.Register<IHitResolver, HitResolver>(Lifetime.Singleton);

            // 配置目录挂在持久根上：怪物数值与大厅展示读的是同一份目录，
            // 而地图场景里并没有 GameLifetimeScope。
            builder.Register<IGameConfigProvider>(
                _ => new StreamingAssetsGameConfigProvider(), Lifetime.Singleton);

            // 战斗侧的场景对象登记表。它们只解决"谁在哪"，不持有任何战斗规则。
            builder.Register<ICombatTargetRegistry, CombatTargetRegistry>(Lifetime.Singleton);
            builder.Register<IExecutionTargetRegistry, ExecutionTargetRegistry>(Lifetime.Singleton);
            builder.Register<IMonsterRegistry, MonsterRegistry>(Lifetime.Singleton);

            var tuning = playerTuning != null
                ? playerTuning.ToTuning()
                : PlayerTuning.CreateBaseline();
            if (playerTuning == null)
            {
                Debug.LogWarning(
                    "AppRootLifetimeScope 未绑定 PlayerTuningAsset，使用内置基线数值。" +
                    "请运行菜单 NARAKA/Setup/Apply P2 Scene Setup。",
                    this);
            }

            builder.Register<PlayerController>(Lifetime.Singleton)
                .AsSelf()
                .As<IPlayerController>()
                .WithParameter(tuning);

            if (inputProvider != null)
            {
                builder.RegisterInstance(inputProvider)
                    .AsSelf()
                    .As<IPlayerInputSource>();
            }
            else
            {
                Debug.LogError(
                    "AppRootLifetimeScope 未绑定 NarakaPlayerInputProvider；玩家输入不可用。", this);
            }

            if (cameraRig != null)
            {
                builder.RegisterInstance(cameraRig)
                    .AsSelf()
                    .As<ICameraOrientation>();
            }
            else
            {
                Debug.LogError(
                    "AppRootLifetimeScope 未绑定 ThirdPersonCameraRig；第三人称相机不可用。", this);
            }

            // 战斗 HUD 的只读状态。视觉与挂载由用户手工完成，因此这里只注册控制器；
            // CombatHudView 如果不在场景里（例如还没做 HUD），整块直接不存在，
            // 不会因为缺一个界面就让容器构建失败。
            builder.Register<CombatHudController>(Lifetime.Singleton)
                .AsSelf()
                .As<ICombatHudController>();

            builder.RegisterComponentInHierarchy<LoadingView>();
        }
    }
}
