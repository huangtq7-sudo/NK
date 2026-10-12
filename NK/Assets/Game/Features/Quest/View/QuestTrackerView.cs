using Naraka.Features.Quest.Controller;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace Naraka.Features.Quest.View
{
    /// <summary>
    /// 任务追踪栏。只显示服务端日志推导出的追踪任务与当前步骤，不提供任何推进或发奖入口。
    /// 布局与位置由用户决定；左上是小地图、左下是头像、底部是快捷栏，建议放在右侧中部。
    /// </summary>
    public sealed class QuestTrackerView : MonoBehaviour
    {
        [Tooltip("追踪栏根对象（本组件的子物体）。没有可追踪任务时隐藏。")]
        [SerializeField] private GameObject root;

        [SerializeField] private Text titleLabel;

        [SerializeField] private Text objectiveLabel;

        [Tooltip("可选。服务器功能未升级等状态提示。")]
        [SerializeField] private Text statusLabel;

        private IQuestController _quests;
        private bool _renderedVisible = true;
        private string _renderedTitle;
        private string _renderedObjective;
        private string _renderedStatus;

        [Inject]
        public void Construct(IQuestController quests) => _quests = quests;

        private void OnEnable() => SetVisible(false);

        private void Update()
        {
            if (_quests == null)
            {
                return;
            }

            var state = _quests.Current;
            var status = state.IsSupported ? string.Empty : state.StatusMessage;
            SetVisible(state.HasTrackedQuest || status.Length != 0);
            Write(titleLabel, state.TrackedTitle, ref _renderedTitle);
            Write(objectiveLabel, state.TrackedObjectiveText, ref _renderedObjective);
            Write(statusLabel, status, ref _renderedStatus);
        }

        private void SetVisible(bool visible)
        {
            if (visible == _renderedVisible)
            {
                return;
            }

            _renderedVisible = visible;
            if (root != null && root != gameObject)
            {
                root.SetActive(visible);
            }
        }

        private static void Write(Text label, string value, ref string rendered)
        {
            if (label == null || value == rendered)
            {
                return;
            }

            rendered = value;
            label.text = value;
        }
    }
}
