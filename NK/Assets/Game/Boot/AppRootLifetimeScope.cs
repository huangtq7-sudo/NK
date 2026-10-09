using Naraka.Core.Application.Bootstrap;
using Naraka.Core.Application.Config;
using Naraka.Core.Application.Networking;
using Naraka.Core.Application.Presentation;
using Naraka.Core.Application.Scenes;
using Naraka.Core.Application.Timing;
using Naraka.Features.Account.Controller;
using Naraka.Features.Account.Model;
using Naraka.Features.Achievement.Controller;
using Naraka.Features.Character.Controller;
using Naraka.Features.Character.Model;
using Naraka.Features.Character.View;
using Naraka.Features.Combat.Controller;
using Naraka.Features.Combat.View;
using Naraka.Features.CombatHud.Controller;
using Naraka.Features.Expedition.Controller;
using Naraka.Features.Expedition.Model;
using Naraka.Features.Forge.Controller;
using Naraka.Features.Gacha.Controller;
using Naraka.Features.Inventory.Controller;
using Naraka.Features.Loading.Controller;
using Naraka.Features.Loading.View;
using Naraka.Features.Loadout.Controller;
using Naraka.Features.Lobby.Controller;
using Naraka.Features.Monster.Controller;
using Naraka.Features.RedDot.Controller;
using Naraka.Features.Shop.Controller;
using Naraka.Features.SignIn.Controller;
using Naraka.Features.Social.Controller;
using Naraka.Features.World.Controller;
using Naraka.Infrastructure.Camera;
using Naraka.Infrastructure.Config;
using Naraka.Infrastructure.Input;
using Naraka.Infrastructure.Network;
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
    /// 它只承载真正需要活过场景切换的能力：认证连接、最小账号会话、
    /// 远征快照、时钟、场景加载、加载界面、输入、第三人称相机、玩家状态与命中结算。
    /// 大厅的其他业务控制器仍然留在
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

        [Tooltip("LegacyNetworkV1只通过本地回环或SSH隧道连接。")]
        [SerializeField] private string serverAddress = "127.0.0.1";

        [SerializeField] private int serverPort = 8011;

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
            // 认证连接必须跨场景存活。它如果留在大厅Scope，加载地图时会被Dispose，
            // 服务端会把这次正常切图误判成断线并立刻结算远征。
            var network = new LegacyNetworkAdapter(serverAddress, serverPort);
            builder.RegisterInstance(network)
                .As<INetworkFacade>()
                .As<IAccountGateway>()
                .As<ILobbyAccountGateway>()
                .As<ILobbyProfileGateway>()
                .As<ILoadoutGateway>()
                .As<IInventoryGateway>()
                .As<IShopGateway>()
                .As<IForgeGateway>()
                .As<IGachaGateway>()
                .As<ISignInGateway>()
                .As<IAchievementGateway>()
                .As<IRedDotGateway>()
                .As<ISocialGateway>()
                .As<IExpeditionGateway>();

            // 会话、能力声明与远征快照同样跨场景存活。回到Bootstrap时只重建View和大厅控制器，
            // 不要求玩家重新输入密码，也不会丢失首次结算摘要。
            builder.Register<AccountSessionModel>(Lifetime.Singleton);
            builder.Register<ServerCapabilityRegistry>(Lifetime.Singleton)
                .AsSelf()
                .As<IServerCapabilities>();
            builder.Register<ExpeditionModel>(Lifetime.Singleton);
            builder.Register<IExpeditionRequestIdSource, GuidExpeditionRequestIdSource>(Lifetime.Singleton);
            builder.Register<ExpeditionController>(Lifetime.Singleton)
                .AsSelf()
                .As<IExpeditionController>();

            builder.Register<IGameClock, UnityGameClock>(Lifetime.Singleton);
            builder.Register<ISceneLoader, UnitySceneLoader>(Lifetime.Singleton);

            // 加载界面按真正走过的帧数推进进度，因此它需要一个帧边界来源。
            // 实现用 PlayerLoop；Controller 只看到接口，不知道 Unity 的存在。
            builder.Register<IPresentationFrameScheduler, UnityPresentationFrameScheduler>(
                Lifetime.Singleton);

            // MapId 到 Unity 场景名的只读映射。注册成单例是为了让它成为
            // 整个运行时唯一的翻译点；Default 里没有灰盒场景，
            // 因此正式流转根本无法解析到它。
            builder.RegisterInstance<IWorldSceneCatalog>(WorldSceneCatalog.Default);

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
