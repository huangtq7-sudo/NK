using System;
using UnityEngine;

namespace Naraka.P2.PlayMode.Tests
{
    /// <summary>
    /// 场景卸载时释放一个对象。用来在测试里还原 GameLifetimeScope 的真实时序：
    /// 大厅控制器挂在 Bootstrap 场景的容器上，场景一卸载就被 Dispose。
    /// </summary>
    public sealed class DisposeWithScene : MonoBehaviour
    {
        private IDisposable _target;

        public static void Attach(IDisposable target)
        {
            var host = new GameObject("DisposeWithScene");
            host.AddComponent<DisposeWithScene>()._target = target;
        }

        private void OnDestroy() => _target?.Dispose();
    }
}
