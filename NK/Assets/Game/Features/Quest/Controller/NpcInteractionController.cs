using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Cysharp.Threading.Tasks;
using Naraka.Config;
using Naraka.Core.Application.Config;
using Naraka.Core.Application.MVC;
using Naraka.Core.Application.Presentation;
using Naraka.Features.Account.Model;
using Naraka.Features.Character.Controller;
using Naraka.Features.Character.Model;
using Naraka.Features.Dialogue.Controller;
using Naraka.Features.Dialogue.Model;
using Naraka.Features.Quest.Model;

namespace Naraka.Features.Quest.Controller
{
    /// <summary>NPC 交互提示与领取面板的只读状态。</summary>
    public readonly struct NpcInteractionPresentationState : IPresentationState
    {
        public NpcInteractionPresentationState(
            string focusedNpcId,
            string focusedNpcName,
            bool promptVisible,
            bool isRewardPanelOpen,
            string rewardQuestId,
            string rewardTitle,
            IReadOnlyList<string> rewardLines,
            bool isClaimInFlight,
            string notice,
            int noticeVersion)
        {
            FocusedNpcId = focusedNpcId ?? string.Empty;
            FocusedNpcName = focusedNpcName ?? string.Empty;
            PromptVisible = promptVisible;
            IsRewardPanelOpen = isRewardPanelOpen;
            RewardQuestId = rewardQuestId ?? string.Empty;
            RewardTitle = rewardTitle ?? string.Empty;
            RewardLines = rewardLines ?? Array.Empty<string>();
            IsClaimInFlight = isClaimInFlight;
            Notice = notice ?? string.Empty;
            NoticeVersion = noticeVersion;
        }

        public string FocusedNpcId { get; }

        public string FocusedNpcName { get; }

        /// <summary>范围内有 NPC，且当前可以按 E 发起对话。</summary>
        public bool PromptVisible { get; }

        public bool IsRewardPanelOpen { get; }

        public string RewardQuestId { get; }

        public string RewardTitle { get; }

        /// <summary>领取前是配置预览，领取后是服务端返回的实际发放。</summary>
        public IReadOnlyList<string> RewardLines { get; }

        public bool IsClaimInFlight { get; }

        public string Notice { get; }

        /// <summary>每条新提示递增，View 据此重新开始提示的显示计时。</summary>
        public int NoticeVersion { get; }

        public static NpcInteractionPresentationState Initial => new NpcInteractionPresentationState(
            string.Empty, string.Empty, false, false, string.Empty, string.Empty, null, false, string.Empty, 0);
    }

    public interface INpcInteractionController : IReadOnlyState<NpcInteractionPresentationState>
    {
        /// <summary>球形范围的进入/离开与距离。由场景里的 NPC View 报告，Collider 不进入 Controller。</summary>
        void ReportCandidate(string npcId, bool inRange, float distance);

        /// <summary>按下交互键。</summary>
        void Interact();

        /// <summary>领取面板上的领取按钮。</summary>
        void RequestClaim();

        void CloseRewardPanel();

        /// <summary>追踪栏的领取按钮：打开当前追踪任务的领取面板。</summary>
        void OpenTrackedRewardPanel();
    }

    /// <summary>
    /// Map01 的 NPC 交互编排。范围检测只产生候选；按 E 时按任务状态选择对白并交给对话控制器，
    /// 对话读完后才向服务端发出接取/目标/提交意图。View 不判任务完成，也不发奖。
    /// </summary>
    public sealed class NpcInteractionController : IController, INpcInteractionController, IDisposable
    {
        /// <summary>新候选必须比当前焦点近这么多才切换，避免两个范围重叠处来回闪烁。</summary>
        public const float FocusHysteresis = 0.3f;

        private const string FallbackPlayerName = "你";

        private readonly IQuestController _quests;
        private readonly IDialogueController _dialogue;
        private readonly IPlayerController _player;
        private readonly IInteractInputSource _input;
        private readonly IGameConfigProvider _config;
        private readonly IGameplayInputLock _inputLock;
        private readonly AccountSessionModel _session;
        private readonly ReactiveState<NpcInteractionPresentationState> _state;
        private readonly Dictionary<string, float> _candidates = new Dictionary<string, float>(StringComparer.Ordinal);
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private string _focusedNpcId = string.Empty;
        private string _conversationNpcId = string.Empty;
        private string _rewardQuestId = string.Empty;
        private IReadOnlyList<string> _rewardLines = Array.Empty<string>();
        private bool _rewardPanelOpen;
        private bool _claimInFlight;
        private string _notice = string.Empty;
        private int _noticeVersion;
        private bool _disposed;

        public NpcInteractionController(
            IQuestController quests,
            IDialogueController dialogue,
            IPlayerController player,
            IInteractInputSource input,
            IGameConfigProvider config,
            IGameplayInputLock inputLock,
            AccountSessionModel session)
        {
            _quests = quests ?? throw new ArgumentNullException(nameof(quests));
            _dialogue = dialogue ?? throw new ArgumentNullException(nameof(dialogue));
            _player = player ?? throw new ArgumentNullException(nameof(player));
            _input = input;
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _inputLock = inputLock ?? throw new ArgumentNullException(nameof(inputLock));
            _session = session;
            _state = new ReactiveState<NpcInteractionPresentationState>(NpcInteractionPresentationState.Initial);
        }

        public NpcInteractionPresentationState Current => _state.Current;

        /// <summary>进入地图时同步一次任务日志。没有任务能力的旧 Host 不会收到任何任务协议。</summary>
        public void Start()
        {
            if (_quests.IsSupported)
            {
                RefreshAsync().Forget();
            }
        }

        /// <summary>每帧由组合根推进：读取交互键、推进逐字表现、处理死亡与离开范围。</summary>
        public void Tick(float deltaSeconds)
        {
            if (_disposed)
            {
                return;
            }

            if (_input != null && _input.InteractPressed)
            {
                Interact();
            }

            _dialogue.Tick(deltaSeconds);
            if (_dialogue.IsOpen)
            {
                var player = _player.IsDisposed ? default : _player.Current;
                if (_player.IsDisposed || !player.IsAlive || player.Reaction == ReactionState.Death ||
                    !_candidates.ContainsKey(_conversationNpcId))
                {
                    _dialogue.Cancel();
                }
            }

            var promptVisible = CanStartConversation();
            if (promptVisible != _state.Current.PromptVisible)
            {
                Publish();
            }
        }

        public void ReportCandidate(string npcId, bool inRange, float distance)
        {
            if (_disposed || string.IsNullOrEmpty(npcId))
            {
                return;
            }

            if (inRange)
            {
                _candidates[npcId] = Math.Max(0f, distance);
            }
            else
            {
                _candidates.Remove(npcId);
            }

            var focus = SelectFocus();
            if (!string.Equals(focus, _focusedNpcId, StringComparison.Ordinal))
            {
                _focusedNpcId = focus;
                Publish();
            }
        }

        public void Interact()
        {
            if (!CanStartConversation())
            {
                return;
            }

            var content = _quests.Content;
            if (content == null)
            {
                Notify(QuestOperationMessages.Describe(QuestOperationStatus.ConfigUnavailable));
                return;
            }

            if (_quests.IsBusy)
            {
                Notify("任务同步中，请稍候。");
                return;
            }

            if (_quests.IsSupported && _quests.Journal == null)
            {
                // 日志还没同步就不发任何意图：先同步，玩家再按一次。
                Notify("任务同步中，请稍候。");
                RefreshAsync().Forget();
                return;
            }

            var conversation = QuestConversationPlanner.Plan(
                _focusedNpcId, _quests.IsSupported ? _quests.Journal : null, content);
            var script = conversation == null ? null : BuildScript(conversation, content);
            if (script == null)
            {
                Notify(QuestOperationMessages.Describe(QuestOperationStatus.ConfigUnavailable));
                return;
            }

            var npcId = _focusedNpcId;
            if (!_dialogue.TryStart(script, reason => OnConversationEnded(conversation, reason)))
            {
                Notify("对话界面尚未就绪。");
                return;
            }

            _conversationNpcId = npcId;
            Publish();
        }

        public void RequestClaim()
        {
            if (_disposed || !_rewardPanelOpen || _claimInFlight || string.IsNullOrEmpty(_rewardQuestId))
            {
                return;
            }

            ClaimAsync(_rewardQuestId).Forget();
        }

        public void CloseRewardPanel()
        {
            if (!_rewardPanelOpen || _claimInFlight)
            {
                return;
            }

            SetRewardPanelOpen(false);
            Publish();
        }

        public void OpenTrackedRewardPanel()
        {
            var state = _quests.Current;
            if (state.CanClaimTracked)
            {
                OpenRewardPanel(state.TrackedQuestId);
            }
        }

        public IDisposable Subscribe(IObserver<NpcInteractionPresentationState> observer) =>
            _state.Subscribe(observer);

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _inputLock.Release(this);
            _lifetime.Cancel();
            _lifetime.Dispose();
            _candidates.Clear();
            _state.Dispose();
        }

        private bool CanStartConversation()
        {
            if (_disposed || string.IsNullOrEmpty(_focusedNpcId) || _dialogue.IsOpen || _rewardPanelOpen ||
                !_dialogue.HasPresenter || _player.IsDisposed)
            {
                return false;
            }

            // 只在站立/移动时交互：动作、受击、死亡、加载与出场期间都不弹对话。
            var player = _player.Current;
            return player.IsAlive &&
                   player.Action == ActionState.None &&
                   player.Reaction == ReactionState.None &&
                   !player.IsInputLocked;
        }

        private string SelectFocus()
        {
            var best = string.Empty;
            var bestDistance = float.MaxValue;
            foreach (var pair in _candidates)
            {
                if (pair.Value < bestDistance - 0.0001f ||
                    (Math.Abs(pair.Value - bestDistance) <= 0.0001f &&
                     string.CompareOrdinal(pair.Key, best) < 0))
                {
                    best = pair.Key;
                    bestDistance = pair.Value;
                }
            }

            if (!string.IsNullOrEmpty(_focusedNpcId) &&
                _candidates.TryGetValue(_focusedNpcId, out var currentDistance) &&
                bestDistance > currentDistance - FocusHysteresis)
            {
                return _focusedNpcId;
            }

            return best;
        }

        private DialogueScript BuildScript(QuestConversation conversation, QuestContentCatalog content)
        {
            var lines = content.GetLines(conversation.DialogueId);
            if (lines.Count == 0 || !content.TryGetNpc(conversation.NpcId, out var npc))
            {
                return null;
            }

            var playerName = _session != null && !string.IsNullOrEmpty(_session.Username)
                ? _session.Username
                : FallbackPlayerName;
            var built = new DialogueLine[lines.Count];
            for (var i = 0; i < lines.Count; i++)
            {
                var isPlayer = lines[i].Speaker == ConfigDialogueSpeaker.Player;
                built[i] = new DialogueLine(
                    isPlayer ? DialogueSpeakerKind.Player : DialogueSpeakerKind.Npc,
                    isPlayer ? playerName : npc.DisplayName,
                    lines[i].Text);
            }

            return new DialogueScript(conversation.DialogueId, built);
        }

        private void OnConversationEnded(QuestConversation conversation, DialogueEndReason reason)
        {
            _conversationNpcId = string.Empty;
            Publish();
            if (_disposed || reason != DialogueEndReason.Completed)
            {
                return;
            }

            switch (conversation.Intent)
            {
                case QuestConversationIntent.Accept:
                    RunIntentAsync(
                        token => _quests.AcceptAsync(conversation.QuestId, conversation.DialogueId, token),
                        null).Forget();
                    break;
                case QuestConversationIntent.CompleteObjective:
                    RunIntentAsync(
                        token => _quests.CompleteDialogueAsync(
                            conversation.QuestId, conversation.ObjectiveId, conversation.DialogueId, token),
                        null).Forget();
                    break;
                case QuestConversationIntent.Submit:
                    RunIntentAsync(
                        token => _quests.SubmitAsync(conversation.QuestId, conversation.DialogueId, token),
                        () => OpenRewardPanel(conversation.QuestId)).Forget();
                    break;
                case QuestConversationIntent.OpenClaim:
                    OpenRewardPanel(conversation.QuestId);
                    break;
            }
        }

        private async UniTaskVoid RunIntentAsync(
            Func<CancellationToken, UniTask<QuestOperationStatus>> operation,
            Action onSuccess)
        {
            try
            {
                var status = await operation(_lifetime.Token);
                if (_disposed)
                {
                    return;
                }

                if (status == QuestOperationStatus.Success)
                {
                    onSuccess?.Invoke();
                }
                else
                {
                    Notify(QuestOperationMessages.Describe(status));
                }
            }
            catch (OperationCanceledException)
            {
                // 场景卸载或断线：结果以下一次同步的服务端日志为准。
            }
        }

        private async UniTaskVoid RefreshAsync()
        {
            try
            {
                await _quests.RefreshAsync(_lifetime.Token);
            }
            catch (OperationCanceledException)
            {
            }
        }

        private void OpenRewardPanel(string questId)
        {
            if (_disposed)
            {
                return;
            }

            _rewardQuestId = questId ?? string.Empty;
            _rewardLines = DescribeConfiguredRewards(_rewardQuestId);
            SetRewardPanelOpen(true);
            Publish();
        }

        private async UniTaskVoid ClaimAsync(string questId)
        {
            _claimInFlight = true;
            Publish();
            try
            {
                var status = await _quests.ClaimAsync(questId, _lifetime.Token);
                if (_disposed)
                {
                    return;
                }

                _claimInFlight = false;
                if (status == QuestOperationStatus.Success)
                {
                    SetRewardPanelOpen(false);
                    Notify("已领取：" + string.Join("、", DescribeClaim(_quests.Current.LastClaim)));
                }
                else
                {
                    if (status == QuestOperationStatus.AlreadyClaimed)
                    {
                        SetRewardPanelOpen(false);
                    }

                    Notify(QuestOperationMessages.Describe(status));
                }
            }
            catch (OperationCanceledException)
            {
                // 结果未知：服务端日志会在下一次同步时告诉玩家是否已领取。
            }
        }

        /// <summary>领取面板需要鼠标点击：打开期间锁定角色输入，避免点按钮时同时出刀或移动。</summary>
        private void SetRewardPanelOpen(bool open)
        {
            if (_rewardPanelOpen == open)
            {
                return;
            }

            _rewardPanelOpen = open;
            if (open)
            {
                _inputLock.Acquire(this);
            }
            else
            {
                _inputLock.Release(this);
            }
        }

        private IReadOnlyList<string> DescribeConfiguredRewards(string questId)
        {
            var content = _quests.Content;
            if (content == null)
            {
                return Array.Empty<string>();
            }

            var lines = new List<string>();
            foreach (var reward in content.GetRewards(questId))
            {
                lines.Add(DescribeReward(reward.RewardKind, reward.RewardKind == ConfigQuestRewardKind.Item
                    ? reward.ItemId
                    : reward.CurrencyId, reward.Amount));
            }

            return lines;
        }

        private IReadOnlyList<string> DescribeClaim(QuestClaimSummary claim)
        {
            if (claim == null)
            {
                return Array.Empty<string>();
            }

            var lines = new List<string>(claim.Rewards.Count);
            foreach (var reward in claim.Rewards)
            {
                lines.Add(DescribeReward(reward.Kind, reward.RewardId, reward.Amount));
            }

            return lines;
        }

        private string DescribeReward(string kind, string rewardId, long amount)
        {
            var count = amount.ToString(CultureInfo.InvariantCulture);
            var catalog = _config.IsLoaded ? _config.Catalog : null;
            switch (kind)
            {
                case ConfigQuestRewardKind.AccountXp:
                    return "账号经验 " + count;
                case ConfigQuestRewardKind.Item:
                    return (catalog != null && catalog.TryGetItem(rewardId, out var item) ? item.DisplayName : rewardId) +
                           " ×" + count;
                case ConfigQuestRewardKind.Currency:
                    return (catalog != null && catalog.TryGetCurrency(rewardId, out var currency)
                               ? currency.DisplayName
                               : rewardId) + " ×" + count;
                default:
                    return rewardId + " ×" + count;
            }
        }

        private void Notify(string message)
        {
            if (_disposed || string.IsNullOrEmpty(message))
            {
                return;
            }

            _notice = message;
            _noticeVersion++;
            Publish();
        }

        private void Publish()
        {
            if (_disposed)
            {
                return;
            }

            var content = _quests.Content;
            _state.Set(new NpcInteractionPresentationState(
                _focusedNpcId,
                content == null ? string.Empty : content.NpcName(_focusedNpcId),
                CanStartConversation(),
                _rewardPanelOpen,
                _rewardQuestId,
                content != null && content.TryGetQuest(_rewardQuestId, out var quest) ? quest.DisplayName : string.Empty,
                _rewardLines,
                _claimInFlight,
                _notice,
                _noticeVersion));
        }
    }
}
