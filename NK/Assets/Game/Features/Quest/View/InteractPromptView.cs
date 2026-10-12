using Naraka.Features.Quest.Controller;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace Naraka.Features.Quest.View
{
    /// <summary>
    /// "E 交谈"提示与短提示。只显示 <see cref="NpcInteractionPresentationState"/>，不读取输入、不判断任务。
    /// </summary>
    public sealed class InteractPromptView : MonoBehaviour
    {
        [Tooltip("交互提示根对象（本组件的子物体）。范围内且可以交互时显示。")]
        [SerializeField] private GameObject promptRoot;

        [SerializeField] private Text promptLabel;

        [Tooltip("提示文字格式，{0} 为 NPC 姓名。")]
        [SerializeField] private string promptFormat = "E  交谈 · {0}";

        [Tooltip("短提示根对象（本组件的子物体），例如\"任务同步中\"或领取结果。")]
        [SerializeField] private GameObject noticeRoot;

        [SerializeField] private Text noticeLabel;

        [Tooltip("短提示显示秒数。只是表现计时。")]
        [SerializeField] private float noticeSeconds = 2.5f;

        private INpcInteractionController _interaction;
        private bool _renderedPrompt = true;
        private string _renderedNpc;
        private int _renderedNoticeVersion;
        private float _noticeHideAt;
        private bool _noticeVisible = true;

        [Inject]
        public void Construct(INpcInteractionController interaction) => _interaction = interaction;

        private void OnEnable()
        {
            SetPrompt(false);
            SetNotice(false);
        }

        private void Update()
        {
            if (_interaction == null)
            {
                return;
            }

            var state = _interaction.Current;
            SetPrompt(state.PromptVisible);
            if (state.PromptVisible && promptLabel != null && state.FocusedNpcName != _renderedNpc)
            {
                _renderedNpc = state.FocusedNpcName;
                promptLabel.text = string.Format(promptFormat, state.FocusedNpcName);
            }

            if (state.NoticeVersion != _renderedNoticeVersion)
            {
                _renderedNoticeVersion = state.NoticeVersion;
                if (noticeLabel != null)
                {
                    noticeLabel.text = state.Notice;
                }

                _noticeHideAt = Time.unscaledTime + noticeSeconds;
                SetNotice(state.Notice.Length != 0);
            }
            else if (_noticeVisible && Time.unscaledTime >= _noticeHideAt)
            {
                SetNotice(false);
            }
        }

        private void SetPrompt(bool visible)
        {
            if (visible == _renderedPrompt)
            {
                return;
            }

            _renderedPrompt = visible;
            if (promptRoot != null && promptRoot != gameObject)
            {
                promptRoot.SetActive(visible);
            }
        }

        private void SetNotice(bool visible)
        {
            if (visible == _noticeVisible)
            {
                return;
            }

            _noticeVisible = visible;
            if (noticeRoot != null && noticeRoot != gameObject)
            {
                noticeRoot.SetActive(visible);
            }
        }
    }
}
