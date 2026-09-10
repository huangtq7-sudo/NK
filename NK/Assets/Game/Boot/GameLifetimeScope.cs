using MessagePipe;
using Naraka.Core.Application.Bootstrap;
using Naraka.Core.Application.Config;
using Naraka.Core.Application.Messaging;
using Naraka.Core.Application.Networking;
using Naraka.Core.Application.Timing;
using Naraka.Features.Account.Controller;
using Naraka.Features.Account.Model;
using Naraka.Features.Account.View;
using Naraka.Features.Bootstrap.Controller;
using Naraka.Features.Bootstrap.Model;
using Naraka.Features.Bootstrap.View;
using Naraka.Features.Forge.Controller;
using Naraka.Features.Forge.View;
using Naraka.Features.Achievement.Controller;
using Naraka.Features.Achievement.View;
using Naraka.Features.Gacha.Controller;
using Naraka.Features.Gacha.View;
using Naraka.Features.RedDot.Controller;
using Naraka.Features.RedDot.View;
using Naraka.Features.SignIn.Controller;
using Naraka.Features.SignIn.View;
using Naraka.Features.Social.Controller;
using Naraka.Features.Social.View;
using Naraka.Features.Inventory.Controller;
using Naraka.Features.Inventory.View;
using Naraka.Features.Loadout.Controller;
using Naraka.Features.Loadout.View;
using Naraka.Features.Lobby.Controller;
using Naraka.Features.Lobby.Model;
using Naraka.Features.Loading.Controller;
using Naraka.Features.Loading.View;
using Naraka.Features.Lobby.View;
using Naraka.Features.Shop.Controller;
using Naraka.Features.Shop.View;
using Naraka.Infrastructure.Config;
using Naraka.Infrastructure.Messaging;
using Naraka.Infrastructure.Network;
using Naraka.Infrastructure.Scene;
using Naraka.Infrastructure.Timing;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Naraka.Boot
{
    public sealed class GameLifetimeScope : LifetimeScope
    {
        private const string DefaultMapSceneName = "Map1";

        [SerializeField] private string serverAddress = "127.0.0.1";
        [SerializeField] private int serverPort = 8011;
        [SerializeField] private string bootstrapBaseUrl = "http://127.0.0.1:5222";
        [SerializeField] private string configVersion = "p1-config-1";
        [SerializeField] private string protocolVersion = "LegacyNetworkV1";
        [SerializeField] private string map1SceneName = DefaultMapSceneName;

        /// <summary>登录后进入大厅前的最短加载时长（秒）。实际加载更久时以实际为准。</summary>
        [SerializeField] private float minimumLoadingSeconds = 2f;

        protected override void Configure(IContainerBuilder builder)
        {
            builder.RegisterMessagePipe();
            builder.Register<MessagePipeDomainEventBus>(Lifetime.Singleton).As<IDomainEventBus>();

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
                .As<ISocialGateway>();
            builder.RegisterInstance<IConfigVersionGateway>(
                new UnityConfigVersionGateway(bootstrapBaseUrl));
            builder.RegisterInstance(new ConfigVersionModel(
                UnityEngine.Application.version,
                configVersion,
                protocolVersion));

            // 服务器能力集合在版本预检时写入，其余模块只读消费。
            // 旧云端不返回能力字段时会退回 P1.1-A 兼容集合，因此登录与大厅始终可用。
            builder.Register<ServerCapabilityRegistry>(Lifetime.Singleton)
                .AsSelf()
                .As<IServerCapabilities>();

            // 客户端配置只用于展示。加载失败不阻断登录，由各界面显示配置错误。
            builder.Register<IGameConfigProvider>(
                _ => new StreamingAssetsGameConfigProvider(), Lifetime.Singleton);

            builder.Register<ConfigVersionController>(Lifetime.Singleton)
                .AsSelf()
                .As<IStartupReadiness>();
            builder.Register<IGameClock, UnityGameClock>(Lifetime.Singleton);
            builder.Register<LoadingController>(Lifetime.Singleton)
                .AsSelf()
                .As<ILoadingController>()
                .WithParameter("minimumSeconds", (double)minimumLoadingSeconds);

            builder.Register<AccountSessionModel>(Lifetime.Singleton);
            builder.Register<AccountController>(Lifetime.Singleton);

            builder.Register<ILobbySceneGateway, UnityLobbySceneGateway>(Lifetime.Singleton);
            builder.Register<LobbyModel>(Lifetime.Singleton);

            // 只把地图场景名注入LobbyController，不向容器注册裸string。
            var mapSceneName = string.IsNullOrWhiteSpace(map1SceneName)
                ? DefaultMapSceneName
                : map1SceneName;
            builder.Register<LobbyController>(Lifetime.Singleton)
                .AsSelf()
                .As<ILobbyController>()
                .WithParameter("mapSceneName", mapSceneName);

            // 装备控制器只读订阅大厅状态，出战选择的权威副本仍然只有账号资料一份。
            builder.Register<LoadoutController>(Lifetime.Singleton)
                .AsSelf()
                .As<ILoadoutController>();

            builder.Register<InventoryController>(Lifetime.Singleton)
                .AsSelf()
                .As<IInventoryController>();

            builder.Register<ShopController>(Lifetime.Singleton)
                .AsSelf()
                .As<IShopController>();

            builder.Register<ForgeController>(Lifetime.Singleton)
                .AsSelf()
                .As<IForgeController>();

            builder.Register<GachaController>(Lifetime.Singleton)
                .AsSelf()
                .As<IGachaController>();

            builder.Register<SignInController>(Lifetime.Singleton)
                .AsSelf()
                .As<ISignInController>();

            builder.Register<AchievementController>(Lifetime.Singleton)
                .AsSelf()
                .As<IAchievementController>();

            // 红点是一个独立模块：它只认识路径与版本号，不引用任何业务控制器。
            // 业务侧通过 MessagePipe 发布来源事件，两边因此没有直接依赖。
            builder.Register<RedDotController>(Lifetime.Singleton)
                .AsSelf()
                .As<IRedDotController>();

            builder.Register<SocialController>(Lifetime.Singleton)
                .AsSelf()
                .As<ISocialController>();

            builder.RegisterComponentInHierarchy<ConfigVersionView>();
            builder.RegisterComponentInHierarchy<AccountView>();
            builder.RegisterComponentInHierarchy<LobbyView>();
            builder.RegisterComponentInHierarchy<LoadingView>();

            // 功能面板都是可选界面：场景里缺少组件时只报错，不让容器构建失败，
            // 否则登录与大厅这两个必需流程会被一个可选界面拖垮。
            RegisterOptionalView<LobbyAppearanceView>(builder, "头像与头像框面板");
            RegisterOptionalView<LobbyFeaturePanelView>(builder, "大厅功能占位面板");
            RegisterOptionalView<HeroPanelView>(builder, "英雄界面");
            RegisterOptionalView<WeaponPanelView>(builder, "兵器界面");
            RegisterOptionalView<InventoryPanelView>(builder, "仓库界面");
            RegisterOptionalView<ShopPanelView>(builder, "商店界面");
            RegisterOptionalView<ForgePanelView>(builder, "锻造界面");
            RegisterOptionalView<GachaPanelView>(builder, "抽奖界面");
            RegisterOptionalView<SignInPanelView>(builder, "签到界面");
            RegisterOptionalView<AchievementPanelView>(builder, "成就与等级奖励界面");
            RegisterOptionalView<RedDotBadgeView>(builder, "红点角标");
            RegisterOptionalView<SocialPanelView>(builder, "好友与聊天界面");

            builder.RegisterEntryPoint<P0FlowCoordinator>();
        }

        /// <summary>
        /// 注册一个可选界面组件。
        ///
        /// <see cref="IContainerBuilder.RegisterComponentInHierarchy{T}"/> 要求组件已经存在于场景中，
        /// 缺失时会在容器构建阶段抛异常并连带拖垮登录与大厅。可选界面不该有这种影响力，
        /// 因此这里缺失只报错，让必需流程继续可用。
        /// </summary>
        private void RegisterOptionalView<T>(IContainerBuilder builder, string displayName)
            where T : MonoBehaviour
        {
            if (FindObjectOfType<T>(true) != null)
            {
                builder.RegisterComponentInHierarchy<T>();
                return;
            }

            Debug.LogError(
                $"场景缺少 {typeof(T).Name}，{displayName}不可用；" +
                "请运行菜单 NARAKA/Setup/Apply P0 Project Settings 补齐装配。");
        }
    }
}
