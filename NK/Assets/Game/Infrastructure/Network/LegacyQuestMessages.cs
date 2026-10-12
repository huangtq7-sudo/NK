using System.Collections.Generic;
using ProtoBuf;

namespace Naraka.Infrastructure.Network
{
    // P4.1 任务消息 67/68。字段编号与服务端 LegacyMessages.cs 中同名类型逐一对应，只能追加。
    // 请求没有账号、奖励、数量或经验字段：发放内容只来自服务端配置。

    [ProtoContract]
    internal sealed class LegacyMsgQuestRequest : LegacyMessage
    {
        public LegacyMsgQuestRequest()
        {
            ProtocolType = LegacyProtocolValue.MsgQuestRequest;
        }

        [ProtoMember(1)]
        public override LegacyProtocolValue ProtocolType { get; set; }

        [ProtoMember(2)] public string RequestId { get; set; }
        [ProtoMember(3)] public LegacyQuestOperation Operation { get; set; }
        [ProtoMember(4)] public string QuestId { get; set; }
        [ProtoMember(5)] public string ObjectiveId { get; set; }
        [ProtoMember(6)] public string DialogueId { get; set; }
    }

    [ProtoContract]
    internal sealed class LegacyMsgQuestResponse : LegacyMessage
    {
        public LegacyMsgQuestResponse()
        {
            ProtocolType = LegacyProtocolValue.MsgQuestResponse;
        }

        [ProtoMember(1)]
        public override LegacyProtocolValue ProtocolType { get; set; }

        [ProtoMember(2)] public string RequestId { get; set; }
        [ProtoMember(3)] public LegacyQuestOperation Operation { get; set; }
        [ProtoMember(4)] public LegacyLobbyOperationStatus Status { get; set; }
        [ProtoMember(5)] public bool IsReplay { get; set; }
        [ProtoMember(6)] public LegacyQuestJournal Journal { get; set; }
        [ProtoMember(7)] public LegacyQuestClaim Claim { get; set; }
    }

    [ProtoContract]
    internal sealed class LegacyQuestJournal
    {
        [ProtoMember(1)] public List<LegacyQuestEntry> Quests { get; } = new List<LegacyQuestEntry>();
        [ProtoMember(2)] public List<string> AvailableQuestIds { get; } = new List<string>();
        [ProtoMember(3)] public long AccountXp { get; set; }
        [ProtoMember(4)] public int AccountLevel { get; set; }
    }

    [ProtoContract]
    internal sealed class LegacyQuestEntry
    {
        [ProtoMember(1)] public string QuestId { get; set; }
        [ProtoMember(2)] public LegacyQuestStatus Status { get; set; }
        [ProtoMember(3)] public List<LegacyQuestObjective> Objectives { get; } = new List<LegacyQuestObjective>();
    }

    [ProtoContract]
    internal sealed class LegacyQuestObjective
    {
        [ProtoMember(1)] public string ObjectiveId { get; set; }
        [ProtoMember(2)] public int Progress { get; set; }
        [ProtoMember(3)] public int TargetCount { get; set; }
        [ProtoMember(4)] public bool IsCompleted { get; set; }
    }

    [ProtoContract]
    internal sealed class LegacyQuestClaim
    {
        [ProtoMember(1)] public string QuestId { get; set; }
        [ProtoMember(2)] public List<LegacyQuestReward> Rewards { get; } = new List<LegacyQuestReward>();
        [ProtoMember(3)] public long AccountXpAfter { get; set; }
    }

    [ProtoContract]
    internal sealed class LegacyQuestReward
    {
        [ProtoMember(1)] public string Kind { get; set; }
        [ProtoMember(2)] public string RewardId { get; set; }
        [ProtoMember(3)] public long Amount { get; set; }
    }
}
