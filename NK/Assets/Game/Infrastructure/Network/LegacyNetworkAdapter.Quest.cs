using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Naraka.Features.Quest.Controller;
using Naraka.Features.Quest.Model;

namespace Naraka.Infrastructure.Network
{
    /// <summary>
    /// P4.1 任务网关。只在服务端声明 <c>quest</c> 能力时由控制器调用；
    /// 旧 Host 不会收到 67 号协议。冻结的帧、AES 与握手不变。
    /// </summary>
    public sealed partial class LegacyNetworkAdapter
    {
        public UniTask<QuestGatewayResult> GetJournalAsync(string requestId, CancellationToken cancellationToken) =>
            RequestQuestAsync(
                new LegacyMsgQuestRequest { RequestId = requestId, Operation = LegacyQuestOperation.GetJournal },
                cancellationToken);

        public UniTask<QuestGatewayResult> AcceptAsync(
            string requestId,
            string questId,
            string dialogueId,
            CancellationToken cancellationToken) =>
            RequestQuestAsync(
                new LegacyMsgQuestRequest
                {
                    RequestId = requestId,
                    Operation = LegacyQuestOperation.Accept,
                    QuestId = questId,
                    DialogueId = dialogueId
                },
                cancellationToken);

        public UniTask<QuestGatewayResult> CompleteDialogueAsync(
            string requestId,
            string questId,
            string objectiveId,
            string dialogueId,
            CancellationToken cancellationToken) =>
            RequestQuestAsync(
                new LegacyMsgQuestRequest
                {
                    RequestId = requestId,
                    Operation = LegacyQuestOperation.CompleteDialogue,
                    QuestId = questId,
                    ObjectiveId = objectiveId,
                    DialogueId = dialogueId
                },
                cancellationToken);

        public UniTask<QuestGatewayResult> SubmitAsync(
            string requestId,
            string questId,
            string dialogueId,
            CancellationToken cancellationToken) =>
            RequestQuestAsync(
                new LegacyMsgQuestRequest
                {
                    RequestId = requestId,
                    Operation = LegacyQuestOperation.Submit,
                    QuestId = questId,
                    DialogueId = dialogueId
                },
                cancellationToken);

        public UniTask<QuestGatewayResult> ClaimAsync(
            string requestId,
            string questId,
            CancellationToken cancellationToken) =>
            RequestQuestAsync(
                new LegacyMsgQuestRequest
                {
                    RequestId = requestId,
                    Operation = LegacyQuestOperation.Claim,
                    QuestId = questId
                },
                cancellationToken);

        private async UniTask<QuestGatewayResult> RequestQuestAsync(
            LegacyMsgQuestRequest request,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.RequestId) || request.RequestId.Length > 64)
            {
                return QuestGatewayResult.Failed(QuestOperationStatus.InvalidRequest);
            }

            LegacyMsgQuestResponse response;
            try
            {
                response = await SendRequestAsync<LegacyMsgQuestRequest, LegacyMsgQuestResponse>(
                    request,
                    LegacyProtocolValue.MsgQuestResponse,
                    DefaultRequestTimeout,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return QuestGatewayResult.Failed(QuestOperationStatus.TransportFailure);
            }

            // 任务共用一个响应协议号：迟到的响应不能被当作另一次操作的结果。
            if (!string.Equals(response.RequestId, request.RequestId, StringComparison.Ordinal) ||
                response.Operation != request.Operation)
            {
                return QuestGatewayResult.Failed(QuestOperationStatus.InternalError);
            }

            return ToQuestResult(response);
        }

        internal static QuestGatewayResult ToQuestResult(LegacyMsgQuestResponse response)
        {
            var status = Enum.IsDefined(typeof(QuestOperationStatus), (int)response.Status) && (int)response.Status < 100
                ? (QuestOperationStatus)(int)response.Status
                : QuestOperationStatus.InternalError;

            QuestJournalSnapshot journal = null;
            if (response.Journal != null && !TryConvertJournal(response.Journal, out journal))
            {
                return QuestGatewayResult.Failed(QuestOperationStatus.InternalError);
            }

            if (status != QuestOperationStatus.Success)
            {
                return QuestGatewayResult.Failed(status, journal);
            }

            if (journal == null)
            {
                return QuestGatewayResult.Failed(QuestOperationStatus.InternalError);
            }

            QuestClaimSummary claim = null;
            if (response.Claim != null)
            {
                var rewards = new QuestRewardLine[response.Claim.Rewards.Count];
                for (var i = 0; i < rewards.Length; i++)
                {
                    var reward = response.Claim.Rewards[i];
                    if (reward == null || reward.Amount < 0)
                    {
                        return QuestGatewayResult.Failed(QuestOperationStatus.InternalError);
                    }

                    rewards[i] = new QuestRewardLine(reward.Kind, reward.RewardId, reward.Amount);
                }

                claim = new QuestClaimSummary(response.Claim.QuestId, rewards, response.Claim.AccountXpAfter);
            }

            return QuestGatewayResult.Success(journal, claim, response.IsReplay);
        }

        private static bool TryConvertJournal(LegacyQuestJournal wire, out QuestJournalSnapshot journal)
        {
            journal = null;
            var entries = new QuestEntrySnapshot[wire.Quests.Count];
            for (var i = 0; i < entries.Length; i++)
            {
                var entry = wire.Quests[i];
                if (entry == null)
                {
                    return false;
                }

                var objectives = new QuestObjectiveSnapshot[entry.Objectives.Count];
                for (var j = 0; j < objectives.Length; j++)
                {
                    var objective = entry.Objectives[j];
                    if (objective == null || string.IsNullOrEmpty(objective.ObjectiveId) ||
                        objective.Progress < 0 || objective.TargetCount <= 0)
                    {
                        return false;
                    }

                    objectives[j] = new QuestObjectiveSnapshot(
                        objective.ObjectiveId, objective.Progress, objective.TargetCount, objective.IsCompleted);
                }

                entries[i] = new QuestEntrySnapshot(entry.QuestId, (QuestProgressStatus)(int)entry.Status, objectives);
            }

            return QuestJournalSnapshot.TryCreate(
                entries, wire.AvailableQuestIds, wire.AccountXp, wire.AccountLevel, out journal);
        }
    }
}
