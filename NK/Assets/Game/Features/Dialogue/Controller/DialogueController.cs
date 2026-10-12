using System;
using System.Collections.Generic;
using Naraka.Core.Application.MVC;
using Naraka.Core.Application.Presentation;
using Naraka.Features.Character.Controller;
using Naraka.Features.Dialogue.Model;

namespace Naraka.Features.Dialogue.Controller
{
    public enum DialogueEndReason
    {
        /// <summary>最后一行揭示后再次点击，对话读完。</summary>
        Completed = 0,

        /// <summary>关闭、离开范围、死亡、场景卸载或断线。读到一半不算读完。</summary>
        Cancelled = 1
    }

    /// <summary>底部对话框的只读状态。View 只把这些字段写到文本与提示上。</summary>
    public readonly struct DialoguePresentationState : IPresentationState
    {
        public DialoguePresentationState(
            bool isOpen,
            int conversationId,
            DialogueSpeakerKind speaker,
            string speakerName,
            string text,
            int visibleElements,
            int totalElements,
            bool isLineRevealed,
            int lineIndex,
            int lineCount)
        {
            IsOpen = isOpen;
            ConversationId = conversationId;
            Speaker = speaker;
            SpeakerName = speakerName ?? string.Empty;
            Text = text ?? string.Empty;
            VisibleElements = visibleElements;
            TotalElements = totalElements;
            IsLineRevealed = isLineRevealed;
            LineIndex = lineIndex;
            LineCount = lineCount;
        }

        public bool IsOpen { get; }

        /// <summary>每次开始新对话都会变化，View 据此判断是否需要重新设置文本。</summary>
        public int ConversationId { get; }

        public DialogueSpeakerKind Speaker { get; }

        public string SpeakerName { get; }

        public string Text { get; }

        public int VisibleElements { get; }

        public int TotalElements { get; }

        /// <summary>本行已完整显示，可以显示"继续"提示。</summary>
        public bool IsLineRevealed { get; }

        public int LineIndex { get; }

        public int LineCount { get; }

        public static DialoguePresentationState Closed => new DialoguePresentationState(
            false, 0, DialogueSpeakerKind.Npc, string.Empty, string.Empty, 0, 0, false, 0, 0);
    }

    public interface IDialogueController : IReadOnlyState<DialoguePresentationState>
    {
        bool IsOpen { get; }

        /// <summary>场景里是否有对话框 View。没有 View 时不开始任何对话，否则玩家会被锁住却看不到文字。</summary>
        bool HasPresenter { get; }

        void AttachPresenter();

        void DetachPresenter();

        /// <summary>开始一段对话。已有对话、没有对话框 View 或已释放时返回 false。</summary>
        bool TryStart(DialogueScript script, Action<DialogueEndReason> onEnd);

        /// <summary>推进逐字表现，并在关闭后检查是否可以归还角色输入。</summary>
        void Tick(float deltaSeconds);

        /// <summary>一次点击。只在按下的那一帧调用一次。</summary>
        void Advance();

        void Cancel();

        /// <summary>逐字速度（文本元素/秒）。表现参数，由 View 的序列化字段提供。</summary>
        void SetRevealSpeed(float elementsPerSecond);

        /// <summary>View 每帧报告左键是否仍按着。对话关闭后要等松开才归还角色输入。</summary>
        void ReportPointerHeld(bool held);
    }

    /// <summary>
    /// 底部对话的编排。它不知道任务：读完与否通过回调交给调用方（任务交互控制器），
    /// 由调用方决定是否向服务端发出意图。对话期间锁定角色输入，但不暂停世界。
    /// </summary>
    public sealed class DialogueController : IController, IDialogueController, IDisposable
    {
        /// <summary>未配置时的默认逐字速度。</summary>
        public const float DefaultRevealSpeed = 30f;

        private readonly IGameplayInputLock _inputLock;
        private readonly ReactiveState<DialoguePresentationState> _state;
        private DialogueSession _session;
        private Action<DialogueEndReason> _onEnd;
        private float _revealSpeed = DefaultRevealSpeed;
        private int _conversationId;
        private int _presenters;
        private bool _pointerHeld;
        private bool _awaitingPointerRelease;
        private bool _disposed;

        public DialogueController(IGameplayInputLock inputLock)
        {
            _inputLock = inputLock ?? throw new ArgumentNullException(nameof(inputLock));
            _state = new ReactiveState<DialoguePresentationState>(DialoguePresentationState.Closed);
        }

        public DialoguePresentationState Current => _state.Current;

        public bool IsOpen => _session != null;

        public bool HasPresenter => _presenters > 0;

        /// <summary>输入锁是否仍被对话占用（包括关闭后等待左键松开的那段）。</summary>
        public bool HoldsInputLock => _session != null || _awaitingPointerRelease;

        /// <summary>开始一段对话。已有对话、没有对话框 View 或已释放时返回 false。</summary>
        public bool TryStart(DialogueScript script, Action<DialogueEndReason> onEnd)
        {
            if (_disposed || script == null || _session != null || !HasPresenter)
            {
                return false;
            }

            _session = new DialogueSession(script);
            _onEnd = onEnd;
            _conversationId++;
            _awaitingPointerRelease = false;
            _inputLock.Acquire(this);
            Publish();
            return true;
        }

        public void Tick(float deltaSeconds)
        {
            if (_disposed)
            {
                return;
            }

            if (_session != null && _session.Tick(deltaSeconds, _revealSpeed))
            {
                Publish();
            }

            TryReleaseInputLock();
        }

        public void Advance()
        {
            if (_disposed || _session == null)
            {
                return;
            }

            switch (_session.Advance())
            {
                case DialogueAdvanceResult.RevealedLine:
                case DialogueAdvanceResult.NextLine:
                    Publish();
                    break;
                case DialogueAdvanceResult.Finished:
                    End(DialogueEndReason.Completed);
                    break;
            }
        }

        public void Cancel()
        {
            if (_session != null)
            {
                End(DialogueEndReason.Cancelled);
            }
        }

        public void SetRevealSpeed(float elementsPerSecond)
        {
            _revealSpeed = elementsPerSecond;
        }

        public void ReportPointerHeld(bool held)
        {
            _pointerHeld = held;
            TryReleaseInputLock();
        }

        public void AttachPresenter()
        {
            _presenters++;
        }

        public void DetachPresenter()
        {
            if (_presenters > 0)
            {
                _presenters--;
            }

            if (_presenters == 0)
            {
                // 没有 View 就没有人报告指针状态：取消对话并立即归还输入，避免角色被永久锁住。
                Cancel();
                _awaitingPointerRelease = false;
                _inputLock.Release(this);
            }
        }

        public IDisposable Subscribe(IObserver<DialoguePresentationState> observer) => _state.Subscribe(observer);

        /// <summary>场景卸载时调用。不回调调用方：此时任务交互控制器同样正在释放。</summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _session = null;
            _onEnd = null;
            _awaitingPointerRelease = false;
            _inputLock.Release(this);
            _state.Dispose();
        }

        private void End(DialogueEndReason reason)
        {
            var callback = _onEnd;
            _session = null;
            _onEnd = null;
            _awaitingPointerRelease = true;
            Publish();
            TryReleaseInputLock();
            callback?.Invoke(reason);
        }

        private void TryReleaseInputLock()
        {
            // 关闭对话的那一下左键如果还按着，角色输入一恢复就会读成普通攻击或蓄力。
            if (_awaitingPointerRelease && !_pointerHeld)
            {
                _awaitingPointerRelease = false;
                _inputLock.Release(this);
            }
        }

        private void Publish()
        {
            if (_disposed)
            {
                return;
            }

            if (_session == null)
            {
                _state.Set(DialoguePresentationState.Closed);
                return;
            }

            var line = _session.CurrentLine;
            _state.Set(new DialoguePresentationState(
                true,
                _conversationId,
                line.Speaker,
                line.SpeakerName,
                line.Text,
                _session.VisibleElements,
                line.ElementCount,
                _session.Phase == DialoguePhase.Revealed,
                _session.LineIndex,
                _session.Script.Lines.Count));
        }
    }

    /// <summary>
    /// 世界内的角色输入锁。对话、全屏地图、Tab 等界面各自作为持有者加锁，
    /// 只要还有一个持有者，角色输入就保持锁定；全部释放后才归还。不改变 Time.timeScale。
    /// </summary>
    public interface IGameplayInputLock
    {
        bool IsLocked { get; }

        void Acquire(object owner);

        void Release(object owner);
    }

    public sealed class PlayerGameplayInputLock : IGameplayInputLock, IDisposable
    {
        private readonly IPlayerController _player;
        private readonly HashSet<object> _owners = new HashSet<object>();

        public PlayerGameplayInputLock(IPlayerController player)
        {
            _player = player ?? throw new ArgumentNullException(nameof(player));
        }

        public bool IsLocked => _owners.Count > 0;

        public void Acquire(object owner)
        {
            if (owner != null && _owners.Add(owner) && _owners.Count == 1)
            {
                Apply();
            }
        }

        public void Release(object owner)
        {
            if (owner != null && _owners.Remove(owner) && _owners.Count == 0)
            {
                Apply();
            }
        }

        public void Dispose()
        {
            if (_owners.Count == 0)
            {
                return;
            }

            _owners.Clear();
            Apply();
        }

        private void Apply()
        {
            if (!_player.IsDisposed)
            {
                _player.SetInputLocked(_owners.Count > 0);
            }
        }
    }
}
