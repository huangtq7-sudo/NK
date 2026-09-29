using Naraka.Features.World.Controller;
using UnityEngine;

namespace Naraka.Features.World.View
{
    /// <summary>
    /// 出生点标记。场景入口按 <see cref="Kind"/> 找到对应的点，
    /// 不依赖场景层级顺序或对象列表下标。
    /// </summary>
    public sealed class PlayerSpawnPoint : MonoBehaviour
    {
        public enum SpawnKind
        {
            /// <summary>本场景的默认入口出生点。</summary>
            Entry = 0,

            /// <summary>死亡后的重生点。只有地图一需要。</summary>
            Respawn = 1
        }

        [SerializeField] private SpawnKind kind = SpawnKind.Entry;

        public SpawnKind Kind => kind;

        /// <summary>该出生点是否适用于本次到达方式。</summary>
        public bool Matches(WorldArrival arrival) => arrival == WorldArrival.DeathToMap01
            ? kind == SpawnKind.Respawn
            : kind == SpawnKind.Entry;

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Gizmos.color = kind == SpawnKind.Entry
                ? new Color(0.3f, 0.9f, 0.4f, 0.8f)
                : new Color(0.4f, 0.6f, 1f, 0.8f);
            Gizmos.DrawWireSphere(transform.position + (Vector3.up * 0.9f), 0.45f);
            Gizmos.DrawLine(
                transform.position + (Vector3.up * 0.9f),
                transform.position + (Vector3.up * 0.9f) + (transform.forward * 1.5f));
        }
#endif
    }
}
