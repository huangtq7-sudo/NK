using Naraka.Features.Combat.View;
using Naraka.Features.CombatHud.View;
using Naraka.Features.Dialogue.Controller;
using Naraka.Features.Dialogue.View;
using Naraka.Features.Monster.View;
using Naraka.Features.Quest.Controller;
using Naraka.Features.Quest.View;
using Naraka.Features.World.View;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Naraka.Boot
{
    /// <summary>
    /// 地图场景的组合根。它是持久化 <see cref="AppRootLifetimeScope"/> 的子 Scope：
    /// 场景卸载时只销毁本层注册，跨场景能力留在父层，因此不会出现第二个组合根。
    /// </summary>
    public sealed class WorldSceneLifetimeScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            if (FindObjectOfType<WorldSceneEntry>(true) != null)
            {
                builder.RegisterComponentInHierarchy<WorldSceneEntry>();
            }
            else
            {
                Debug.LogError($"场景 {gameObject.scene.name} 缺少 WorldSceneEntry，玩家不会生成。", this);
            }

            // 数量可能是 0 或多个的场景组件统一走"逐个注入"，而不是逐个注册：
            // 同一类型的多次 RegisterComponent 只会让其中一个被解析到，其余拿不到依赖。
            InjectAll(builder, FindObjectsOfType<MapPortal>(true));
            InjectAll(builder, FindObjectsOfType<MonsterSpawner>(true));
            InjectAll(builder, FindObjectsOfType<DuskshadowWolfView>(true));
            InjectAll(builder, FindObjectsOfType<CounterTrainingTarget>(true));

            // 战斗 HUD 的视觉与挂载由用户手工完成，因此它可能还不存在。
            // 存在才注册：缺少 HUD 不应该让整张地图起不来。
            if (FindObjectOfType<CombatHudView>(true) != null)
            {
                builder.RegisterComponentInHierarchy<CombatHudView>();
            }

            RegisterQuestInteraction(builder);
        }

        /// <summary>
        /// P4.1 NPC 交互与底部对话。控制器随地图场景创建与释放：卸载时取消对话、归还角色输入，
        /// 不在持久根上留下任何场景引用。对话框、提示、追踪栏、领取面板与 NPC 范围都由用户手工挂载，
        /// 缺哪个就少哪个功能，不影响地图加载；没有对话框时不会开始任何对话。
        /// </summary>
        private static void RegisterQuestInteraction(IContainerBuilder builder)
        {
            builder.Register<PlayerGameplayInputLock>(Lifetime.Singleton)
                .AsSelf()
                .As<IGameplayInputLock>();
            builder.Register<DialogueController>(Lifetime.Singleton)
                .AsSelf()
                .As<IDialogueController>();
            builder.Register<NpcInteractionController>(Lifetime.Singleton)
                .AsSelf()
                .As<INpcInteractionController>();
            builder.RegisterEntryPoint<QuestWorldEntryPoint>();

            InjectAll(builder, FindObjectsOfType<NpcInteractionVolume>(true));
            InjectAll(builder, FindObjectsOfType<DialogueBoxView>(true));
            InjectAll(builder, FindObjectsOfType<InteractPromptView>(true));
            InjectAll(builder, FindObjectsOfType<QuestTrackerView>(true));
            InjectAll(builder, FindObjectsOfType<QuestRewardPanelView>(true));
        }

        private static void InjectAll<T>(IContainerBuilder builder, T[] components)
            where T : Component
        {
            if (components == null || components.Length == 0)
            {
                return;
            }

            builder.RegisterBuildCallback(container =>
            {
                for (var i = 0; i < components.Length; i++)
                {
                    if (components[i] != null)
                    {
                        container.Inject(components[i]);
                    }
                }
            });
        }

        protected override LifetimeScope FindParent()
        {
            var root = Find<AppRootLifetimeScope>();
            if (root == null)
            {
                Debug.LogError(
                    $"{name} 找不到持久化 AppRootLifetimeScope；" +
                    "请从 Bootstrap 场景进入地图，或运行菜单 NARAKA/Setup/Apply P2 Scene Setup。",
                    this);
                return null;
            }

            // 父 Scope 还没建好时先建它：VContainer 要求父容器在子容器之前存在。
            if (root.Container == null)
            {
                root.Build();
            }

            return root;
        }
    }
}
