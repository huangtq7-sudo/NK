using System.Linq;
using Naraka.Features.Quest.Controller;
using Naraka.Features.Quest.Model;
using Naraka.Infrastructure.Network;
using NUnit.Framework;

namespace Naraka.P0.Tests
{
    /// <summary>P4.1 任务消息 67/68 的客户端线级契约，与服务端 QuestProtocolTests 对应。</summary>
    public sealed class QuestWireTests
    {
        [Test]
        public void QuestProtocolsAreAppendedAfterTheExpeditionPair()
        {
            Assert.That((int)LegacyProtocolValue.MsgExpeditionResponse, Is.EqualTo(66));
            Assert.That((int)LegacyProtocolValue.MsgQuestRequest, Is.EqualTo(67));
            Assert.That((int)LegacyProtocolValue.MsgQuestResponse, Is.EqualTo(68));
        }

        [Test]
        public void QuestRequestCarriesOnlyIntentFields()
        {
            var names = typeof(LegacyMsgQuestRequest).GetProperties().Select(property => property.Name).ToArray();

            Assert.That(names, Is.EquivalentTo(new[]
            {
                "ProtocolType", "RequestId", "Operation", "QuestId", "ObjectiveId", "DialogueId"
            }));
        }

        [Test]
        public void QuestResponseRoundTripsThroughTheLegacyEnvelope()
        {
            var response = Response(LegacyLobbyOperationStatus.Success, LegacyQuestStatus.RewardPending);
            response.Claim = new LegacyQuestClaim { QuestId = "main_c1_01", AccountXpAfter = 100 };
            response.Claim.Rewards.Add(new LegacyQuestReward { Kind = "Item", RewardId = "consumable_blood_pack", Amount = 2 });

            var packet = LegacyWireCodec.Encode(response, "quest-session-key");
            Assert.That(LegacyWireCodec.TryDecode(packet, packet.Length, "quest-session-key", out var decoded, out _), Is.True);

            var result = LegacyNetworkAdapter.ToQuestResult((LegacyMsgQuestResponse)decoded);
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Journal.Find("main_c1_01").Status, Is.EqualTo(QuestProgressStatus.RewardPending));
            Assert.That(result.Claim.Rewards.Single().Amount, Is.EqualTo(2));
            Assert.That(result.Journal.AccountLevel, Is.EqualTo(2));
        }

        [Test]
        public void RejectionKeepsTheServerJournalForResynchronisation()
        {
            var result = LegacyNetworkAdapter.ToQuestResult(
                Response(LegacyLobbyOperationStatus.NotAvailable, LegacyQuestStatus.Accepted));

            Assert.That(result.Status, Is.EqualTo(QuestOperationStatus.NotAvailable));
            Assert.That(result.Journal, Is.Not.Null);
        }

        [Test]
        public void MalformedJournalIsRejectedAsAWhole()
        {
            var response = Response(LegacyLobbyOperationStatus.Success, LegacyQuestStatus.Accepted);
            response.Journal.Quests.Add(new LegacyQuestEntry { QuestId = "main_c1_01", Status = LegacyQuestStatus.Accepted });

            Assert.That(LegacyNetworkAdapter.ToQuestResult(response).Status, Is.EqualTo(QuestOperationStatus.InternalError));
        }

        [Test]
        public void SuccessWithoutAJournalIsNotTrusted()
        {
            var response = new LegacyMsgQuestResponse
            {
                RequestId = "r", Operation = LegacyQuestOperation.Accept, Status = LegacyLobbyOperationStatus.Success
            };

            Assert.That(LegacyNetworkAdapter.ToQuestResult(response).Status, Is.EqualTo(QuestOperationStatus.InternalError));
        }

        private static LegacyMsgQuestResponse Response(LegacyLobbyOperationStatus status, LegacyQuestStatus questStatus)
        {
            var response = new LegacyMsgQuestResponse
            {
                RequestId = "quest-request-1",
                Operation = LegacyQuestOperation.Claim,
                Status = status,
                Journal = new LegacyQuestJournal { AccountXp = 100, AccountLevel = 2 }
            };
            var entry = new LegacyQuestEntry { QuestId = "main_c1_01", Status = questStatus };
            entry.Objectives.Add(new LegacyQuestObjective
            {
                ObjectiveId = "main_c1_01_o1", Progress = 1, TargetCount = 1, IsCompleted = true
            });
            response.Journal.Quests.Add(entry);
            return response;
        }
    }
}
