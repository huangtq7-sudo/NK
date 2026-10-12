using Naraka.Server.Application.Config;
using Naraka.Server.Application.Progression;
using Naraka.Server.Application.Quests;
using Naraka.Server.Domain.Quests;
using Naraka.Server.Tests.Shared;

namespace Naraka.Server.Application.Tests;

/// <summary>
/// P4.1 任务用例。使用仓库中真实生成的配置（首条主线 main_c1_01），
/// 证明接取 → 对话目标 → 提交 → 手动领取的闭环只信任服务端配置与服务端时间，
/// 并且同一实例无论换不换 RequestId 都只能领一次。
/// </summary>
public sealed class QuestServiceTests
{
    private const long AccountId = 7;
    private const string Quest = "main_c1_01";
    private const string Objective = "main_c1_01_o1";
    private const string OfferDialogue = "dlg_c1_01_offer";
    private const string TalkDialogue = "dlg_c1_01_yunsheng";
    private const string SubmitDialogue = "dlg_c1_01_submit";

    private static readonly GameConfig Config = LoadConfig();

    private static GameConfig LoadConfig()
    {
        var json = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Config", "naraka-config.json"),
            System.Text.Encoding.UTF8);
        return GameConfig.Load(json, GameConfig.RequiredSchemaVersion).Config
               ?? throw new InvalidOperationException("测试无法加载生成配置。");
    }

    private sealed record Fixture(
        QuestService Service,
        MemoryQuestRepository Repository,
        FixedExpeditionProbe Expedition,
        ManualTimeProvider Time)
    {
        /// <summary>每一步之间推进足够的服务端时间，越过限流与最短时长。</summary>
        public void Wait(double seconds = 5) => Time.Advance(TimeSpan.FromSeconds(seconds));
    }

    private static Fixture Create()
    {
        var repository = new MemoryQuestRepository { MaximumAccountXp = Config.MaximumAccountXp };
        repository.AddAccount(AccountId);
        var expedition = new FixedExpeditionProbe();
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 10, 11, 8, 0, 0, TimeSpan.Zero));
        return new Fixture(new QuestService(repository, expedition, Config, time), repository, expedition, time);
    }

    private static async Task<Fixture> AdvanceToRewardPendingAsync()
    {
        var fixture = Create();
        Assert.Equal(LobbyOperationStatus.Success,
            (await fixture.Service.AcceptAsync(AccountId, "accept-1", Quest, OfferDialogue, default)).Status);
        fixture.Wait();
        Assert.Equal(LobbyOperationStatus.Success,
            (await fixture.Service.CompleteDialogueAsync(
                AccountId, "talk-1", Quest, Objective, TalkDialogue, default)).Status);
        fixture.Wait();
        Assert.Equal(LobbyOperationStatus.Success,
            (await fixture.Service.SubmitAsync(AccountId, "submit-1", Quest, SubmitDialogue, default)).Status);
        fixture.Wait();
        return fixture;
    }

    [Fact]
    public async Task FirstMainQuestCompletesTheWholeServerAuthoritativeLoop()
    {
        var fixture = Create();

        var journal = await fixture.Service.GetJournalAsync(AccountId, default);
        Assert.Equal(LobbyOperationStatus.Success, journal.Status);
        Assert.Contains(Quest, journal.Journal!.AvailableQuestIds);
        Assert.Empty(journal.Journal.Quests);

        var accepted = await fixture.Service.AcceptAsync(AccountId, "accept-1", Quest, OfferDialogue, default);
        Assert.Equal(LobbyOperationStatus.Success, accepted.Status);
        Assert.Equal(QuestStatus.Accepted, Assert.Single(accepted.Journal!.Quests).Status);
        Assert.DoesNotContain(Quest, accepted.Journal.AvailableQuestIds);

        fixture.Wait();
        var talked = await fixture.Service.CompleteDialogueAsync(
            AccountId, "talk-1", Quest, Objective, TalkDialogue, default);
        Assert.Equal(LobbyOperationStatus.Success, talked.Status);
        var afterTalk = Assert.Single(talked.Journal!.Quests);
        Assert.Equal(QuestStatus.ReadyToSubmit, afterTalk.Status);
        Assert.True(Assert.Single(afterTalk.Objectives).IsCompleted);

        fixture.Wait();
        var submitted = await fixture.Service.SubmitAsync(AccountId, "submit-1", Quest, SubmitDialogue, default);
        Assert.Equal(LobbyOperationStatus.Success, submitted.Status);
        Assert.Equal(QuestStatus.RewardPending, Assert.Single(submitted.Journal!.Quests).Status);
        Assert.Empty(fixture.Repository.Grants);

        fixture.Wait();
        var claimed = await fixture.Service.ClaimAsync(AccountId, "claim-1", Quest, default);
        Assert.Equal(LobbyOperationStatus.Success, claimed.Status);
        Assert.False(claimed.IsReplay);
        Assert.Equal(QuestStatus.Claimed, Assert.Single(claimed.Journal!.Quests).Status);

        // 奖励只来自服务端配置：账号经验 100、铜币 300、血包 2。
        var grant = Assert.Single(fixture.Repository.Grants);
        Assert.Equal(100, grant.AccountXp);
        Assert.Equal(300, Assert.Single(grant.Currencies, value => value.CurrencyId == "Copper").Amount);
        Assert.Equal(2, Assert.Single(grant.Items, value => value.ItemId == "consumable_blood_pack").Amount);
        Assert.Equal(100, fixture.Repository.AccountXp(AccountId));
        Assert.Equal(2, claimed.Journal.AccountLevel);
        Assert.Equal(100, claimed.Claim!.AccountXpAfter);
    }

    [Fact]
    public async Task ClaimRetryWithTheSameRequestIdReplaysWithoutGrantingAgain()
    {
        var fixture = await AdvanceToRewardPendingAsync();
        var first = await fixture.Service.ClaimAsync(AccountId, "claim-1", Quest, default);
        fixture.Wait();

        var retry = await fixture.Service.ClaimAsync(AccountId, "claim-1", Quest, default);

        Assert.Equal(LobbyOperationStatus.Success, retry.Status);
        Assert.True(retry.IsReplay);
        Assert.Equal(first.Claim!.AccountXpAfter, retry.Claim!.AccountXpAfter);
        Assert.Single(fixture.Repository.Grants);
        Assert.Equal(100, fixture.Repository.AccountXp(AccountId));
    }

    [Fact]
    public async Task ClaimWithADifferentRequestIdIsRejectedAsAlreadyClaimed()
    {
        var fixture = await AdvanceToRewardPendingAsync();
        await fixture.Service.ClaimAsync(AccountId, "claim-1", Quest, default);
        fixture.Wait();

        var second = await fixture.Service.ClaimAsync(AccountId, "claim-2", Quest, default);

        Assert.Equal(LobbyOperationStatus.AlreadyClaimed, second.Status);
        Assert.Single(fixture.Repository.Grants);
        Assert.Equal(100, fixture.Repository.AccountXp(AccountId));
    }

    [Fact]
    public async Task FullInventoryKeepsTheRewardPendingUntilThePlayerMakesRoom()
    {
        var fixture = await AdvanceToRewardPendingAsync();
        fixture.Repository.FreeInventorySlots = 0;

        var full = await fixture.Service.ClaimAsync(AccountId, "claim-1", Quest, default);
        Assert.Equal(LobbyOperationStatus.InventoryFull, full.Status);
        Assert.Equal(QuestStatus.RewardPending, Assert.Single(full.Journal!.Quests).Status);
        Assert.Empty(fixture.Repository.Grants);

        fixture.Repository.FreeInventorySlots = 10;
        fixture.Wait();
        var retry = await fixture.Service.ClaimAsync(AccountId, "claim-1", Quest, default);
        Assert.Equal(LobbyOperationStatus.Success, retry.Status);
        Assert.Single(fixture.Repository.Grants);
    }

    [Fact]
    public async Task DialogueObjectiveFinishedFasterThanTheServerMinimumIsRejected()
    {
        var fixture = Create();
        await fixture.Service.AcceptAsync(AccountId, "accept-1", Quest, OfferDialogue, default);
        fixture.Wait(1);

        var tooEarly = await fixture.Service.CompleteDialogueAsync(
            AccountId, "talk-1", Quest, Objective, TalkDialogue, default);

        Assert.Equal(LobbyOperationStatus.RateLimited, tooEarly.Status);
        Assert.Equal(QuestStatus.Accepted, fixture.Repository.StatusOf(AccountId, Quest));
    }

    [Fact]
    public async Task WorldStepsRequireAnActiveExpeditionButClaimDoesNot()
    {
        var fixture = Create();
        fixture.Expedition.Active = false;

        var accept = await fixture.Service.AcceptAsync(AccountId, "accept-1", Quest, OfferDialogue, default);
        Assert.Equal(LobbyOperationStatus.NotAvailable, accept.Status);
        Assert.NotNull(accept.Journal);
        Assert.Equal(0, fixture.Repository.Writes);

        var pending = await AdvanceToRewardPendingAsync();
        pending.Expedition.Active = false;
        var claim = await pending.Service.ClaimAsync(AccountId, "claim-1", Quest, default);
        Assert.Equal(LobbyOperationStatus.Success, claim.Status);
    }

    [Theory]
    [InlineData("dlg_c1_01_submit")]
    [InlineData("dlg_shuangchi_idle")]
    [InlineData("dlg_unknown")]
    public async Task AcceptingWithADialogueThatIsNotTheOfferDialogueIsInvalid(string dialogueId)
    {
        var fixture = Create();

        var result = await fixture.Service.AcceptAsync(AccountId, "accept-1", Quest, dialogueId, default);

        Assert.Equal(LobbyOperationStatus.InvalidRequest, result.Status);
        Assert.Equal(0, fixture.Repository.Writes);
    }

    [Fact]
    public async Task UnknownQuestOrForgedObjectiveIsInvalid()
    {
        var fixture = Create();
        await fixture.Service.AcceptAsync(AccountId, "accept-1", Quest, OfferDialogue, default);
        fixture.Wait();

        Assert.Equal(LobbyOperationStatus.InvalidRequest,
            (await fixture.Service.AcceptAsync(AccountId, "accept-2", "main_missing", OfferDialogue, default)).Status);
        fixture.Wait();
        Assert.Equal(LobbyOperationStatus.InvalidRequest,
            (await fixture.Service.CompleteDialogueAsync(
                AccountId, "talk-1", Quest, "main_c1_01_o9", TalkDialogue, default)).Status);
        fixture.Wait();
        Assert.Equal(LobbyOperationStatus.InvalidRequest,
            (await fixture.Service.CompleteDialogueAsync(
                AccountId, "talk-2", Quest, Objective, OfferDialogue, default)).Status);
        Assert.Equal(1, fixture.Repository.Writes);
    }

    [Fact]
    public async Task StepsOutOfOrderAreNotAvailable()
    {
        var fixture = Create();

        var talkBeforeAccept = await fixture.Service.CompleteDialogueAsync(
            AccountId, "talk-1", Quest, Objective, TalkDialogue, default);
        Assert.Equal(LobbyOperationStatus.NotAvailable, talkBeforeAccept.Status);

        fixture.Wait();
        await fixture.Service.AcceptAsync(AccountId, "accept-1", Quest, OfferDialogue, default);
        fixture.Wait();
        var submitBeforeTalk = await fixture.Service.SubmitAsync(AccountId, "submit-1", Quest, SubmitDialogue, default);
        Assert.Equal(LobbyOperationStatus.NotAvailable, submitBeforeTalk.Status);

        fixture.Wait();
        var claimBeforeSubmit = await fixture.Service.ClaimAsync(AccountId, "claim-1", Quest, default);
        Assert.Equal(LobbyOperationStatus.NotAvailable, claimBeforeSubmit.Status);
        Assert.Empty(fixture.Repository.Grants);
    }

    [Fact]
    public async Task AcceptingTwiceDoesNotCreateASecondInstance()
    {
        var fixture = Create();
        await fixture.Service.AcceptAsync(AccountId, "accept-1", Quest, OfferDialogue, default);
        fixture.Wait();

        var second = await fixture.Service.AcceptAsync(AccountId, "accept-2", Quest, OfferDialogue, default);

        Assert.Equal(LobbyOperationStatus.NotAvailable, second.Status);
        Assert.Equal(1, fixture.Repository.Writes);
    }

    [Fact]
    public async Task ReusingARequestIdForAnotherOperationIsInvalid()
    {
        var fixture = Create();
        await fixture.Service.AcceptAsync(AccountId, "shared-id", Quest, OfferDialogue, default);
        fixture.Wait();

        var reused = await fixture.Service.CompleteDialogueAsync(
            AccountId, "shared-id", Quest, Objective, TalkDialogue, default);

        Assert.Equal(LobbyOperationStatus.InvalidRequest, reused.Status);
    }

    [Fact]
    public async Task RapidWritesFromOneAccountAreRateLimited()
    {
        var fixture = Create();
        await fixture.Service.AcceptAsync(AccountId, "accept-1", Quest, OfferDialogue, default);

        var immediate = await fixture.Service.ClaimAsync(AccountId, "claim-1", Quest, default);

        Assert.Equal(LobbyOperationStatus.RateLimited, immediate.Status);
    }

    [Fact]
    public async Task StorageFailureMapsToDatabaseUnavailable()
    {
        var fixture = Create();
        fixture.Repository.FailWrites = true;

        var result = await fixture.Service.AcceptAsync(AccountId, "accept-1", Quest, OfferDialogue, default);

        Assert.Equal(LobbyOperationStatus.DatabaseUnavailable, result.Status);
    }

    [Fact]
    public async Task UnknownAccountIsNotFound()
    {
        var fixture = Create();

        Assert.Equal(LobbyOperationStatus.NotFound, (await fixture.Service.GetJournalAsync(99, default)).Status);
        Assert.Equal(LobbyOperationStatus.InvalidRequest, (await fixture.Service.GetJournalAsync(0, default)).Status);
    }
}

/// <summary>账号级任务规则，不经过配置，直接构造定义。</summary>
public sealed class QuestLogTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 8, 0, 0, TimeSpan.Zero);

    private static QuestDefinition Define(
        string questId,
        QuestCategory category = QuestCategory.Main,
        string? prerequisite = null,
        int minLevel = 1,
        int objectives = 1) => new(
        questId,
        category,
        prerequisite,
        minLevel,
        TimeSpan.Zero,
        Enumerable.Range(1, objectives)
            .Select(order => new QuestObjectiveDefinition($"{questId}_o{order}", order, 1, TimeSpan.Zero))
            .ToArray());

    private static QuestCategory? CategoryOf(string questId) =>
        questId.StartsWith("side", StringComparison.Ordinal) ? QuestCategory.Side : QuestCategory.Main;

    [Fact]
    public void OnlyOneMainQuestCanBeOpenAtATime()
    {
        var log = new QuestLog(1, 0, Array.Empty<QuestInstance>());
        Assert.Equal(QuestRuleOutcome.Applied, log.Accept(Define("main_a"), 1, CategoryOf, Now, out _));

        Assert.Equal(QuestRuleOutcome.LimitReached, log.Accept(Define("main_b"), 1, CategoryOf, Now, out var second));
        Assert.Null(second);
    }

    [Fact]
    public void AtMostFiveSideQuestsCanBeOpen()
    {
        var log = new QuestLog(1, 0, Array.Empty<QuestInstance>());
        for (var i = 0; i < QuestLog.MaximumOpenSideQuests; i++)
        {
            Assert.Equal(
                QuestRuleOutcome.Applied,
                log.Accept(Define($"side_{i}", QuestCategory.Side), 1, CategoryOf, Now, out _));
        }

        Assert.Equal(
            QuestRuleOutcome.LimitReached,
            log.Accept(Define("side_extra", QuestCategory.Side), 1, CategoryOf, Now, out _));
    }

    [Fact]
    public void PrerequisiteMustBeClaimedAndLevelMustBeReached()
    {
        var log = new QuestLog(1, 0, Array.Empty<QuestInstance>());

        Assert.Equal(
            QuestRuleOutcome.PrerequisiteMissing,
            log.Accept(Define("main_b", prerequisite: "main_a"), 1, CategoryOf, Now, out _));
        Assert.Equal(
            QuestRuleOutcome.LevelTooLow,
            log.Accept(Define("main_c", minLevel: 3), 2, CategoryOf, Now, out _));
    }

    [Fact]
    public void ObjectivesCompleteStrictlyInOrderAndStatusOnlyMovesForward()
    {
        var definition = Define("main_a", objectives: 2);
        var log = new QuestLog(1, 0, Array.Empty<QuestInstance>());
        log.Accept(definition, 1, CategoryOf, Now, out var instance);

        Assert.Equal(QuestRuleOutcome.NotAvailable, log.RecordObjectiveProgress(definition, "main_a_o2", 1, Now));
        Assert.Equal(QuestRuleOutcome.Applied, log.RecordObjectiveProgress(definition, "main_a_o1", 1, Now));
        Assert.Equal(QuestStatus.InProgress, instance!.Status);
        Assert.Equal(QuestRuleOutcome.NotAvailable, log.RecordObjectiveProgress(definition, "main_a_o1", 1, Now));
        Assert.Equal(QuestRuleOutcome.Applied, log.RecordObjectiveProgress(definition, "main_a_o2", 1, Now));
        Assert.Equal(QuestStatus.ReadyToSubmit, instance.Status);
        Assert.Equal(QuestRuleOutcome.Applied, log.Submit(definition, Now));
        Assert.Equal(QuestRuleOutcome.NotAvailable, log.Submit(definition, Now));
        Assert.Equal(QuestRuleOutcome.Applied, log.Claim(definition, Now));
        Assert.Equal(QuestRuleOutcome.AlreadyClaimed, log.Claim(definition, Now));
        Assert.Equal(QuestStatus.Claimed, instance.Status);
    }

    [Fact]
    public void ARewardPendingMainQuestStillBlocksTheNextMainQuest()
    {
        var first = Define("main_a");
        var log = new QuestLog(1, 0, Array.Empty<QuestInstance>());
        log.Accept(first, 1, CategoryOf, Now, out _);
        log.RecordObjectiveProgress(first, "main_a_o1", 1, Now);
        log.Submit(first, Now);

        Assert.Equal(QuestRuleOutcome.LimitReached, log.CanAccept(Define("main_b"), 1, CategoryOf));
        log.Claim(first, Now);
        Assert.Equal(QuestRuleOutcome.Applied, log.CanAccept(Define("main_b", prerequisite: "main_a"), 1, CategoryOf));
    }

    [Fact]
    public void PersistedInstancesCannotBeLockedOrAvailable()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => QuestInstance.Restore(
            "main_a", "main_a", QuestStatus.Available, Now, Now, null, null, Array.Empty<QuestObjectiveProgress>()));
    }
}
