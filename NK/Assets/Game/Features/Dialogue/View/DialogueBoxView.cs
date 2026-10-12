using System.Globalization;
using System.Text;
using Naraka.Features.Dialogue.Controller;
using Naraka.Features.Dialogue.Model;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using VContainer;

namespace Naraka.Features.Dialogue.View
{
    /// <summary>
    /// 屏幕最底部的对话框。只显示 <see cref="DialoguePresentationState"/> 并采样点击：
    /// 每次左键按下调用一次 Advance，逐字/揭示/继续的规则全部在 Model 里。
    ///
    /// 对话期间角色的 Player 动作表被输入锁关闭，因此这里使用自己的本地输入动作读取左键与 Esc，
    /// 不修改 NarakaPlayerControls 输入资产。指针保持锁定也能读到按键，不需要显示鼠标。
    ///
    /// 文本使用 uGUI Text：工程没有包含中文字形的 TMP 字体资产，而内置动态字体会回退到系统字体。
    /// 未揭示的字以透明色占位，逐字时行不会重新折行；每揭示一个字生成一次字符串，不是每帧。
    /// </summary>
    public sealed class DialogueBoxView : MonoBehaviour
    {
        private const string HiddenOpen = "<color=#00000000>";
        private const string HiddenClose = "</color>";

        [Tooltip("对话框根对象（本组件的子物体）。没有对话时隐藏。")]
        [SerializeField] private GameObject root;

        [Tooltip("说话人姓名。")]
        [SerializeField] private Text speakerNameLabel;

        [Tooltip("逐字显示的正文。需要开启 Rich Text。")]
        [SerializeField] private Text bodyLabel;

        [Tooltip("本行完整显示后出现的继续提示。")]
        [SerializeField] private GameObject continueHint;

        [Tooltip("逐字速度（字/秒）。只是表现参数，不影响任何业务时间。")]
        [SerializeField] private float revealCharactersPerSecond = DialogueController.DefaultRevealSpeed;

        [SerializeField] private Color npcNameColor = new Color(0.96f, 0.84f, 0.55f);
        [SerializeField] private Color playerNameColor = new Color(0.66f, 0.85f, 1f);

        [Tooltip("按 Esc 关闭对话。关闭的对话不算读完，不会推进任务。")]
        [SerializeField] private bool closeWithEscape = true;

        private readonly StringBuilder _builder = new StringBuilder(256);
        private IDialogueController _dialogue;
        private InputAction _click;
        private InputAction _escape;
        private bool _attached;
        private bool _renderedOpen;
        private int _renderedConversation = -1;
        private int _renderedLine = -1;
        private int _renderedVisible = -1;
        private bool _renderedRevealed = true;
        private string _lineText = string.Empty;
        private int[] _elementStarts = new int[0];

        [Inject]
        public void Construct(IDialogueController dialogue) => _dialogue = dialogue;

        private void OnEnable()
        {
            _click = new InputAction("DialogueAdvance", InputActionType.Button, "<Mouse>/leftButton");
            _escape = new InputAction("DialogueClose", InputActionType.Button, "<Keyboard>/escape");
            _click.Enable();
            _escape.Enable();
            _renderedOpen = true;
            Render(DialoguePresentationState.Closed);
        }

        private void OnDisable()
        {
            DisposeAction(ref _click);
            DisposeAction(ref _escape);
            if (_attached)
            {
                _attached = false;
                _dialogue.DetachPresenter();
            }
        }

        private void Update()
        {
            if (_dialogue == null)
            {
                return;
            }

            if (!_attached)
            {
                // 注入可能晚于 OnEnable：在第一次拿到控制器时登记，告诉它场景里有对话框可以显示。
                _attached = true;
                _dialogue.AttachPresenter();
            }

            _dialogue.SetRevealSpeed(revealCharactersPerSecond);
            _dialogue.ReportPointerHeld(_click.IsPressed());
            if (_dialogue.IsOpen)
            {
                if (_click.WasPressedThisFrame())
                {
                    _dialogue.Advance();
                }
                else if (closeWithEscape && _escape.WasPressedThisFrame())
                {
                    _dialogue.Cancel();
                }
            }

            Render(_dialogue.Current);
        }

        private void Render(DialoguePresentationState state)
        {
            // 根对象必须是本组件的子物体：隐藏自己会让 Update 停止，对话框再也打不开。
            if (state.IsOpen != _renderedOpen && root != null && root != gameObject)
            {
                root.SetActive(state.IsOpen);
            }

            _renderedOpen = state.IsOpen;
            if (!state.IsOpen)
            {
                _renderedConversation = -1;
                _renderedLine = -1;
                return;
            }

            if (state.ConversationId != _renderedConversation || state.LineIndex != _renderedLine)
            {
                _renderedConversation = state.ConversationId;
                _renderedLine = state.LineIndex;
                _renderedVisible = -1;
                _lineText = state.Text;
                _elementStarts = StringInfo.ParseCombiningCharacters(_lineText);
                if (speakerNameLabel != null)
                {
                    speakerNameLabel.text = state.SpeakerName;
                    speakerNameLabel.color = state.Speaker == DialogueSpeakerKind.Player ? playerNameColor : npcNameColor;
                }
            }

            if (state.VisibleElements != _renderedVisible && bodyLabel != null)
            {
                _renderedVisible = state.VisibleElements;
                bodyLabel.text = BuildRevealText(_lineText, _elementStarts, state.VisibleElements, _builder);
            }

            if (state.IsLineRevealed != _renderedRevealed && continueHint != null)
            {
                continueHint.SetActive(state.IsLineRevealed);
            }

            _renderedRevealed = state.IsLineRevealed;
        }

        /// <summary>
        /// 已揭示部分正常显示，其余部分以透明色占位，保证整行排版从第一个字起就固定。
        /// 按文本元素切分，代理对与组合字符不会被拆开。
        /// </summary>
        public static string BuildRevealText(string text, int[] elementStarts, int visibleElements, StringBuilder builder)
        {
            text = text ?? string.Empty;
            if (elementStarts == null || visibleElements >= elementStarts.Length)
            {
                return text;
            }

            var cut = visibleElements <= 0 ? 0 : elementStarts[visibleElements];
            builder.Clear();
            builder.Append(text, 0, cut);
            builder.Append(HiddenOpen);
            builder.Append(text, cut, text.Length - cut);
            builder.Append(HiddenClose);
            return builder.ToString();
        }

        private static void DisposeAction(ref InputAction action)
        {
            if (action == null)
            {
                return;
            }

            action.Disable();
            action.Dispose();
            action = null;
        }
    }
}
