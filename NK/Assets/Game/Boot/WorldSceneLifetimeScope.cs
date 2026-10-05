using Naraka.Features.Combat.View;
using Naraka.Features.CombatHud.View;
using Naraka.Features.Monster.View;
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
            InjectAll(builder, FindObjectsOfType<GrayboxWolfView>(true));
            InjectAll(builder, FindObjectsOfType<CounterTrainingTarget>(true));

            // 战斗 HUD 的视觉与挂载由用户手工完成，因此它可能还不存在。
            // 存在才注册：缺少 HUD 不应该让整张地图起不来。
            if (FindObjectOfType<CombatHudView>(true) != null)
            {
                builder.RegisterComponentInHierarchy<CombatHudView>();
            }
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
