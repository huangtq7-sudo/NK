using System;
using System.Collections.Generic;
using Naraka.Core.Domain;

namespace Naraka.Features.Quest.Model
{
    /// <summary>与服务端 QuestStatus 数值一致。Locked/Available 不会出现在已接取条目里。</summary>
    public enum QuestProgressStatus
    {
        Locked = 0,
        Available = 1,
        Accepted = 2,
        InProgress = 3,
        ReadyToSubmit = 4,
        RewardPending = 5,
        Claimed = 6
    }

    public sealed class QuestObjectiveSnapshot
    {
        public QuestObjectiveSnapshot(string objectiveId, int progress, int targetCount, bool isCompleted)
        {
            ObjectiveId = objectiveId ?? string.Empty;
            Progress = progress;
            TargetCount = targetCount;
            IsCompleted = isCompleted;
        }

        public string ObjectiveId { get; }

        public int Progress { get; }

        public int TargetCount { get; }

        public bool IsCompleted { get; }
    }

    public sealed class QuestEntrySnapshot
    {
        public QuestEntrySnapshot(
            string questId,
            QuestProgressStatus status,
            IReadOnlyList<QuestObjectiveSnapshot> objectives)
        {
            QuestId = questId ?? string.Empty;
            Status = status;
            Objectives = objectives ?? Array.Empty<QuestObjectiveSnapshot>();
        }

        public string QuestId { get; }

        public QuestProgressStatus Status { get; }

        public IReadOnlyList<QuestObjectiveSnapshot> Objectives { get; }

        /// <summary>仍占用进行名额的状态，与服务端定义一致。</summary>
        public bool IsOpen =>
            Status == QuestProgressStatus.Accepted ||
            Status == QuestProgressStatus.InProgress ||
            Status == QuestProgressStatus.ReadyToSubmit ||
            Status == QuestProgressStatus.RewardPending;

        public QuestObjectiveSnapshot FindObjective(string objectiveId)
        {
            for (var i = 0; i < Objectives.Count; i++)
            {
                if (string.Equals(Objectives[i].ObjectiveId, objectiveId, StringComparison.Ordinal))
                {
                    return Objectives[i];
                }
            }

            return null;
        }
    }

    /// <summary>
    /// 服务端权威的任务日志快照。客户端从不在本地推进或猜测任务状态：
    /// 所有变化都等服务端响应后整体替换这份快照。
    /// </summary>
    public sealed class QuestJournalSnapshot : IModel
    {
        private QuestJournalSnapshot(
            QuestEntrySnapshot[] entries,
            string[] availableQuestIds,
            long accountXp,
            int accountLevel)
        {
            Entries = entries;
            AvailableQuestIds = availableQuestIds;
            AccountXp = accountXp;
            AccountLevel = accountLevel;
        }

        public IReadOnlyList<QuestEntrySnapshot> Entries { get; }

        public IReadOnlyList<string> AvailableQuestIds { get; }

        public long AccountXp { get; }

        public int AccountLevel { get; }

        public static QuestJournalSnapshot Empty { get; } =
            new QuestJournalSnapshot(Array.Empty<QuestEntrySnapshot>(), Array.Empty<string>(), 0, 1);

        public QuestEntrySnapshot Find(string questId)
        {
            for (var i = 0; i < Entries.Count; i++)
            {
                if (string.Equals(Entries[i].QuestId, questId, StringComparison.Ordinal))
                {
                    return Entries[i];
                }
            }

            return null;
        }

        public bool IsAvailable(string questId)
        {
            for (var i = 0; i < AvailableQuestIds.Count; i++)
            {
                if (string.Equals(AvailableQuestIds[i], questId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>校验服务端响应形状。重复或空 Id、负数经验都说明响应不可信，整份拒绝。</summary>
        public static bool TryCreate(
            IReadOnlyList<QuestEntrySnapshot> entries,
            IReadOnlyList<string> availableQuestIds,
            long accountXp,
            int accountLevel,
            out QuestJournalSnapshot journal)
        {
            journal = null;
            if (entries == null || availableQuestIds == null || accountXp < 0 || accountLevel < 1)
            {
                return false;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var entryCopy = new QuestEntrySnapshot[entries.Count];
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry == null || string.IsNullOrEmpty(entry.QuestId) || !seen.Add(entry.QuestId) ||
                    entry.Status == QuestProgressStatus.Locked || entry.Status == QuestProgressStatus.Available ||
                    !Enum.IsDefined(typeof(QuestProgressStatus), entry.Status))
                {
                    return false;
                }

                entryCopy[i] = entry;
            }

            var available = new string[availableQuestIds.Count];
            for (var i = 0; i < availableQuestIds.Count; i++)
            {
                if (string.IsNullOrEmpty(availableQuestIds[i]) || seen.Contains(availableQuestIds[i]))
                {
                    return false;
                }

                available[i] = availableQuestIds[i];
            }

            journal = new QuestJournalSnapshot(entryCopy, available, accountXp, accountLevel);
            return true;
        }
    }

    public sealed class QuestRewardLine
    {
        public QuestRewardLine(string kind, string rewardId, long amount)
        {
            Kind = kind ?? string.Empty;
            RewardId = rewardId ?? string.Empty;
            Amount = amount;
        }

        /// <summary>AccountXp、Item 或 Currency。</summary>
        public string Kind { get; }

        public string RewardId { get; }

        public long Amount { get; }
    }

    /// <summary>服务端确认的一次领取。客户端只展示它，不据此改写任何资产。</summary>
    public sealed class QuestClaimSummary
    {
        public QuestClaimSummary(string questId, IReadOnlyList<QuestRewardLine> rewards, long accountXpAfter)
        {
            QuestId = questId ?? string.Empty;
            Rewards = rewards ?? Array.Empty<QuestRewardLine>();
            AccountXpAfter = accountXpAfter;
        }

        public string QuestId { get; }

        public IReadOnlyList<QuestRewardLine> Rewards { get; }

        public long AccountXpAfter { get; }
    }

    /// <summary>跨场景保留的任务日志。只能由服务端响应写入。</summary>
    public sealed class QuestModel : IModel
    {
        public QuestJournalSnapshot Journal { get; private set; }

        public QuestClaimSummary LastClaim { get; private set; }

        public bool HasJournal => Journal != null;

        /// <summary>每次服务端快照替换后递增，用来丢弃针对旧快照发起的过期对话。</summary>
        public int Version { get; private set; }

        public void ApplyJournal(QuestJournalSnapshot journal)
        {
            Journal = journal ?? throw new ArgumentNullException(nameof(journal));
            Version++;
        }

        public void ApplyClaim(QuestClaimSummary claim)
        {
            LastClaim = claim ?? throw new ArgumentNullException(nameof(claim));
        }

        public void Clear()
        {
            Journal = null;
            LastClaim = null;
            Version++;
        }
    }
}
