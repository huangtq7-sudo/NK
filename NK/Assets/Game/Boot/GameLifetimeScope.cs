using MessagePipe;
using Naraka.Core.Application.Bootstrap;
using Naraka.Core.Application.Config;
using Naraka.Core.Application.Messaging;
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
using Naraka.Features.Lobby.View;
using Naraka.Features.Shop.Controller;
using Naraka.Features.Shop.View;
using Naraka.Infrastructure.Config;
using Naraka.Infrastructure.Messaging;
using Naraka.Core.Application.Scenes;
using UnityEngine;
using UnityEngine.Serialization;
using VContainer;
using VContainer.Unity;

namespace Naraka.Boot
{
    public sealed class GameLifetimeScope : LifetimeScope
    {
        /// <summary>
        /// 大厅"开始游戏"的目标地图。这是稳定业务 MapId，不是 Unity 场景名：
        /// 换正式场景素材不应该让这里跟着改。
        /// </summary>
        private const string DefaultMapId = WorldMapIds.Map01;

        [SerializeField] private string bootstrapBaseUrl = "http://127.0.0.1:5222";
        [SerializeField] private string configVersion = "p3-config-1";
        [SerializeField] private string protocolVersion = "LegacyNetworkV1";
        [FormerlySerializedAs("map1SceneName")]
        [SerializeField] private string map1MapId = DefaultMapId;

        protected override void Configure(IContainerBuilder builder)
        {
            builder.RegisterMessagePipe();
            builder.Register<MessagePipeDomainEventBus>(Lifetime.Singleton).As<IDomainEventBus>();

            builder.RegisterInstance<IConfigVersionGateway>(
                new UnityConfigVersionGateway(bootstrapBaseUrl));
            builder.RegisterInstance(new ConfigVersionModel(
                UnityEngine.Application.version,
                configVersion,
                protocolVersion));

            // 服务器能力集合在版本预检时写入，其余模块只读消费。
            // 旧云端不返回能力字段时会退回 P1.1-A 兼容集合，因此登录与大厅始终可用。
            // 客户端配置只用于展示。它已经移到持久化的 AppRootLifetimeScope：
            // 怪物数值也从同一份目录读，而地图场景里没有 GameLifetimeScope。
            // 本 Scope 是 App Root 的子 Scope，因此这里的消费者照常解析得到。

            builder.Register<ConfigVersionController>(Lifetime.Singleton)
                .AsSelf()
                .As<IStartupReadiness>();
            builder.Register<AccountController>(Lifetime.Singleton);

            builder.Register<LobbyModel>(Lifetime.Singleton);

            // 只把目标地图的业务 MapId 注入 LobbyController，不向容器注册裸 string。
            // 历史序列化值可能还是场景名（例如 Map01_Task），
            // 那不是合法 MapId，这里直接退回默认值，并由装配工具矫正资产。
            var mapId = WorldSceneCatalog.Default.TryResolveSceneName(map1MapId, out _)
                ? map1MapId
                : DefaultMapId;
            builder.Register<LobbyController>(Lifetime.Singleton)
                .AsSelf()
                .As<ILobbyController>()
                .WithParameter("mapSceneName", mapId);

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
        /// <summary>
        /// 大厅与账号的 Scope 挂在持久化 App Root 之下：加载界面、输入、相机与场景流转
        /// 必须活过场景切换，而这十几个大厅控制器不应该被带进战斗场景。
        ///
        /// 找不到 App Root 时返回 null，Scope 退回成独立根，登录与大厅仍然可用 ——
        /// 一个缺失的持久层不该让整个客户端起不来。
        /// </summary>
        protected override LifetimeScope FindParent()
        {
            var root = Find<AppRootLifetimeScope>();
            if (root == null)
            {
                Debug.LogError(
                    "场景缺少 AppRootLifetimeScope，开始游戏与战斗场景不可用；" +
                    "请运行菜单 NARAKA/Setup/Apply P2 Scene Setup。",
                    this);
                return null;
            }

            if (root.Container == null)
            {
                root.Build();
            }

            return root;
        }

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
