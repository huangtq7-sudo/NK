using System;
using Naraka.Config;
using Naraka.Features.Quest.Model;

namespace Naraka.Features.Quest.Controller
{
    /// <summary>对话读完后要向服务端发出的意图。客户端只发意图，结果以服务端为准。</summary>
    public enum QuestConversationIntent
    {
        /// <summary>纯对白，不推进任何状态。</summary>
        None = 0,

        Accept = 1,

        CompleteObjective = 2,

        Submit = 3,

        /// <summary>奖励待领取提示，读完后打开领取面板。</summary>
        OpenClaim = 4
    }

    public sealed class QuestConversation
    {
        public QuestConversation(
            string npcId,
            string dialogueId,
            QuestConversationIntent intent,
            string questId,
            string objectiveId)
        {
            NpcId = npcId ?? string.Empty;
            DialogueId = dialogueId ?? string.Empty;
            Intent = intent;
            QuestId = questId ?? string.Empty;
            ObjectiveId = objectiveId ?? string.Empty;
        }

        public string NpcId { get; }

        public string DialogueId { get; }

        public QuestConversationIntent Intent { get; }

        public string QuestId { get; }

        public string ObjectiveId { get; }
    }

    /// <summary>
    /// 按任务状态为 NPC 选择对话。纯函数，规则固定且可测试：
    ///   1. 可向该 NPC 复命 → 提交对话
    ///   2. 该 NPC 是当前对话目标 → 目标对话
    ///   3. 奖励待领取且该 NPC 是提交者 → 待领取提示
    ///   4. 该 NPC 发放可接取的任务 → 接取对话
    ///   5. 该 NPC 发放的任务进行中 → 进行中提示
    ///   6. 其余 → 日常对话
    /// 同一优先级内按任务显示顺序取第一条。任务日志尚未同步时只播日常对话，不发任何意图。
    /// </summary>
    public static class QuestConversationPlanner
    {
        public static QuestConversation Plan(string npcId, QuestJournalSnapshot journal, QuestContentCatalog content)
        {
            if (content == null || !content.TryGetNpc(npcId, out var npc))
            {
                return null;
            }

            var idle = new QuestConversation(npcId, npc.IdleDialogueId, QuestConversationIntent.None, null, null);
            if (journal == null)
            {
                return idle;
            }

            var quests = content.QuestsInDisplayOrder;
            foreach (var quest in quests)
            {
                var entry = journal.Find(quest.QuestId);
                if (entry != null && entry.Status == QuestProgressStatus.ReadyToSubmit && Is(quest.SubmitNpcId, npcId))
                {
                    return new QuestConversation(
                        npcId, quest.SubmitDialogueId, QuestConversationIntent.Submit, quest.QuestId, null);
                }
            }

            foreach (var quest in quests)
            {
                var entry = journal.Find(quest.QuestId);
                if (entry == null || (entry.Status != QuestProgressStatus.Accepted &&
                                      entry.Status != QuestProgressStatus.InProgress))
                {
                    continue;
                }

                var objective = CurrentTalkObjective(quest, entry, content);
                if (objective != null && Is(objective.TargetNpcId, npcId))
                {
                    return new QuestConversation(
                        npcId,
                        objective.TargetDialogueId,
                        QuestConversationIntent.CompleteObjective,
                        quest.QuestId,
                        objective.ObjectiveId);
                }
            }

            foreach (var quest in quests)
            {
                var entry = journal.Find(quest.QuestId);
                if (entry != null && entry.Status == QuestProgressStatus.RewardPending && Is(quest.SubmitNpcId, npcId))
                {
                    var dialogueId = string.IsNullOrEmpty(quest.RewardPendingDialogueId)
                        ? npc.IdleDialogueId
                        : quest.RewardPendingDialogueId;
                    return new QuestConversation(
                        npcId, dialogueId, QuestConversationIntent.OpenClaim, quest.QuestId, null);
                }
            }

            foreach (var quest in quests)
            {
                if (journal.Find(quest.QuestId) == null && journal.IsAvailable(quest.QuestId) &&
                    Is(quest.GiverNpcId, npcId))
                {
                    return new QuestConversation(
                        npcId, quest.OfferDialogueId, QuestConversationIntent.Accept, quest.QuestId, null);
                }
            }

            foreach (var quest in quests)
            {
                var entry = journal.Find(quest.QuestId);
                if (entry != null &&
                    (entry.Status == QuestProgressStatus.Accepted || entry.Status == QuestProgressStatus.InProgress) &&
                    Is(quest.GiverNpcId, npcId) && !string.IsNullOrEmpty(quest.InProgressDialogueId))
                {
                    return new QuestConversation(
                        npcId, quest.InProgressDialogueId, QuestConversationIntent.None, quest.QuestId, null);
                }
            }

            return idle;
        }

        /// <summary>按顺序第一个未完成的目标，且它是已启用的对话目标；否则为 null。</summary>
        public static QuestObjectiveConfig CurrentTalkObjective(
            QuestConfig quest,
            QuestEntrySnapshot entry,
            QuestContentCatalog content)
        {
            foreach (var objective in content.GetObjectives(quest.QuestId))
            {
                var progress = entry.FindObjective(objective.ObjectiveId);
                if (progress != null && progress.IsCompleted)
                {
                    continue;
                }

                return objective.IsActive && objective.ObjectiveType == ConfigQuestObjectiveType.Talk
                    ? objective
                    : null;
            }

            return null;
        }

        private static bool Is(string left, string right) => string.Equals(left, right, StringComparison.Ordinal);
    }
}
