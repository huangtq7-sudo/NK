using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using Naraka.Features.Account.Controller;
using Naraka.Features.Expedition.Controller;
using Naraka.Features.Quest.Controller;
using Naraka.Features.Quest.Model;
using Naraka.Infrastructure.Network;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Naraka.P0.Tests
{
    /// <summary>
    /// P4.1 真实链路冒烟：Unity 客户端的 LegacyNetworkAdapter → 本机隔离端口 Host → 本机 MySQL。
    /// 默认跳过；设置 NARAKA_RUN_QUEST_LIVE_SMOKE=1 与 NARAKA_QUEST_LIVE_PORT 后才运行。
    /// 测试账号以 p4_live_ 开头，结束后由 <c>Naraka.Server.QuestSmoke --cleanup</c> 删除。
    /// </summary>
    public sealed class QuestLiveSmokeTests
    {
        [UnityTest]
        public IEnumerator FirstMainQuestCompletesAgainstARealHost() => UniTask.ToCoroutine(async () =>
        {
            if (!string.Equals(Environment.GetEnvironmentVariable("NARAKA_RUN_QUEST_LIVE_SMOKE"), "1", StringComparison.Ordinal))
            {
                Assert.Ignore("Set NARAKA_RUN_QUEST_LIVE_SMOKE=1 and NARAKA_QUEST_LIVE_PORT to run the live quest smoke.");
            }

            var port = int.Parse(Environment.GetEnvironmentVariable("NARAKA_QUEST_LIVE_PORT") ?? "0");
            Assert.That(port, Is.GreaterThan(0));
            var username = "p4_live_" + Guid.NewGuid().ToString("N").Substring(0, 12);
            var password = "Live-" + Guid.NewGuid().ToString("N");
            TestContext.WriteLine("live account: " + username);
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(UnityEngine.Application.dataPath, "..", "..", "artifacts", "p41", "live-username.txt"),
                username);

            using (var adapter = new LegacyNetworkAdapter("127.0.0.1", port))
            {
                Assert.That((await adapter.RegisterAsync(username, password, CancellationToken.None)).Status,
                    Is.EqualTo(AccountRegistrationStatus.Success));
                Assert.That((await adapter.LoginAsync(username, password, CancellationToken.None)).Status,
                    Is.EqualTo(AccountLoginStatus.Success));
                IExpeditionGateway expeditions = adapter;
                Assert.That((await expeditions.StartAsync(NewId(), CancellationToken.None)).Status,
                    Is.EqualTo(ExpeditionOperationStatus.Success));

                IQuestGateway quests = adapter;
                var journal = await quests.GetJournalAsync(NewId(), CancellationToken.None);
                Assert.That(journal.Status, Is.EqualTo(QuestOperationStatus.Success));
                Assert.That(journal.Journal.IsAvailable("main_c1_01"), Is.True);

                var accepted = await quests.AcceptAsync(NewId(), "main_c1_01", "dlg_c1_01_offer", CancellationToken.None);
                Assert.That(accepted.Journal.Find("main_c1_01").Status, Is.EqualTo(QuestProgressStatus.Accepted));

                var early = await quests.CompleteDialogueAsync(
                    NewId(), "main_c1_01", "main_c1_01_o1", "dlg_c1_01_yunsheng", CancellationToken.None);
                Assert.That(early.Status, Is.EqualTo(QuestOperationStatus.RateLimited), "服务端最短时长 3 秒。");

                await UniTask.Delay(TimeSpan.FromSeconds(3.5));
                var talked = await quests.CompleteDialogueAsync(
                    NewId(), "main_c1_01", "main_c1_01_o1", "dlg_c1_01_yunsheng", CancellationToken.None);
                Assert.That(talked.Journal.Find("main_c1_01").Status, Is.EqualTo(QuestProgressStatus.ReadyToSubmit));

                await UniTask.Delay(TimeSpan.FromSeconds(3.5));
                var submitted = await quests.SubmitAsync(NewId(), "main_c1_01", "dlg_c1_01_submit", CancellationToken.None);
                Assert.That(submitted.Journal.Find("main_c1_01").Status, Is.EqualTo(QuestProgressStatus.RewardPending));

                await UniTask.Delay(TimeSpan.FromSeconds(0.5));
                var claimId = NewId();
                var claimed = await quests.ClaimAsync(claimId, "main_c1_01", CancellationToken.None);
                Assert.That(claimed.Status, Is.EqualTo(QuestOperationStatus.Success));
                Assert.That(claimed.Claim.AccountXpAfter, Is.EqualTo(100));
                Assert.That(claimed.Journal.AccountLevel, Is.EqualTo(2));

                await UniTask.Delay(TimeSpan.FromSeconds(0.5));
                var replay = await quests.ClaimAsync(claimId, "main_c1_01", CancellationToken.None);
                Assert.That(replay.IsReplay, Is.True);
                await UniTask.Delay(TimeSpan.FromSeconds(0.5));
                var duplicate = await quests.ClaimAsync(NewId(), "main_c1_01", CancellationToken.None);
                Assert.That(duplicate.Status, Is.EqualTo(QuestOperationStatus.AlreadyClaimed));
            }

            // 断线重连后由服务端恢复：任务已领取、账号等级 2。
            using (var relogin = new LegacyNetworkAdapter("127.0.0.1", port))
            {
                Assert.That((await relogin.LoginAsync(username, password, CancellationToken.None)).Status,
                    Is.EqualTo(AccountLoginStatus.Success));
                var restored = await ((IQuestGateway)relogin).GetJournalAsync(NewId(), CancellationToken.None);
                Assert.That(restored.Journal.Find("main_c1_01").Status, Is.EqualTo(QuestProgressStatus.Claimed));
                Assert.That(restored.Journal.AccountXp, Is.EqualTo(100));
            }
        });

        private static string NewId() => Guid.NewGuid().ToString("N");
    }
}
