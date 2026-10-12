using Naraka.Features.Character.View;
using Naraka.Features.Quest.Controller;
using UnityEngine;
using VContainer;

namespace Naraka.Features.Quest.View
{
    /// <summary>
    /// NPC 的球形交互范围。只在 Unity 边界产生"玩家在不在范围内、离多近"的候选，
    /// 不判断任务状态，也不决定播哪段对话。半径与位置由场景里的 SphereCollider 决定。
    /// </summary>
    [RequireComponent(typeof(SphereCollider))]
    public sealed class NpcInteractionVolume : MonoBehaviour
    {
        [Tooltip("稳定 NpcId，必须与 Config/Source/npcs.csv 一致，例如 npc_shuangchi。")]
        [SerializeField] private string npcId = string.Empty;

        [Tooltip("计算距离用的锚点，通常是 NPC 根节点。为空时使用本物体。")]
        [SerializeField] private Transform anchor;

        [Tooltip("范围内距离上报间隔（秒）。重叠范围按距离选最近的 NPC，不需要每帧计算。")]
        [SerializeField] private float reportIntervalSeconds = 0.1f;

        [Header("头顶标记（可选）")]
        [SerializeField] private GameObject availableMarker;
        [SerializeField] private GameObject objectiveMarker;
        [SerializeField] private GameObject submitMarker;
        [SerializeField] private GameObject rewardMarker;

        private INpcInteractionController _interaction;
        private IQuestController _quests;
        private SphereCollider _sphere;
        private Transform _player;
        private float _nextReport;
        private QuestNpcMarker _renderedMarker = (QuestNpcMarker)(-1);

        public string NpcId => npcId;

        public bool IsPlayerInside => _player != null;

        [Inject]
        public void Construct(INpcInteractionController interaction, IQuestController quests)
        {
            _interaction = interaction;
            _quests = quests;
        }

        private void Awake()
        {
            _sphere = GetComponent<SphereCollider>();
            _sphere.isTrigger = true;
            if (string.IsNullOrWhiteSpace(npcId))
            {
                Debug.LogError($"{name} 的 NpcInteractionVolume 没有填写 NpcId，交互不会生效。", this);
            }
        }

        private void OnDisable()
        {
            if (_player != null)
            {
                _player = null;
                _interaction?.ReportCandidate(npcId, false, 0f);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            var player = other.GetComponentInParent<PlayerCharacterView>();
            if (player == null || string.IsNullOrWhiteSpace(npcId))
            {
                return;
            }

            _player = player.transform;
            _nextReport = 0f;
            Report();
        }

        private void OnTriggerExit(Collider other)
        {
            var player = other.GetComponentInParent<PlayerCharacterView>();
            if (player == null || _player == null || player.transform != _player)
            {
                return;
            }

            _player = null;
            _interaction?.ReportCandidate(npcId, false, 0f);
        }

        private void Update()
        {
            if (_player != null && Time.unscaledTime >= _nextReport)
            {
                Report();
            }

            RenderMarker();
        }

        private void Report()
        {
            _nextReport = Time.unscaledTime + Mathf.Max(0.02f, reportIntervalSeconds);
            if (_interaction == null)
            {
                return;
            }

            if (_player == null)
            {
                _interaction.ReportCandidate(npcId, false, 0f);
                return;
            }

            var origin = anchor != null ? anchor.position : transform.position;
            var offset = _player.position - origin;
            offset.y = 0f;
            var distance = offset.magnitude;

            // 传送、死亡重生会先停用再启用 CharacterController，Unity 不会为此发送 OnTriggerExit。
            // 超出球形范围 1 米即按离开处理，避免提示残留在远处。
            if (distance > WorldRadius() + 1f)
            {
                _player = null;
                _interaction.ReportCandidate(npcId, false, 0f);
                return;
            }

            _interaction.ReportCandidate(npcId, true, distance);
        }

        private float WorldRadius()
        {
            if (_sphere == null)
            {
                return float.MaxValue;
            }

            var scale = transform.lossyScale;
            return _sphere.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        }

        private void RenderMarker()
        {
            var marker = _quests == null ? QuestNpcMarker.None : _quests.Current.MarkerFor(npcId);
            if (marker == _renderedMarker)
            {
                return;
            }

            _renderedMarker = marker;
            SetActive(availableMarker, marker == QuestNpcMarker.Available);
            SetActive(objectiveMarker, marker == QuestNpcMarker.Objective);
            SetActive(submitMarker, marker == QuestNpcMarker.ReadyToSubmit);
            SetActive(rewardMarker, marker == QuestNpcMarker.RewardPending);
        }

        private static void SetActive(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active)
            {
                target.SetActive(active);
            }
        }
    }
}
