using System;
using System.Collections.Generic;
using Naraka.Config;

namespace Naraka.Features.Quest.Controller
{
    /// <summary>
    /// 客户端的任务、NPC 与对话只读索引。只用于展示和选择要播放的对话；
    /// 能否接取、是否完成、奖励多少一律以服务端响应为准。
    /// </summary>
    public sealed class QuestContentCatalog
    {
        private readonly Dictionary<string, NpcConfig> _npcs;
        private readonly Dictionary<string, QuestConfig> _quests;
        private readonly Dictionary<string, DialogueConfig> _dialogues;
        private readonly Dictionary<string, DialogueLineConfig[]> _lines;
        private readonly Dictionary<string, QuestObjectiveConfig[]> _objectives;
        private readonly Dictionary<string, QuestRewardConfig[]> _rewards;

        public QuestContentCatalog(NarakaConfigCatalog catalog)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            _npcs = Index(catalog.Npcs, value => value.NpcId);
            _quests = Index(catalog.Quests, value => value.QuestId);
            _dialogues = Index(catalog.Dialogues, value => value.DialogueId);
            _lines = Group(catalog.DialogueLines, value => value.DialogueId, value => value.LineIndex);
            _objectives = Group(catalog.QuestObjectives, value => value.QuestId, value => value.Order);
            _rewards = Group(catalog.QuestRewards, value => value.QuestId, value => value.RewardIndex);

            var quests = new List<QuestConfig>(catalog.Quests);
            quests.Sort((left, right) => left.SortOrder.CompareTo(right.SortOrder));
            QuestsInDisplayOrder = quests.ToArray();
        }

        public IReadOnlyList<QuestConfig> QuestsInDisplayOrder { get; }

        public bool TryGetNpc(string npcId, out NpcConfig npc) =>
            _npcs.TryGetValue(npcId ?? string.Empty, out npc);

        public bool TryGetQuest(string questId, out QuestConfig quest) =>
            _quests.TryGetValue(questId ?? string.Empty, out quest);

        public bool TryGetDialogue(string dialogueId, out DialogueConfig dialogue) =>
            _dialogues.TryGetValue(dialogueId ?? string.Empty, out dialogue);

        /// <summary>对白按 LineIndex 排序。</summary>
        public IReadOnlyList<DialogueLineConfig> GetLines(string dialogueId) =>
            _lines.TryGetValue(dialogueId ?? string.Empty, out var lines) ? lines : Array.Empty<DialogueLineConfig>();

        /// <summary>任务目标按 Order 排序。</summary>
        public IReadOnlyList<QuestObjectiveConfig> GetObjectives(string questId) =>
            _objectives.TryGetValue(questId ?? string.Empty, out var objectives)
                ? objectives
                : Array.Empty<QuestObjectiveConfig>();

        /// <summary>配置中的奖励预览。真正发放的数量以服务端领取摘要为准。</summary>
        public IReadOnlyList<QuestRewardConfig> GetRewards(string questId) =>
            _rewards.TryGetValue(questId ?? string.Empty, out var rewards) ? rewards : Array.Empty<QuestRewardConfig>();

        public string NpcName(string npcId) =>
            TryGetNpc(npcId, out var npc) ? npc.DisplayName : string.Empty;

        private static Dictionary<string, T> Index<T>(T[] rows, Func<T, string> key)
        {
            var index = new Dictionary<string, T>(StringComparer.Ordinal);
            if (rows == null)
            {
                return index;
            }

            foreach (var row in rows)
            {
                index[key(row)] = row;
            }

            return index;
        }

        private static Dictionary<string, T[]> Group<T>(T[] rows, Func<T, string> key, Func<T, int> order)
        {
            var groups = new Dictionary<string, List<T>>(StringComparer.Ordinal);
            if (rows != null)
            {
                foreach (var row in rows)
                {
                    if (!groups.TryGetValue(key(row), out var list))
                    {
                        list = new List<T>();
                        groups[key(row)] = list;
                    }

                    list.Add(row);
                }
            }

            var result = new Dictionary<string, T[]>(StringComparer.Ordinal);
            foreach (var pair in groups)
            {
                pair.Value.Sort((left, right) => order(left).CompareTo(order(right)));
                result[pair.Key] = pair.Value.ToArray();
            }

            return result;
        }
    }
}
