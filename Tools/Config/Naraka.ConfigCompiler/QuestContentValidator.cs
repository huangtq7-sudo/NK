using System.Globalization;
using Naraka.Config;
using Naraka.ConfigCompiler.Csv;

namespace Naraka.ConfigCompiler;

/// <summary>
/// NPC、任务、目标、任务奖励与对话的跨表校验。服务端的接取、推进、提交与领取完全信任生成配置，
/// 因此引用断裂、依赖成环、行序断档或奖励非法都必须在这里失败，而不是在玩家对话时才暴露。
/// </summary>
public static class QuestContentValidator
{
    /// <summary>一行对白的最大字符数（按文本元素计）。底部对话框只容纳有限长度。</summary>
    public const int MaxDialogueLineLength = 200;

    private const string NpcFile = "npcs.csv";
    private const string QuestFile = "quests.csv";
    private const string ObjectiveFile = "quest_objectives.csv";
    private const string RewardFile = "quest_rewards.csv";
    private const string DialogueFile = "dialogues.csv";
    private const string LineFile = "dialogue_lines.csv";

    public static void Validate(
        NarakaConfigCatalog catalog,
        ISet<string> currencies,
        ISet<string> items,
        DiagnosticBag diagnostics)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(currencies);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var npcs = UniqueIndex(catalog.Npcs, value => value.NpcId, NpcFile, "NpcId", diagnostics);
        var quests = UniqueIndex(catalog.Quests, value => value.QuestId, QuestFile, "QuestId", diagnostics);
        var dialogues = UniqueIndex(
            catalog.Dialogues, value => value.DialogueId, DialogueFile, "DialogueId", diagnostics);
        UniqueIndex(
            catalog.QuestObjectives, value => value.ObjectiveId, ObjectiveFile, "ObjectiveId", diagnostics);

        ValidateDialogues(catalog, npcs, quests, dialogues, diagnostics);
        ValidateNpcs(catalog, dialogues, diagnostics);
        ValidateQuests(catalog, npcs, quests, dialogues, diagnostics);
        ValidateObjectives(catalog, npcs, quests, dialogues, diagnostics);
        ValidateRewards(catalog, quests, currencies, items, diagnostics);
        ValidateDialogueReferences(catalog, dialogues, diagnostics);
    }

    private static void ValidateNpcs(
        NarakaConfigCatalog catalog,
        IReadOnlyDictionary<string, DialogueConfig> dialogues,
        DiagnosticBag diagnostics)
    {
        foreach (var npc in catalog.Npcs)
        {
            var subject = $"NPC '{npc.NpcId}'";
            if (!ConfigNpcMap.IsDefined(npc.MapId))
            {
                diagnostics.Add(
                    NpcFile, 0, $"{subject} 的 MapId '{npc.MapId}' 非法，NPC 只能放在 {string.Join('/', ConfigNpcMap.All)}。");
            }

            RequireDialogue(
                NpcFile, $"{subject} 的 IdleDialogueId", npc.IdleDialogueId, ConfigDialoguePurpose.Idle,
                npc.NpcId, string.Empty, dialogues, diagnostics);
        }

        RequireDistinctSortOrder(catalog.Npcs, value => value.SortOrder, value => value.NpcId, NpcFile, diagnostics);
    }

    private static void ValidateDialogues(
        NarakaConfigCatalog catalog,
        IReadOnlyDictionary<string, NpcConfig> npcs,
        IReadOnlyDictionary<string, QuestConfig> quests,
        IReadOnlyDictionary<string, DialogueConfig> dialogues,
        DiagnosticBag diagnostics)
    {
        foreach (var dialogue in catalog.Dialogues)
        {
            var subject = $"对话 '{dialogue.DialogueId}'";
            if (!npcs.ContainsKey(dialogue.NpcId))
            {
                diagnostics.Add(DialogueFile, 0, $"{subject} 的 NpcId '{dialogue.NpcId}' 不存在。");
            }

            if (!ConfigDialoguePurpose.IsDefined(dialogue.Purpose))
            {
                diagnostics.Add(
                    DialogueFile, 0,
                    $"{subject} 的 Purpose '{dialogue.Purpose}' 非法，只允许 {string.Join('/', ConfigDialoguePurpose.All)}。");
                continue;
            }

            var isIdle = dialogue.Purpose == ConfigDialoguePurpose.Idle;
            if (isIdle && dialogue.QuestId.Length != 0)
            {
                diagnostics.Add(DialogueFile, 0, $"{subject} 是日常对话，不能关联任务。");
            }

            if (!isIdle && !quests.ContainsKey(dialogue.QuestId))
            {
                diagnostics.Add(DialogueFile, 0, $"{subject} 的 QuestId '{dialogue.QuestId}' 不存在。");
            }
        }

        var linesByDialogue = catalog.DialogueLines
            .GroupBy(line => line.DialogueId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        foreach (var line in catalog.DialogueLines)
        {
            var subject = $"对话 '{line.DialogueId}' 第 {line.LineIndex} 行";
            if (!dialogues.ContainsKey(line.DialogueId))
            {
                diagnostics.Add(LineFile, 0, $"{subject} 引用的 DialogueId 不存在。");
            }

            if (!ConfigDialogueSpeaker.IsDefined(line.Speaker))
            {
                diagnostics.Add(
                    LineFile, 0,
                    $"{subject} 的 Speaker '{line.Speaker}' 非法，只允许 {string.Join('/', ConfigDialogueSpeaker.All)}。");
            }

            ValidateLineText(subject, line.Text, diagnostics);
        }

        foreach (var dialogue in catalog.Dialogues)
        {
            if (!linesByDialogue.TryGetValue(dialogue.DialogueId, out var lines) || lines.Length == 0)
            {
                diagnostics.Add(LineFile, 0, $"对话 '{dialogue.DialogueId}' 没有任何对白。");
                continue;
            }

            var indexes = lines.Select(line => line.LineIndex).OrderBy(index => index).ToArray();
            if (!indexes.SequenceEqual(Enumerable.Range(1, indexes.Length)))
            {
                diagnostics.Add(
                    LineFile, 0,
                    $"对话 '{dialogue.DialogueId}' 的 LineIndex 必须从 1 开始连续且不重复，实际为 [{string.Join(',', indexes)}]。");
            }
        }
    }

    private static void ValidateLineText(string subject, string text, DiagnosticBag diagnostics)
    {
        if (text.Length == 0)
        {
            return;
        }

        if (text.Any(character => char.IsControl(character)))
        {
            diagnostics.Add(LineFile, 0, $"{subject} 含有控制字符（换行、制表符等），底部对话框无法正确显示。");
        }

        // 对话框使用富文本做逐字占位，尖括号会被当成标签解析；中文书名号请用《》。
        if (text.IndexOf('<') >= 0 || text.IndexOf('>') >= 0)
        {
            diagnostics.Add(LineFile, 0, $"{subject} 含有尖括号，会被对话框当作富文本标签。");
        }

        if (text.Any(character => char.GetUnicodeCategory(character) == UnicodeCategory.Surrogate) &&
            !IsWellFormedUtf16(text))
        {
            diagnostics.Add(LineFile, 0, $"{subject} 含有不成对的代理字符。");
        }

        var length = new StringInfo(text).LengthInTextElements;
        if (length > MaxDialogueLineLength)
        {
            diagnostics.Add(
                LineFile, 0, $"{subject} 长度为 {length}，超过上限 {MaxDialogueLineLength}。");
        }
    }

    private static bool IsWellFormedUtf16(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]))
            {
                if (i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1]))
                {
                    return false;
                }

                i++;
            }
            else if (char.IsLowSurrogate(text[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static void ValidateQuests(
        NarakaConfigCatalog catalog,
        IReadOnlyDictionary<string, NpcConfig> npcs,
        IReadOnlyDictionary<string, QuestConfig> quests,
        IReadOnlyDictionary<string, DialogueConfig> dialogues,
        DiagnosticBag diagnostics)
    {
        var maximumLevel = catalog.AccountLevelRewards.Length == 0
            ? 1
            : catalog.AccountLevelRewards.Max(value => value.Level);

        foreach (var quest in catalog.Quests)
        {
            var subject = $"任务 '{quest.QuestId}'";
            if (!ConfigQuestCategory.IsDefined(quest.Category))
            {
                diagnostics.Add(
                    QuestFile, 0,
                    $"{subject} 的 Category '{quest.Category}' 非法，只允许 {string.Join('/', ConfigQuestCategory.All)}。");
            }

            if (!ConfigQuestRepeatRule.IsDefined(quest.RepeatRule))
            {
                diagnostics.Add(
                    QuestFile, 0,
                    $"{subject} 的 RepeatRule '{quest.RepeatRule}' 非法，只允许 {string.Join('/', ConfigQuestRepeatRule.All)}。");
            }
            else if (Array.IndexOf(ConfigQuestRepeatRule.Implemented, quest.RepeatRule) < 0)
            {
                diagnostics.Add(
                    QuestFile, 0,
                    $"{subject} 的 RepeatRule '{quest.RepeatRule}' 尚未实现，重复悬赏的周期实例落地前不能使用。");
            }

            if (!npcs.ContainsKey(quest.GiverNpcId))
            {
                diagnostics.Add(QuestFile, 0, $"{subject} 的 GiverNpcId '{quest.GiverNpcId}' 不存在。");
            }

            if (!npcs.ContainsKey(quest.SubmitNpcId))
            {
                diagnostics.Add(QuestFile, 0, $"{subject} 的 SubmitNpcId '{quest.SubmitNpcId}' 不存在。");
            }

            RequireDialogue(QuestFile, $"{subject} 的 OfferDialogueId", quest.OfferDialogueId,
                ConfigDialoguePurpose.Offer, quest.GiverNpcId, quest.QuestId, dialogues, diagnostics);
            RequireDialogue(QuestFile, $"{subject} 的 SubmitDialogueId", quest.SubmitDialogueId,
                ConfigDialoguePurpose.Submit, quest.SubmitNpcId, quest.QuestId, dialogues, diagnostics);

            if (quest.InProgressDialogueId.Length != 0)
            {
                RequireDialogue(QuestFile, $"{subject} 的 InProgressDialogueId", quest.InProgressDialogueId,
                    ConfigDialoguePurpose.InProgress, quest.GiverNpcId, quest.QuestId, dialogues, diagnostics);
            }

            if (quest.RewardPendingDialogueId.Length != 0)
            {
                RequireDialogue(QuestFile, $"{subject} 的 RewardPendingDialogueId", quest.RewardPendingDialogueId,
                    ConfigDialoguePurpose.RewardPending, quest.SubmitNpcId, quest.QuestId, dialogues, diagnostics);
            }

            if (quest.PrerequisiteQuestId.Length != 0)
            {
                if (string.Equals(quest.PrerequisiteQuestId, quest.QuestId, StringComparison.Ordinal))
                {
                    diagnostics.Add(QuestFile, 0, $"{subject} 不能把自己作为前置任务。");
                }
                else if (!quests.ContainsKey(quest.PrerequisiteQuestId))
                {
                    diagnostics.Add(
                        QuestFile, 0, $"{subject} 的 PrerequisiteQuestId '{quest.PrerequisiteQuestId}' 不存在。");
                }
            }

            if (quest.MinAccountLevel < 1 || quest.MinAccountLevel > maximumLevel)
            {
                diagnostics.Add(
                    QuestFile, 0, $"{subject} 的 MinAccountLevel 必须在 1 到 {maximumLevel} 之间。");
            }

            if (quest.SubmitMinSeconds < 0f || float.IsNaN(quest.SubmitMinSeconds))
            {
                diagnostics.Add(QuestFile, 0, $"{subject} 的 SubmitMinSeconds 不能为负。");
            }

            if (quest.Chapter < 0)
            {
                diagnostics.Add(QuestFile, 0, $"{subject} 的 Chapter 不能为负。");
            }
        }

        RequireDistinctSortOrder(
            catalog.Quests, value => value.SortOrder, value => value.QuestId, QuestFile, diagnostics);
        RequireAcyclicPrerequisites(catalog, quests, diagnostics);
    }

    /// <summary>
    /// 前置任务必须是一片森林。成环会让环上的任务永远无法接取，而服务端不会主动报这种"永远不可用"。
    /// </summary>
    private static void RequireAcyclicPrerequisites(
        NarakaConfigCatalog catalog,
        IReadOnlyDictionary<string, QuestConfig> quests,
        DiagnosticBag diagnostics)
    {
        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var quest in catalog.Quests.OrderBy(value => value.QuestId, StringComparer.Ordinal))
        {
            var visited = new List<string>();
            QuestConfig? current = quest;
            while (current is not null && current.PrerequisiteQuestId.Length != 0)
            {
                if (visited.Contains(current.QuestId, StringComparer.Ordinal))
                {
                    var cycle = string.Join("→", visited.SkipWhile(id => id != current.QuestId).Append(current.QuestId));
                    if (reported.Add(string.Join(',', visited.OrderBy(id => id, StringComparer.Ordinal))))
                    {
                        diagnostics.Add(QuestFile, 0, $"前置任务成环：{cycle}。");
                    }

                    break;
                }

                visited.Add(current.QuestId);
                quests.TryGetValue(current.PrerequisiteQuestId, out current);
            }
        }
    }

    private static void ValidateObjectives(
        NarakaConfigCatalog catalog,
        IReadOnlyDictionary<string, NpcConfig> npcs,
        IReadOnlyDictionary<string, QuestConfig> quests,
        IReadOnlyDictionary<string, DialogueConfig> dialogues,
        DiagnosticBag diagnostics)
    {
        foreach (var objective in catalog.QuestObjectives)
        {
            var subject = $"目标 '{objective.ObjectiveId}'";
            if (!quests.ContainsKey(objective.QuestId))
            {
                diagnostics.Add(ObjectiveFile, 0, $"{subject} 的 QuestId '{objective.QuestId}' 不存在。");
            }

            if (!ConfigQuestObjectiveType.IsDefined(objective.ObjectiveType))
            {
                diagnostics.Add(
                    ObjectiveFile, 0,
                    $"{subject} 的 ObjectiveType '{objective.ObjectiveType}' 非法，只允许 {string.Join('/', ConfigQuestObjectiveType.All)}。");
                continue;
            }

            if (objective.IsActive && !ConfigQuestObjectiveType.IsImplemented(objective.ObjectiveType))
            {
                diagnostics.Add(
                    ObjectiveFile, 0,
                    $"{subject} 的类型 '{objective.ObjectiveType}' 还没有可信的服务端校验路径，不能启用。");
            }

            if (objective.TargetCount <= 0)
            {
                diagnostics.Add(ObjectiveFile, 0, $"{subject} 的 TargetCount 必须大于 0。");
            }

            if (objective.MinCompletionSeconds < 0f || float.IsNaN(objective.MinCompletionSeconds))
            {
                diagnostics.Add(ObjectiveFile, 0, $"{subject} 的 MinCompletionSeconds 不能为负。");
            }

            if (objective.ObjectiveType == ConfigQuestObjectiveType.Talk)
            {
                if (!npcs.ContainsKey(objective.TargetNpcId))
                {
                    diagnostics.Add(
                        ObjectiveFile, 0, $"{subject} 的 TargetNpcId '{objective.TargetNpcId}' 不存在。");
                }

                // 一段对话只能读完一次，对话目标的计数因此恒为 1。
                if (objective.TargetCount != 1)
                {
                    diagnostics.Add(ObjectiveFile, 0, $"{subject} 是对话目标，TargetCount 必须为 1。");
                }

                RequireDialogue(ObjectiveFile, $"{subject} 的 TargetDialogueId", objective.TargetDialogueId,
                    ConfigDialoguePurpose.Objective, objective.TargetNpcId, objective.QuestId, dialogues, diagnostics);
            }
        }

        var objectivesByQuest = catalog.QuestObjectives
            .GroupBy(objective => objective.QuestId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        foreach (var quest in catalog.Quests)
        {
            if (!objectivesByQuest.TryGetValue(quest.QuestId, out var objectives) || objectives.Length == 0)
            {
                diagnostics.Add(ObjectiveFile, 0, $"任务 '{quest.QuestId}' 没有任何目标。");
                continue;
            }

            var orders = objectives.Select(objective => objective.Order).OrderBy(order => order).ToArray();
            if (!orders.SequenceEqual(Enumerable.Range(1, orders.Length)))
            {
                diagnostics.Add(
                    ObjectiveFile, 0,
                    $"任务 '{quest.QuestId}' 的目标 Order 必须从 1 开始连续且不重复，实际为 [{string.Join(',', orders)}]。");
            }

            // 启用的任务带着未启用的目标，玩家接了就永远交不掉。
            if (quest.IsActive && objectives.Any(objective => !objective.IsActive))
            {
                diagnostics.Add(
                    ObjectiveFile, 0, $"任务 '{quest.QuestId}' 已启用，但仍有未启用的目标，接取后将永远无法完成。");
            }
        }
    }

    private static void ValidateRewards(
        NarakaConfigCatalog catalog,
        IReadOnlyDictionary<string, QuestConfig> quests,
        ISet<string> currencies,
        ISet<string> items,
        DiagnosticBag diagnostics)
    {
        // 物品 Id 重复已由物品表校验报告；这里取第一条，避免重复 Id 让奖励校验本身抛异常。
        var stackLimits = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in catalog.Items)
        {
            stackLimits.TryAdd(item.ItemId, item.StackLimit);
        }

        var seen = new HashSet<(string, int)>();
        foreach (var reward in catalog.QuestRewards)
        {
            var subject = $"任务 '{reward.QuestId}' 的奖励 {reward.RewardIndex}";
            if (!quests.ContainsKey(reward.QuestId))
            {
                diagnostics.Add(RewardFile, 0, $"{subject} 引用的 QuestId 不存在。");
            }

            if (!seen.Add((reward.QuestId, reward.RewardIndex)))
            {
                diagnostics.Add(RewardFile, 0, $"{subject} 的 RewardIndex 重复。");
            }

            if (reward.Amount <= 0)
            {
                diagnostics.Add(RewardFile, 0, $"{subject} 的 Amount 必须大于 0。");
            }

            switch (reward.RewardKind)
            {
                case ConfigQuestRewardKind.AccountXp:
                    if (reward.ItemId.Length != 0 || reward.CurrencyId.Length != 0)
                    {
                        diagnostics.Add(RewardFile, 0, $"{subject} 是账号经验，不能同时填写物品或货币。");
                    }

                    break;

                case ConfigQuestRewardKind.Item:
                    if (!items.Contains(reward.ItemId))
                    {
                        diagnostics.Add(RewardFile, 0, $"{subject} 的 ItemId '{reward.ItemId}' 不存在。");
                    }
                    else if (reward.Amount > stackLimits[reward.ItemId])
                    {
                        diagnostics.Add(RewardFile, 0, $"{subject} 的数量超过物品堆叠上限。");
                    }

                    if (reward.CurrencyId.Length != 0)
                    {
                        diagnostics.Add(RewardFile, 0, $"{subject} 是物品奖励，不能同时填写货币。");
                    }

                    break;

                case ConfigQuestRewardKind.Currency:
                    if (!currencies.Contains(reward.CurrencyId))
                    {
                        diagnostics.Add(RewardFile, 0, $"{subject} 的 CurrencyId '{reward.CurrencyId}' 不存在。");
                    }

                    if (reward.ItemId.Length != 0)
                    {
                        diagnostics.Add(RewardFile, 0, $"{subject} 是货币奖励，不能同时填写物品。");
                    }

                    break;

                default:
                    diagnostics.Add(
                        RewardFile, 0,
                        $"{subject} 的 RewardKind '{reward.RewardKind}' 非法，只允许 {string.Join('/', ConfigQuestRewardKind.All)}。");
                    break;
            }
        }

        foreach (var group in catalog.QuestRewards
                     .Where(reward => reward.RewardKind == ConfigQuestRewardKind.AccountXp)
                     .GroupBy(reward => reward.QuestId, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
        {
            diagnostics.Add(RewardFile, 0, $"任务 '{group.Key}' 有多条账号经验奖励，只允许一条。");
        }

        // 账号经验只来自任务，且等级上限为 30：全部一次性任务的经验之和不能超过满级所需。
        if (catalog.AccountLevelRewards.Length != 0)
        {
            var maximumXp = catalog.AccountLevelRewards.Max(value => value.XpToReach);
            var totalXp = catalog.QuestRewards
                .Where(reward => reward.RewardKind == ConfigQuestRewardKind.AccountXp)
                .Where(reward => quests.TryGetValue(reward.QuestId, out var quest) &&
                                 quest.RepeatRule == ConfigQuestRepeatRule.None)
                .Sum(reward => reward.Amount);
            if (totalXp > maximumXp)
            {
                diagnostics.Add(
                    RewardFile, 0,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "全部一次性任务的账号经验合计 {0}，超过满级所需 {1}。",
                        totalXp,
                        maximumXp));
            }
        }
    }

    /// <summary>
    /// 任务对话必须被它声明的任务真正引用。孤立的任务对话通常是 Id 拼写错误，
    /// 留着它会让维护者以为某段文本已经接入了任务流程。
    /// </summary>
    private static void ValidateDialogueReferences(
        NarakaConfigCatalog catalog,
        IReadOnlyDictionary<string, DialogueConfig> dialogues,
        DiagnosticBag diagnostics)
    {
        var referenced = new HashSet<string>(StringComparer.Ordinal);
        foreach (var quest in catalog.Quests)
        {
            referenced.Add(quest.OfferDialogueId);
            referenced.Add(quest.SubmitDialogueId);
            referenced.Add(quest.InProgressDialogueId);
            referenced.Add(quest.RewardPendingDialogueId);
        }

        foreach (var objective in catalog.QuestObjectives)
        {
            referenced.Add(objective.TargetDialogueId);
        }

        foreach (var npc in catalog.Npcs)
        {
            referenced.Add(npc.IdleDialogueId);
        }

        foreach (var dialogue in dialogues.Values.OrderBy(value => value.DialogueId, StringComparer.Ordinal))
        {
            if (!referenced.Contains(dialogue.DialogueId))
            {
                diagnostics.Add(DialogueFile, 0, $"对话 '{dialogue.DialogueId}' 没有被任何任务、目标或 NPC 引用。");
            }
        }
    }

    private static void RequireDialogue(
        string file,
        string subject,
        string dialogueId,
        string purpose,
        string npcId,
        string questId,
        IReadOnlyDictionary<string, DialogueConfig> dialogues,
        DiagnosticBag diagnostics)
    {
        if (!dialogues.TryGetValue(dialogueId, out var dialogue))
        {
            diagnostics.Add(file, 0, $"{subject} '{dialogueId}' 不存在。");
            return;
        }

        if (dialogue.Purpose != purpose)
        {
            diagnostics.Add(file, 0, $"{subject} '{dialogueId}' 的用途是 {dialogue.Purpose}，这里需要 {purpose}。");
        }

        if (!string.Equals(dialogue.NpcId, npcId, StringComparison.Ordinal))
        {
            diagnostics.Add(file, 0, $"{subject} '{dialogueId}' 属于 NPC '{dialogue.NpcId}'，这里需要 '{npcId}'。");
        }

        if (!string.Equals(dialogue.QuestId, questId, StringComparison.Ordinal))
        {
            diagnostics.Add(file, 0, $"{subject} '{dialogueId}' 关联任务 '{dialogue.QuestId}'，这里需要 '{questId}'。");
        }
    }

    private static Dictionary<string, T> UniqueIndex<T>(
        IReadOnlyCollection<T> rows,
        Func<T, string> selector,
        string file,
        string column,
        DiagnosticBag diagnostics)
    {
        var index = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var id = selector(row);
            if (id.Length == 0)
            {
                continue;
            }

            if (!index.TryAdd(id, row))
            {
                diagnostics.Add(file, 0, $"{column} 重复：'{id}'。");
            }
        }

        return index;
    }

    private static void RequireDistinctSortOrder<T>(
        IReadOnlyCollection<T> rows,
        Func<T, int> sortOrder,
        Func<T, string> id,
        string file,
        DiagnosticBag diagnostics)
    {
        foreach (var duplicate in rows.GroupBy(sortOrder).Where(group => group.Count() > 1))
        {
            diagnostics.Add(
                file,
                0,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "SortOrder {0} 被 [{1}] 共用，显示顺序不确定。",
                    duplicate.Key,
                    string.Join(',', duplicate.Select(id).OrderBy(value => value, StringComparer.Ordinal))));
        }
    }
}
