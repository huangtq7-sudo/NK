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

            // 传送门数量可能是 0（地图二）或多个。逐个注入而不是逐个注册：
            // 同一类型的多次 RegisterComponent 只会让其中一个被解析到，其余拿不到依赖。
            var portals = FindObjectsOfType<MapPortal>(true);
            if (portals.Length > 0)
            {
                builder.RegisterBuildCallback(container =>
                {
                    for (var i = 0; i < portals.Length; i++)
                    {
                        container.Inject(portals[i]);
                    }
                });
            }
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
