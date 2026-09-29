using System.Threading;
using Cysharp.Threading.Tasks;
using Naraka.Features.Character.View;
using Naraka.Features.World.Controller;
using UnityEngine;
using VContainer;

namespace Naraka.Features.World.View
{
    /// <summary>
    /// 地图一通往地图二的固定灰盒传送门。
    ///
    /// 玩家进入后立刻锁定：本组件的 <c>_locked</c> 让触发器只能生效一次，
    /// <see cref="IWorldFlowController"/> 内部还有第二层重复保护，
    /// 因此即使同一帧被多个碰撞体触发也只会产生一个场景切换任务。
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class MapPortal : MonoBehaviour
    {
        [Tooltip("传送门稳定 ID。用于日志与后续任务系统，不使用场景下标。")]
        [SerializeField] private string portalId = "Map01_To_Map02";

        private IWorldFlowController _world;
        private bool _locked;

        public string PortalId => portalId;

        public bool IsLocked => _locked;

        /// <summary>本传送门累计触发次数。PlayMode 测试用它断言"只触发一次"。</summary>
        public int TriggerCount { get; private set; }

        [Inject]
        public void Construct(IWorldFlowController world) => _world = world;

        private void Awake()
        {
            var trigger = GetComponent<Collider>();
            trigger.isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_locked)
            {
                return;
            }

            var player = other.GetComponentInParent<PlayerCharacterView>();
            if (player == null)
            {
                return;
            }

            _locked = true;
            TriggerCount++;
            // 立刻禁止玩家输入：加载界面出现之前不能再走出触发区或发起别的动作。
            player.SetLoading(true);
            EnterMapAsync().Forget();
        }

        private async UniTaskVoid EnterMapAsync()
        {
            if (_world == null)
            {
                Debug.LogError($"传送门 {portalId} 未注入 IWorldFlowController。", this);
                return;
            }

            try
            {
                // 刻意不传本场景的 CancellationToken：这次切换会把地图一连带这个
                // 传送门一起卸载，用它自己的 token 等于让切换在半途取消自己。
                // 切换的生命周期归持久化的 WorldFlowController 管。
                await _world.EnterMap02Async(CancellationToken.None);
            }
            catch (System.OperationCanceledException)
            {
                // 应用退出时持久化控制器会取消，属于正常流程。
            }
            catch (System.Exception exception)
            {
                Debug.LogError($"传送门 {portalId} 加载地图二失败。");
                Debug.LogException(exception);
                // 加载失败后解锁，让玩家可以重试，而不是卡在一个永远不响应的门里。
                _locked = false;
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.55f, 0.35f, 0.95f, 0.35f);
            var box = GetComponent<BoxCollider>();
            if (box == null)
            {
                return;
            }

            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(box.center, box.size);
        }
#endif
    }
}
