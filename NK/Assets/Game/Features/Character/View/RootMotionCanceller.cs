using UnityEngine;

namespace Naraka.Features.Character.View
{
    /// <summary>
    /// 根位移抵消器。
    ///
    /// 这些动画在根骨骼（`Root`）上烘焙了整体位移，必须被抵消掉，否则：
    ///
    /// - 动作播放时整个骨架相对 GameObject 偏移，与代码驱动的位移叠加；
    /// - 动作结束交叉淡入回 Idle 时，骨架被插值从偏移后的位置拉回原点，
    ///   看上去就是**角色突然一跳**。
    ///
    /// 为什么用代码抵消而不用 Unity 的 Root Motion 提取：正式角色模型与全部动画
    /// 都是 Generic 且 `avatarSetup = NoAvatar`，没有 Avatar 就没有 Root Motion 节点，
    /// 在导入器上设 `motionNodeName` 是无效的。要走那条路就得给角色生成 Avatar
    /// 并重新导入全部动画，那是对用户美术资源的大改动。
    ///
    /// 因此抵消范围是**水平位移**，垂直分量保留（<c>preserveVertical</c> 默认打开）：
    ///
    /// - **水平**：全部由代码驱动（见 ADR-0015）。实测片段里烘焙的水平行程与配置值
    ///   分毫不差 —— 冲刺 12.276、V 技能 17.837、停止走路 1.439、停止奔跑 1.417
    ///   —— 所以动画那一份必须抵消掉，否则与代码驱动的位移叠加成两倍。
    /// - **垂直**：蓄力（`Attack10`）的腾空就烘焙在这条通道上：升到 **+5.227**
    ///   （角色身高 4.04，跳得比自己还高）再落回 +0.062。锁掉它就等于把"飞上天空"
    ///   抹成"原地旋转"。垂直净变化恒为 0，因此保留它不会让角色停在空中。
    ///   这条不变量由 <c>PlayerAnimationRootFixup</c> 在导入期保证：
    ///   净变化不为 0 的垂直通道是导出残留（实测 `Attack01` 为 −3.167），会被压平。
    /// - **旋转**：默认保留，因为 `Attack02` 这类"转身再转身"的动作靠它才有观感。
    ///   `Run_Turnback` 带 180° 净旋转，需要时可以单独打开 <c>cancelRotation</c>。
    ///
    /// 2026-10-04 的两次往返值得记下来，省得以后再绕一遍：新动画的导出把整条根通道
    /// 绕 X 轴转了 +90° 并缩了 1/2.54，于是"向上"被读成"向前"、"向前"被读成"向下"。
    /// 当时这里一度改成锁死三个轴来止血，代价就是蓄力的腾空一起消失。
    /// 正确的分工是：**轴向错位在导入期按片段还原**
    /// （<c>Naraka.EditorTools.PlayerAnimationRootFixup</c>，运行期分不清新旧片段，
    /// 交叉淡入期间补偿量更无从定义），**这里只负责抵消代码已经驱动的那一部分**。
    /// 见 ADR-0015 的 2026-10-04 第二、三次修订。
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

        [Tooltip("只锁水平位移、保留根节点的垂直分量。" +
                 "默认打开：蓄力的腾空烘焙在垂直通道上，关掉它蓄力就变成原地旋转。")]
        [SerializeField] private bool preserveVertical = true;

        private Vector3 _restPosition;
        private Quaternion _restRotation;
        private bool _ready;

        public Transform RootBone => rootBone;

        /// <summary>本帧被抵消掉的位移。调试与测试用。</summary>
        public Vector3 LastCancelledOffset { get; private set; }

        /// <summary>是否保留根节点的垂直分量。PlayMode 测试据此断言默认行为。</summary>
        public bool PreserveVertical => preserveVertical;

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
            var target = preserveVertical
                ? new Vector3(_restPosition.x, current.y, _restPosition.z)
                : _restPosition;
            LastCancelledOffset = current - target;
            rootBone.localPosition = target;

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
