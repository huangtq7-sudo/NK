using UnityEngine;

namespace Naraka.Features.Character.View
{
    /// <summary>
    /// 根位移抵消器。
    ///
    /// 这些动画在根骨骼上烘焙了水平位移（冲刺 +12.3、V 技能 +17.8、三段攻击 +1.7～+3.2、
    /// 停止动作 +1.4 等）。它们必须被抵消掉，否则：
    ///
    /// - 动作播放时整个骨架相对 GameObject 前移，与代码驱动的位移叠加成双倍移动；
    /// - 动作结束交叉淡入回 Idle 时，骨架被插值从"前移后的位置"拉回原点，
    ///   看上去就是**角色向后退一小步**。
    ///
    /// 为什么用代码抵消而不用 Unity 的 Root Motion 提取：正式角色模型与全部动画
    /// 都是 Generic 且 `avatarSetup = NoAvatar`，没有 Avatar 就没有 Root Motion 节点，
    /// 在导入器上设 `motionNodeName` 是无效的。要走那条路就得给角色生成 Avatar
    /// 并重新导入全部动画，那是对用户美术资源的大改动。
    ///
    /// 抵消范围刻意只限水平位移：
    /// - **水平（XZ）**：抵消。位移只由代码驱动（见 ADR-0015）。
    /// - **垂直（Y）**：保留。蹲伏、起跳这类垂直姿态属于动画表现。
    /// - **旋转**：默认保留，因为 `Attack02` 这类"转身再转身"的动作靠它才有观感。
    ///   `Run_Turnback` 带 180° 净旋转，需要时可以单独打开 <c>cancelRotation</c>。
    ///
    /// 必须在 <c>LateUpdate</c> 里做：Animator 在 Update 之后、LateUpdate 之前写入姿态。
    /// </summary>
    [DefaultExecutionOrder(200)]
    public sealed class RootMotionCanceller : MonoBehaviour
    {
        [Tooltip("骨架根节点。留空时自动按名字查找。")]
        [SerializeField] private Transform rootBone;

        [Tooltip("自动查找时使用的根节点名。")]
        [SerializeField] private string rootBoneName = "Root";

        [Tooltip("同时抵消根节点的旋转。默认关闭，否则转身类动作会失去观感。")]
        [SerializeField] private bool cancelRotation;

        private Vector3 _restPosition;
        private Quaternion _restRotation;
        private bool _ready;

        public Transform RootBone => rootBone;

        /// <summary>本帧被抵消掉的水平位移。调试与测试用。</summary>
        public Vector3 LastCancelledOffset { get; private set; }

        private void Awake()
        {
            if (rootBone == null)
            {
                rootBone = FindRootBone(transform, rootBoneName);
            }

            if (rootBone == null)
            {
                Debug.LogError(
                    $"RootMotionCanceller 找不到名为 '{rootBoneName}' 的骨架根节点；" +
                    "动画烘焙的根位移不会被抵消，角色会在动作结束时回退一小步。",
                    this);
                enabled = false;
                return;
            }

            // Awake 早于 Animator 第一次求值，因此这里读到的就是 Prefab 里的静止姿态。
            _restPosition = rootBone.localPosition;
            _restRotation = rootBone.localRotation;
            _ready = true;
        }

        private void LateUpdate()
        {
            if (!_ready)
            {
                return;
            }

            var current = rootBone.localPosition;
            LastCancelledOffset = new Vector3(
                current.x - _restPosition.x, 0f, current.z - _restPosition.z);

            // 只锁水平位置，垂直保留动画自己的值。
            rootBone.localPosition = new Vector3(_restPosition.x, current.y, _restPosition.z);

            if (cancelRotation)
            {
                rootBone.localRotation = _restRotation;
            }
        }

        private static Transform FindRootBone(Transform parent, string name)
        {
            for (var i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child.name == name)
                {
                    return child;
                }
            }

            return null;
        }
    }
}
