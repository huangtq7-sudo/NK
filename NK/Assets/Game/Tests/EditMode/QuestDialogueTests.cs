using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Naraka.Config;
using Naraka.Core.Application.Bootstrap;
using Naraka.Core.Application.Config;
using Naraka.Features.Account.Model;
using Naraka.Features.Character.Controller;
using Naraka.Features.Character.Model;
using Naraka.Features.Dialogue.Controller;
using Naraka.Features.Dialogue.Model;
using Naraka.Features.Dialogue.View;
using Naraka.Features.Quest.Controller;
using Naraka.Features.Quest.Model;
using Naraka.Infrastructure.Config;
using NUnit.Framework;

namespace Naraka.P0.Tests
{
    /// <summary>P4.1 底部对话的揭示规则：一次点击只做一件事。</summary>
    public sealed class DialogueSessionTests
    {
        private static DialogueScript Script(params string[] lines)
        {
            var built = new DialogueLine[lines.Length];
            for (var i = 0; i < lines.Length; i++)
            {
                built[i] = new DialogueLine(
                    i % 2 == 0 ? DialogueSpeakerKind.Npc : DialogueSpeakerKind.Player,
                    i % 2 == 0 ? "沈霜迟" : "你",
                    lines[i]);
            }

            return new DialogueScript("dlg_test", built);
        }

        [Test]
        public void TextRevealsCharacterByCharacterOverTime()
        {
            var session = new DialogueSession(Script("一二三四"));

            session.Tick(0.05f, 20f);
            Assert.That(session.VisibleElements, Is.EqualTo(1));
            session.Tick(0.1f, 20f);
            Assert.That(session.VisibleElements, Is.EqualTo(3));
            Assert.That(session.Phase, Is.EqualTo(DialoguePhase.Revealing));
            session.Tick(1f, 20f);
            Assert.That(session.VisibleElements, Is.EqualTo(4));
            Assert.That(session.Phase, Is.EqualTo(DialoguePhase.Revealed));
        }

        [Test]
        public void ClickWhileRevealingShowsTheWholeLineWithoutAdvancing()
        {
            var session = new DialogueSession(Script("第一句话", "第二句话"));
            session.Tick(0.05f, 20f);

            Assert.That(session.Advance(), Is.EqualTo(DialogueAdvanceResult.RevealedLine));
            Assert.That(session.LineIndex, Is.EqualTo(0));
            Assert.That(session.VisibleElements, Is.EqualTo(4));

            Assert.That(session.Advance(), Is.EqualTo(DialogueAdvanceResult.NextLine));
            Assert.That(session.LineIndex, Is.EqualTo(1));
            Assert.That(session.VisibleElements, Is.EqualTo(0), "新的一行必须从头逐字显示，不能被同一次点击揭示。");
        }

        [Test]
        public void RapidClicksAdvanceExactlyOneStepEach()
        {
            var session = new DialogueSession(Script("甲", "乙", "丙"));
            var results = new List<DialogueAdvanceResult>();
            for (var i = 0; i < 8; i++)
            {
                results.Add(session.Advance());
            }

            Assert.That(results, Is.EqualTo(new[]
            {
                DialogueAdvanceResult.RevealedLine, DialogueAdvanceResult.NextLine,
                DialogueAdvanceResult.RevealedLine, DialogueAdvanceResult.NextLine,
                DialogueAdvanceResult.RevealedLine, DialogueAdvanceResult.Finished,
                DialogueAdvanceResult.Ignored, DialogueAdvanceResult.Ignored
            }));
        }

        [Test]
        public void UnicodeIsCountedByTextElementSoSurrogatesAreNeverSplit()
        {
            // 扩展 B 汉字是代理对，组合重音是两个码元：逐字显示都只算一个字。
            var line = new DialogueLine(DialogueSpeakerKind.Npc, "N", "\U00020000\U00020001é");

            Assert.That(line.ElementCount, Is.EqualTo(3));
        }

        [Test]
        public void NonPositiveSpeedRevealsImmediately()
        {
            var session = new DialogueSession(Script("立即显示"));

            session.Tick(0.016f, 0f);

            Assert.That(session.Phase, Is.EqualTo(DialoguePhase.Revealed));
        }

        [Test]
        public void EmptyLinesAndEmptyScriptsAreRejected()
        {
            Assert.Throws<ArgumentException>(() => new DialogueLine(DialogueSpeakerKind.Npc, "N", string.Empty));
            Assert.Throws<ArgumentException>(() => new DialogueScript("dlg", Array.Empty<DialogueLine>()));
        }

        [Test]
        public void RevealTextKeepsTheLayoutAndNeverSplitsATextElement()
        {
            var text = "甲𠀀乙";
            var starts = System.Globalization.StringInfo.ParseCombiningCharacters(text);
            var builder = new System.Text.StringBuilder();

            Assert.That(DialogueBoxView.BuildRevealText(text, starts, 0, builder),
                Is.EqualTo("<color=#00000000>甲𠀀乙</color>"));
            Assert.That(DialogueBoxView.BuildRevealText(text, starts, 2, builder),
                Is.EqualTo("甲𠀀<color=#00000000>乙</color>"), "代理对作为一个字整体揭示。");
            Assert.That(DialogueBoxView.BuildRevealText(text, starts, 3, builder), Is.EqualTo(text));
        }
    }

    internal sealed class RecordingInputLock : IGameplayInputLock
    {
        private readonly HashSet<object> _owners = new HashSet<object>();

        public bool IsLocked => _owners.Count > 0;

        public void Acquire(object owner) => _owners.Add(owner);

        public void Release(object owner) => _owners.Remove(owner);
    }

    public sealed class DialogueControllerTests
    {
        private static DialogueScript Script(params string[] lines)
        {
            var built = new DialogueLine[lines.Length];
            for (var i = 0; i < lines.Length; i++)
            {
                built[i] = new DialogueLine(DialogueSpeakerKind.Npc, "N", lines[i]);
            }

            return new DialogueScript("dlg_test", built);
        }

        [Test]
        public void NoDialogueStartsWithoutAPresenterSoThePlayerIsNeverLockedBlind()
        {
            var inputLock = new RecordingInputLock();
            var dialogue = new DialogueController(inputLock);

            Assert.That(dialogue.TryStart(Script("一"), _ => { }), Is.False);
            Assert.That(inputLock.IsLocked, Is.False);
        }

        [Test]
        public void CompletingLocksInputUntilTheClosingClickIsReleased()
        {
            var inputLock = new RecordingInputLock();
            var dialogue = new DialogueController(inputLock);
            dialogue.AttachPresenter();
            DialogueEndReason? ended = null;

            Assert.That(dialogue.TryStart(Script("一"), reason => ended = reason), Is.True);
            Assert.That(inputLock.IsLocked, Is.True);
            Assert.That(dialogue.TryStart(Script("二"), _ => { }), Is.False, "同一时间只能有一段对话。");

            dialogue.ReportPointerHeld(true);
            dialogue.Advance();
            dialogue.Advance();
            Assert.That(ended, Is.EqualTo(DialogueEndReason.Completed));
            Assert.That(dialogue.IsOpen, Is.False);
            Assert.That(inputLock.IsLocked, Is.True, "关闭对话的那一下左键还按着时不能把输入还给角色。");

            dialogue.ReportPointerHeld(false);
            Assert.That(inputLock.IsLocked, Is.False);
        }

        [Test]
        public void CancellingReportsCancelledAndNeverCompleted()
        {
            var dialogue = new DialogueController(new RecordingInputLock());
            dialogue.AttachPresenter();
            var reasons = new List<DialogueEndReason>();
            dialogue.TryStart(Script("一", "二"), reasons.Add);

            dialogue.Advance();
            dialogue.Cancel();
            dialogue.Cancel();

            Assert.That(reasons, Is.EqualTo(new[] { DialogueEndReason.Cancelled }));
        }

        [Test]
        public void LosingThePresenterCancelsAndReturnsInputImmediately()
        {
            var inputLock = new RecordingInputLock();
            var dialogue = new DialogueController(inputLock);
            dialogue.AttachPresenter();
            DialogueEndReason? ended = null;
            dialogue.TryStart(Script("一"), reason => ended = reason);
            dialogue.ReportPointerHeld(true);

            dialogue.DetachPresenter();

            Assert.That(ended, Is.EqualTo(DialogueEndReason.Cancelled));
            Assert.That(inputLock.IsLocked, Is.False);
        }

        [Test]
        public void DisposingDuringADialogueReleasesInputWithoutCallingBack()
        {
            var inputLock = new RecordingInputLock();
            var dialogue = new DialogueController(inputLock);
            dialogue.AttachPresenter();
            var called = false;
            dialogue.TryStart(Script("一"), _ => called = true);

            dialogue.Dispose();

            Assert.That(called, Is.False);
            Assert.That(inputLock.IsLocked, Is.False);
        }
    }

    internal sealed class FakeQuestGateway : IQuestGateway
    {
        public readonly Queue<QuestGatewayResult> Results = new Queue<QuestGatewayResult>();
        public readonly List<(string Operation, string RequestId, string QuestId, string ObjectiveId, string DialogueId)> Calls =
            new List<(string, string, string, string, string)>();

        public UniTask<QuestGatewayResult> GetJournalAsync(string requestId, CancellationToken cancellationToken) =>
            Next("journal", requestId, null, null, null);

        public UniTask<QuestGatewayResult> AcceptAsync(
            string requestId, string questId, string dialogueId, CancellationToken cancellationToken) =>
            Next("accept", requestId, questId, null, dialogueId);

        public UniTask<QuestGatewayResult> CompleteDialogueAsync(
            string requestId, string questId, string objectiveId, string dialogueId, CancellationToken cancellationToken) =>
            Next("talk", requestId, questId, objectiveId, dialogueId);

        public UniTask<QuestGatewayResult> SubmitAsync(
            string requestId, string questId, string dialogueId, CancellationToken cancellationToken) =>
            Next("submit", requestId, questId, null, dialogueId);

        public UniTask<QuestGatewayResult> ClaimAsync(string requestId, string questId, CancellationToken cancellationToken) =>
            Next("claim", requestId, questId, null, null);

        private UniTask<QuestGatewayResult> Next(
            string operation, string requestId, string questId, string objectiveId, string dialogueId)
        {
            Calls.Add((operation, requestId, questId, objectiveId, dialogueId));
            return UniTask.FromResult(Results.Count > 0
                ? Results.Dequeue()
                : QuestGatewayResult.Failed(QuestOperationStatus.InternalError));
        }
    }

    internal sealed class SequenceQuestRequestIds : IQuestRequestIdSource
    {
        private int _next;

        public string NewId() => "quest-request-" + (++_next);
    }

    internal static class QuestTestData
    {
        public const string Quest = "main_c1_01";
        public const string Giver = "npc_shuangchi";
        public const string Target = "npc_yunsheng";

        private static IGameConfigProvider _config;

        public static IGameConfigProvider Config
        {
            get
            {
                if (_config == null)
                {
                    var provider = new StreamingAssetsGameConfigProvider();
                    Assert.That(provider.IsLoaded, Is.True, "真实生成配置未能加载：" + provider.LoadError);
                    _config = provider;
                }

                return _config;
            }
        }

        public static QuestContentCatalog Content => new QuestContentCatalog(Config.Catalog.Catalog);

        public static IServerCapabilities Capabilities(bool withQuest)
        {
            var registry = new ServerCapabilityRegistry();
            registry.Resolve(withQuest
                ? NarakaServerCapabilities.Full
                : new[] { NarakaServerCapabilities.LobbyAccountSummary, NarakaServerCapabilities.Expedition });
            return registry;
        }

        public static QuestJournalSnapshot Journal(QuestProgressStatus? status, bool objectiveDone = false, bool available = false)
        {
            var entries = new List<QuestEntrySnapshot>();
            if (status.HasValue)
            {
                entries.Add(new QuestEntrySnapshot(Quest, status.Value, new[]
                {
                    new QuestObjectiveSnapshot("main_c1_01_o1", objectiveDone ? 1 : 0, 1, objectiveDone)
                }));
            }

            Assert.That(QuestJournalSnapshot.TryCreate(
                entries, available ? new[] { Quest } : Array.Empty<string>(), 0, 1, out var journal), Is.True);
            return journal;
        }
    }

    /// <summary>按任务状态为 NPC 选择对话的固定优先级。</summary>
    public sealed class QuestConversationPlannerTests
    {
        [Test]
        public void AvailableQuestMakesTheGiverOfferIt()
        {
            var plan = QuestConversationPlanner.Plan(
                QuestTestData.Giver, QuestTestData.Journal(null, available: true), QuestTestData.Content);

            Assert.That(plan.Intent, Is.EqualTo(QuestConversationIntent.Accept));
            Assert.That(plan.DialogueId, Is.EqualTo("dlg_c1_01_offer"));
        }

        [Test]
        public void AcceptedQuestSendsThePlayerToTheTalkTarget()
        {
            var journal = QuestTestData.Journal(QuestProgressStatus.Accepted);

            var target = QuestConversationPlanner.Plan(QuestTestData.Target, journal, QuestTestData.Content);
            var giver = QuestConversationPlanner.Plan(QuestTestData.Giver, journal, QuestTestData.Content);

            Assert.That(target.Intent, Is.EqualTo(QuestConversationIntent.CompleteObjective));
            Assert.That(target.ObjectiveId, Is.EqualTo("main_c1_01_o1"));
            Assert.That(target.DialogueId, Is.EqualTo("dlg_c1_01_yunsheng"));
            Assert.That(giver.Intent, Is.EqualTo(QuestConversationIntent.None));
            Assert.That(giver.DialogueId, Is.EqualTo("dlg_c1_01_inprogress"));
        }

        [Test]
        public void ReadyToSubmitAndRewardPendingRouteToTheSubmitter()
        {
            var ready = QuestConversationPlanner.Plan(
                QuestTestData.Giver,
                QuestTestData.Journal(QuestProgressStatus.ReadyToSubmit, objectiveDone: true),
                QuestTestData.Content);
            var pending = QuestConversationPlanner.Plan(
                QuestTestData.Giver,
                QuestTestData.Journal(QuestProgressStatus.RewardPending, objectiveDone: true),
                QuestTestData.Content);

            Assert.That(ready.Intent, Is.EqualTo(QuestConversationIntent.Submit));
            Assert.That(ready.DialogueId, Is.EqualTo("dlg_c1_01_submit"));
            Assert.That(pending.Intent, Is.EqualTo(QuestConversationIntent.OpenClaim));
            Assert.That(pending.DialogueId, Is.EqualTo("dlg_c1_01_pending"));
        }

        [Test]
        public void ClaimedOrUnsyncedJournalsOnlyPlayIdleLines()
        {
            var claimed = QuestConversationPlanner.Plan(
                QuestTestData.Giver,
                QuestTestData.Journal(QuestProgressStatus.Claimed, objectiveDone: true),
                QuestTestData.Content);
            var unsynced = QuestConversationPlanner.Plan(QuestTestData.Target, null, QuestTestData.Content);

            Assert.That(claimed.Intent, Is.EqualTo(QuestConversationIntent.None));
            Assert.That(claimed.DialogueId, Is.EqualTo("dlg_shuangchi_idle"));
            Assert.That(unsynced.Intent, Is.EqualTo(QuestConversationIntent.None));
            Assert.That(unsynced.DialogueId, Is.EqualTo("dlg_yunsheng_idle"));
        }

        [Test]
        public void UnknownNpcHasNoConversation()
        {
            Assert.That(QuestConversationPlanner.Plan("npc_missing", null, QuestTestData.Content), Is.Null);
        }

        [Test]
        public void DialogueObjectivesAreNotCombatTasks()
        {
            Assert.That(
                ConfigQuestObjectiveType.CategoryOf(ConfigQuestObjectiveType.Talk),
                Is.EqualTo(ConfigQuestObjectiveCategory.Dialogue));
            Assert.That(
                ConfigQuestObjectiveType.CategoryOf(ConfigQuestObjectiveType.Kill),
                Is.EqualTo(ConfigQuestObjectiveCategory.Combat));
        }
    }

    public sealed class QuestControllerTests
    {
        private static QuestController Create(FakeQuestGateway gateway, bool withQuest = true) => new QuestController(
            new QuestModel(),
            gateway,
            QuestTestData.Capabilities(withQuest),
            QuestTestData.Config,
            new SequenceQuestRequestIds());

        [Test]
        public void HostWithoutTheQuestCapabilityReceivesNoQuestProtocol()
        {
            var gateway = new FakeQuestGateway();
            var controller = Create(gateway, withQuest: false);

            var status = controller.RefreshAsync(CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(status, Is.EqualTo(QuestOperationStatus.ServerCapabilityMissing));
            Assert.That(gateway.Calls, Is.Empty);
            Assert.That(controller.Current.StatusMessage, Is.EqualTo("服务器功能尚未升级。"));
        }

        [Test]
        public void TransportFailureRetriesTheSameIntentWithTheSameRequestId()
        {
            var gateway = new FakeQuestGateway();
            var controller = Create(gateway);
            gateway.Results.Enqueue(QuestGatewayResult.Failed(QuestOperationStatus.TransportFailure));
            gateway.Results.Enqueue(QuestGatewayResult.Success(QuestTestData.Journal(QuestProgressStatus.Accepted)));
            gateway.Results.Enqueue(QuestGatewayResult.Failed(QuestOperationStatus.NotAvailable));

            controller.AcceptAsync(QuestTestData.Quest, "dlg_c1_01_offer", CancellationToken.None).GetAwaiter().GetResult();
            controller.AcceptAsync(QuestTestData.Quest, "dlg_c1_01_offer", CancellationToken.None).GetAwaiter().GetResult();
            controller.AcceptAsync(QuestTestData.Quest, "dlg_c1_01_offer", CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(gateway.Calls[0].RequestId, Is.EqualTo(gateway.Calls[1].RequestId),
                "结果未知时必须复用 RequestId，让服务端重放而不是重复执行。");
            Assert.That(gateway.Calls[2].RequestId, Is.Not.EqualTo(gateway.Calls[1].RequestId),
                "收到服务端明确响应后，同一意图的下一次请求必须是新的 RequestId。");
        }

        [Test]
        public void JournalOnlyChangesFromServerResponsesIncludingRejections()
        {
            var gateway = new FakeQuestGateway();
            var controller = Create(gateway);
            gateway.Results.Enqueue(QuestGatewayResult.Failed(
                QuestOperationStatus.NotAvailable, QuestTestData.Journal(QuestProgressStatus.ReadyToSubmit, true)));

            var status = controller.CompleteDialogueAsync(
                QuestTestData.Quest, "main_c1_01_o1", "dlg_c1_01_yunsheng", CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.That(status, Is.EqualTo(QuestOperationStatus.NotAvailable));
            Assert.That(controller.Journal.Find(QuestTestData.Quest).Status, Is.EqualTo(QuestProgressStatus.ReadyToSubmit));
            Assert.That(controller.Current.TrackedObjectiveText, Is.EqualTo("回到沈霜迟处复命"));
            Assert.That(controller.Current.MarkerFor(QuestTestData.Giver), Is.EqualTo(QuestNpcMarker.ReadyToSubmit));
        }

        [Test]
        public void TrackerShowsTheAvailableMainQuestBeforeAcceptance()
        {
            var gateway = new FakeQuestGateway();
            var controller = Create(gateway);
            gateway.Results.Enqueue(QuestGatewayResult.Success(QuestTestData.Journal(null, available: true)));

            controller.RefreshAsync(CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(controller.Current.TrackedTitle, Is.EqualTo("入世·问路"));
            Assert.That(controller.Current.TrackedStatus, Is.EqualTo(QuestProgressStatus.Available));
            Assert.That(controller.Current.MarkerFor(QuestTestData.Giver), Is.EqualTo(QuestNpcMarker.Available));
        }

        [Test]
        public void JournalWithAnUnpersistableStatusIsRejected()
        {
            var entries = new[] { new QuestEntrySnapshot(QuestTestData.Quest, QuestProgressStatus.Available, null) };

            Assert.That(QuestJournalSnapshot.TryCreate(entries, Array.Empty<string>(), 0, 1, out _), Is.False);
        }
    }

    public sealed class NpcInteractionControllerTests
    {
        private sealed class Fixture
        {
            public FakeQuestGateway Gateway { get; } = new FakeQuestGateway();
            public RecordingInputLock InputLock { get; } = new RecordingInputLock();
            public PlayerController Player { get; } = new PlayerController(PlayerTuning.CreateBaseline());
            public QuestController Quests { get; }
            public DialogueController Dialogue { get; }
            public NpcInteractionController Interaction { get; }

            public Fixture(QuestJournalSnapshot journal)
            {
                Quests = new QuestController(
                    new QuestModel(),
                    Gateway,
                    QuestTestData.Capabilities(true),
                    QuestTestData.Config,
                    new SequenceQuestRequestIds());
                Gateway.Results.Enqueue(QuestGatewayResult.Success(journal));
                Quests.RefreshAsync(CancellationToken.None).GetAwaiter().GetResult();
                Gateway.Calls.Clear();

                Dialogue = new DialogueController(InputLock);
                Dialogue.AttachPresenter();
                var session = new AccountSessionModel();
                session.Authenticate("tester", 7);
                Interaction = new NpcInteractionController(
                    Quests, Dialogue, Player, new NoInteractInputSource(), QuestTestData.Config, InputLock, session);
            }

            public void ReadToTheEnd()
            {
                for (var i = 0; i < 64 && Dialogue.IsOpen; i++)
                {
                    Dialogue.Advance();
                }
            }
        }

        [Test]
        public void OverlappingRangesFocusTheNearestNpcWithHysteresis()
        {
            var fixture = new Fixture(QuestTestData.Journal(null, available: true));
            var interaction = fixture.Interaction;

            interaction.ReportCandidate(QuestTestData.Giver, true, 2f);
            interaction.ReportCandidate(QuestTestData.Target, true, 1.9f);
            Assert.That(interaction.Current.FocusedNpcId, Is.EqualTo(QuestTestData.Giver), "近不到 0.3 米不切换焦点。");

            interaction.ReportCandidate(QuestTestData.Target, true, 1.5f);
            Assert.That(interaction.Current.FocusedNpcId, Is.EqualTo(QuestTestData.Target));

            interaction.ReportCandidate(QuestTestData.Target, false, 0f);
            Assert.That(interaction.Current.FocusedNpcId, Is.EqualTo(QuestTestData.Giver));
            Assert.That(interaction.Current.FocusedNpcName, Is.EqualTo("沈霜迟"));
        }

        [Test]
        public void ReadingTheOfferDialogueToTheEndSendsOnlyTheAcceptIntent()
        {
            var fixture = new Fixture(QuestTestData.Journal(null, available: true));
            fixture.Interaction.ReportCandidate(QuestTestData.Giver, true, 1f);
            fixture.Gateway.Results.Enqueue(QuestGatewayResult.Success(QuestTestData.Journal(QuestProgressStatus.Accepted)));

            fixture.Interaction.Interact();
            Assert.That(fixture.Dialogue.IsOpen, Is.True);
            Assert.That(fixture.Dialogue.Current.SpeakerName, Is.EqualTo("沈霜迟"));
            Assert.That(fixture.InputLock.IsLocked, Is.True);
            Assert.That(fixture.Gateway.Calls, Is.Empty, "对话还没读完，不能发任何意图。");

            fixture.ReadToTheEnd();

            Assert.That(fixture.Gateway.Calls.Count, Is.EqualTo(1));
            Assert.That(fixture.Gateway.Calls[0].Operation, Is.EqualTo("accept"));
            Assert.That(fixture.Gateway.Calls[0].DialogueId, Is.EqualTo("dlg_c1_01_offer"));
            Assert.That(fixture.Quests.Journal.Find(QuestTestData.Quest).Status, Is.EqualTo(QuestProgressStatus.Accepted));
        }

        [Test]
        public void ClosingTheDialogueEarlyAcceptsNothing()
        {
            var fixture = new Fixture(QuestTestData.Journal(null, available: true));
            fixture.Interaction.ReportCandidate(QuestTestData.Giver, true, 1f);

            fixture.Interaction.Interact();
            fixture.Dialogue.Advance();
            fixture.Dialogue.Cancel();

            Assert.That(fixture.Gateway.Calls, Is.Empty);
        }

        [Test]
        public void LeavingTheRangeCancelsTheConversation()
        {
            var fixture = new Fixture(QuestTestData.Journal(QuestProgressStatus.Accepted));
            fixture.Interaction.ReportCandidate(QuestTestData.Target, true, 1f);
            fixture.Interaction.Interact();
            Assert.That(fixture.Dialogue.IsOpen, Is.True);

            fixture.Interaction.ReportCandidate(QuestTestData.Target, false, 0f);
            fixture.Interaction.Tick(0.016f);

            Assert.That(fixture.Dialogue.IsOpen, Is.False);
            Assert.That(fixture.Gateway.Calls, Is.Empty);
        }

        [Test]
        public void LockedOrDeadPlayersCannotStartAConversation()
        {
            var fixture = new Fixture(QuestTestData.Journal(null, available: true));
            fixture.Interaction.ReportCandidate(QuestTestData.Giver, true, 1f);
            fixture.Player.SetInputLocked(true);
            fixture.Player.Tick(PlayerInputFrame.Idle, 0.016f);

            fixture.Interaction.Interact();

            Assert.That(fixture.Dialogue.IsOpen, Is.False);
        }

        [Test]
        public void SubmittingOpensTheRewardPanelAndClaimingClosesItAndReturnsInput()
        {
            var fixture = new Fixture(QuestTestData.Journal(QuestProgressStatus.ReadyToSubmit, objectiveDone: true));
            fixture.Interaction.ReportCandidate(QuestTestData.Giver, true, 1f);
            fixture.Gateway.Results.Enqueue(QuestGatewayResult.Success(
                QuestTestData.Journal(QuestProgressStatus.RewardPending, objectiveDone: true)));

            fixture.Interaction.Interact();
            fixture.ReadToTheEnd();
            fixture.Dialogue.ReportPointerHeld(false);

            Assert.That(fixture.Interaction.Current.IsRewardPanelOpen, Is.True);
            Assert.That(fixture.Interaction.Current.RewardLines, Has.Some.Contains("账号经验 100"));
            Assert.That(fixture.InputLock.IsLocked, Is.True, "领取面板打开期间屏蔽角色输入。");

            fixture.Gateway.Results.Enqueue(QuestGatewayResult.Success(
                QuestTestData.Journal(QuestProgressStatus.Claimed, objectiveDone: true),
                new QuestClaimSummary(QuestTestData.Quest, new[] { new QuestRewardLine("Currency", "Copper", 300) }, 100)));
            fixture.Interaction.RequestClaim();

            Assert.That(fixture.Gateway.Calls[fixture.Gateway.Calls.Count - 1].Operation, Is.EqualTo("claim"));
            Assert.That(fixture.Interaction.Current.IsRewardPanelOpen, Is.False);
            Assert.That(fixture.InputLock.IsLocked, Is.False);
            Assert.That(fixture.Interaction.Current.Notice, Does.Contain("铜币 ×300"));
        }
    }
}
