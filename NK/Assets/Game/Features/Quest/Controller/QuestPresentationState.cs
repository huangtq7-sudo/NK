using System;
using System.Collections.Generic;
using Naraka.Config;
using Naraka.Core.Application.MVC;
using Naraka.Features.Quest.Model;

namespace Naraka.Features.Quest.Controller
{
    /// <summary>NPC 头顶标记。只是展示，不决定能否交互。</summary>
    public enum QuestNpcMarker
    {
        None = 0,

        /// <summary>有可接取的任务。</summary>
        Available = 1,

        /// <summary>该 NPC 是当前对话目标。</summary>
        Objective = 2,

        /// <summary>可以向该 NPC 复命。</summary>
        ReadyToSubmit = 3,

        /// <summary>奖励待领取。</summary>
        RewardPending = 4
    }

    /// <summary>
    /// 任务追踪与 NPC 标记的只读状态。文字由配置推导，状态只来自服务端日志。
    /// </summary>
    public readonly struct QuestPresentationState : IPresentationState
    {
        private static readonly IReadOnlyDictionary<string, QuestNpcMarker> NoMarkers =
            new Dictionary<string, QuestNpcMarker>(StringComparer.Ordinal);

        private QuestPresentationState(
            bool isBusy,
            bool isSupported,
            bool hasJournal,
            string trackedQuestId,
            string trackedTitle,
            string trackedObjectiveText,
            QuestProgressStatus trackedStatus,
            bool canClaimTracked,
            long accountXp,
            int accountLevel,
            QuestClaimSummary lastClaim,
            QuestOperationStatus lastStatus,
            IReadOnlyDictionary<string, QuestNpcMarker> npcMarkers)
        {
            IsBusy = isBusy;
            IsSupported = isSupported;
            HasJournal = hasJournal;
            TrackedQuestId = trackedQuestId ?? string.Empty;
            TrackedTitle = trackedTitle ?? string.Empty;
            TrackedObjectiveText = trackedObjectiveText ?? string.Empty;
            TrackedStatus = trackedStatus;
            CanClaimTracked = canClaimTracked;
            AccountXp = accountXp;
            AccountLevel = accountLevel;
            LastClaim = lastClaim;
            LastStatus = lastStatus;
            StatusMessage = QuestOperationMessages.Describe(lastStatus);
            NpcMarkers = npcMarkers ?? NoMarkers;
        }

        public bool IsBusy { get; }

        public bool IsSupported { get; }

        public bool HasJournal { get; }

        public bool HasTrackedQuest => TrackedQuestId.Length != 0;

        public string TrackedQuestId { get; }

        public string TrackedTitle { get; }

        public string TrackedObjectiveText { get; }

        public QuestProgressStatus TrackedStatus { get; }

        public bool CanClaimTracked { get; }

        public long AccountXp { get; }

        public int AccountLevel { get; }

        public QuestClaimSummary LastClaim { get; }

        public QuestOperationStatus LastStatus { get; }

        public string StatusMessage { get; }

        public IReadOnlyDictionary<string, QuestNpcMarker> NpcMarkers { get; }

        public QuestNpcMarker MarkerFor(string npcId) =>
            npcId != null && NpcMarkers.TryGetValue(npcId, out var marker) ? marker : QuestNpcMarker.None;

        public static QuestPresentationState Initial => new QuestPresentationState(
            false, false, false, string.Empty, string.Empty, string.Empty, QuestProgressStatus.Locked,
            false, 0, 1, null, QuestOperationStatus.Success, NoMarkers);

        public static QuestPresentationState Build(
            bool isBusy,
            bool isSupported,
            QuestJournalSnapshot journal,
            QuestContentCatalog content,
            QuestClaimSummary lastClaim,
            QuestOperationStatus lastStatus)
        {
            if (journal == null || content == null)
            {
                return new QuestPresentationState(
                    isBusy, isSupported, false, string.Empty, string.Empty, string.Empty, QuestProgressStatus.Locked,
                    false, 0, 1, lastClaim, lastStatus, NoMarkers);
            }

            var tracked = SelectTracked(journal, content, out var trackedStatus);
            var title = string.Empty;
            var objectiveText = string.Empty;
            if (tracked != null)
            {
                title = tracked.DisplayName;
                objectiveText = DescribeStep(tracked, journal.Find(tracked.QuestId), trackedStatus, content);
            }

            return new QuestPresentationState(
                isBusy,
                isSupported,
                true,
                tracked?.QuestId,
                title,
                objectiveText,
                trackedStatus,
                trackedStatus == QuestProgressStatus.RewardPending,
                journal.AccountXp,
                journal.AccountLevel,
                lastClaim,
                lastStatus,
                BuildMarkers(journal, content));
        }

        /// <summary>
        /// 追踪栏显示的任务：进行中的主线优先，其次其他进行中的任务，再次可接取的主线。
        /// 主线同时只追踪 1 条，由服务端上限保证。
        /// </summary>
        private static QuestConfig SelectTracked(
            QuestJournalSnapshot journal,
            QuestContentCatalog content,
            out QuestProgressStatus status)
        {
            QuestConfig fallback = null;
            var fallbackStatus = QuestProgressStatus.Locked;
            foreach (var quest in content.QuestsInDisplayOrder)
            {
                var entry = journal.Find(quest.QuestId);
                if (entry != null && entry.IsOpen)
                {
                    if (quest.Category == ConfigQuestCategory.Main)
                    {
                        status = entry.Status;
                        return quest;
                    }

                    if (fallback == null || fallbackStatus == QuestProgressStatus.Available)
                    {
                        fallback = quest;
                        fallbackStatus = entry.Status;
                    }
                }
                else if (entry == null && fallback == null && quest.Category == ConfigQuestCategory.Main &&
                         journal.IsAvailable(quest.QuestId))
                {
                    fallback = quest;
                    fallbackStatus = QuestProgressStatus.Available;
                }
            }

            status = fallbackStatus;
            return fallback;
        }

        private static string DescribeStep(
            QuestConfig quest,
            QuestEntrySnapshot entry,
            QuestProgressStatus status,
            QuestContentCatalog content)
        {
            switch (status)
            {
                case QuestProgressStatus.Available:
                    return "前往" + content.NpcName(quest.GiverNpcId) + "处接取任务";
                case QuestProgressStatus.ReadyToSubmit:
                    return "回到" + content.NpcName(quest.SubmitNpcId) + "处复命";
                case QuestProgressStatus.RewardPending:
                    return "任务完成，领取奖励";
                case QuestProgressStatus.Accepted:
                case QuestProgressStatus.InProgress:
                    foreach (var objective in content.GetObjectives(quest.QuestId))
                    {
                        var progress = entry?.FindObjective(objective.ObjectiveId);
                        if (progress == null || !progress.IsCompleted)
                        {
                            return objective.Description;
                        }
                    }

                    return string.Empty;
                default:
                    return string.Empty;
            }
        }

        private static IReadOnlyDictionary<string, QuestNpcMarker> BuildMarkers(
            QuestJournalSnapshot journal,
            QuestContentCatalog content)
        {
            var markers = new Dictionary<string, QuestNpcMarker>(StringComparer.Ordinal);
            foreach (var quest in content.QuestsInDisplayOrder)
            {
                var entry = journal.Find(quest.QuestId);
                if (entry == null)
                {
                    if (journal.IsAvailable(quest.QuestId))
                    {
                        Raise(markers, quest.GiverNpcId, QuestNpcMarker.Available);
                    }

                    continue;
                }

                switch (entry.Status)
                {
                    case QuestProgressStatus.Accepted:
                    case QuestProgressStatus.InProgress:
                        var target = QuestConversationPlanner.CurrentTalkObjective(quest, entry, content);
                        if (target != null)
                        {
                            Raise(markers, target.TargetNpcId, QuestNpcMarker.Objective);
                        }

                        break;
                    case QuestProgressStatus.ReadyToSubmit:
                        Raise(markers, quest.SubmitNpcId, QuestNpcMarker.ReadyToSubmit);
                        break;
                    case QuestProgressStatus.RewardPending:
                        Raise(markers, quest.SubmitNpcId, QuestNpcMarker.RewardPending);
                        break;
                }
            }

            return markers;
        }

        private static void Raise(Dictionary<string, QuestNpcMarker> markers, string npcId, QuestNpcMarker marker)
        {
            if (string.IsNullOrEmpty(npcId))
            {
                return;
            }

            if (!markers.TryGetValue(npcId, out var existing) || marker > existing)
            {
                markers[npcId] = marker;
            }
        }
    }

    public static class QuestOperationMessages
    {
        public static string Describe(QuestOperationStatus status)
        {
            switch (status)
            {
                case QuestOperationStatus.Success:
                case QuestOperationStatus.Conflict:
                    return string.Empty;
                case QuestOperationStatus.Unauthenticated:
                    return "登录状态已失效，请重新登录。";
                case QuestOperationStatus.NotAvailable:
                    return "任务状态已变化，请再与对方交谈。";
                case QuestOperationStatus.LimitReached:
                    return "已有进行中的同类任务。";
                case QuestOperationStatus.RateLimited:
                    return "操作太快了，请稍后再试。";
                case QuestOperationStatus.InventoryFull:
                    return "仓库空间不足，整理后再领取奖励。";
                case QuestOperationStatus.AlreadyClaimed:
                    return "奖励已经领取过了。";
                case QuestOperationStatus.DatabaseUnavailable:
                    return "任务数据暂时不可用，请稍后重试。";
                case QuestOperationStatus.TransportFailure:
                    return "无法连接服务器，请检查网络后重试。";
                case QuestOperationStatus.ServerCapabilityMissing:
                    return "服务器功能尚未升级。";
                case QuestOperationStatus.ConfigUnavailable:
                    return "任务配置不可用。";
                default:
                    return "任务操作失败，请稍后重试。";
            }
        }
    }
}
