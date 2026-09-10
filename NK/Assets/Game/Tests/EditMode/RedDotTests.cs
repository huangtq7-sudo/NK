using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Naraka.Core.Application.Bootstrap;
using Naraka.Core.Application.RedDot;
using Naraka.Features.RedDot.Controller;
using NUnit.Framework;

namespace Naraka.P0.Tests
{
    /// <summary>
    /// 红点路径：路径本身就是树，祖先由字符串前缀决定。
    /// </summary>
    public sealed class RedDotPathTests
    {
        [Test]
        public void SelfAndAncestorsWalksFromTheLeafToTheRoot()
        {
            var nodes = RedDotPath.SelfAndAncestors(RedDotPath.UnreadChat("c42"));

            Assert.That(nodes, Is.EqualTo(new[]
            {
                "Lobby/Social/UnreadChat/c42",
                "Lobby/Social/UnreadChat",
                "Lobby/Social",
                "Lobby"
            }));
        }

        [Test]
        public void ARootPathHasNoAncestors()
        {
            Assert.That(RedDotPath.SelfAndAncestors(RedDotPath.Lobby), Is.EqualTo(new[] { "Lobby" }));
        }

        [Test]
        public void AnEmptyPathYieldsNothing()
        {
            Assert.That(RedDotPath.SelfAndAncestors(string.Empty), Is.Empty);
            Assert.That(RedDotPath.SelfAndAncestors(null), Is.Empty);
        }

        [Test]
        public void DescendantCheckRequiresAFullSegmentMatch()
        {
            // "Lobby/SignInExtra" 不是 "Lobby/SignIn" 的后代，尽管它以后者为字符串前缀。
            Assert.That(RedDotPath.IsDescendantOf("Lobby/SignIn/DailyClaim", "Lobby/SignIn"), Is.True);
            Assert.That(RedDotPath.IsDescendantOf("Lobby/SignInExtra", "Lobby/SignIn"), Is.False);
            Assert.That(RedDotPath.IsDescendantOf("Lobby/SignIn", "Lobby/SignIn"), Is.False);
        }

        [Test]
        public void ConversationNodesHangUnderTheUnreadChatNode()
        {
            Assert.That(
                RedDotPath.IsDescendantOf(RedDotPath.UnreadChat("abc"), RedDotPath.SocialUnreadChat),
                Is.True);
            Assert.That(RedDotPath.UnreadChat(string.Empty), Is.EqualTo(RedDotPath.SocialUnreadChat));
        }
    }

    /// <summary>
    /// 红点前缀树。
    ///
    /// 关键性质：叶子有内容时祖先跟着亮；叶子被清空后祖先只在还有其它未读子节点时才继续亮；
    /// 版本号让"清掉之后又来一个新的"能够重新亮起——这是 bool 做不到的。
    /// </summary>
    public sealed class RedDotTreeTests
    {
        [Test]
        public void ALeafWithContentLightsUpEveryAncestor()
        {
            var tree = new RedDotTree();

            tree.SetLeaf(RedDotPath.SignInDailyClaim, true);

            Assert.That(tree.IsActive(RedDotPath.SignInDailyClaim), Is.True);
            Assert.That(tree.IsActive(RedDotPath.SignIn), Is.True);
            Assert.That(tree.IsActive(RedDotPath.Lobby), Is.True);
        }

        [Test]
        public void NothingIsLitBeforeAnyContentIsDeclared()
        {
            var tree = new RedDotTree();

            Assert.That(tree.IsActive(RedDotPath.Lobby), Is.False);
            Assert.That(tree.IsActive(RedDotPath.SignInDailyClaim), Is.False);
            Assert.That(tree.IsActive(RedDotPath.SocialFriendRequest), Is.False);
        }

        [Test]
        public void ClearingTheOnlyLeafTurnsTheAncestorsOff()
        {
            var tree = new RedDotTree();
            tree.SetLeaf(RedDotPath.SignInDailyClaim, true);

            tree.SetLeaf(RedDotPath.SignInDailyClaim, false);

            Assert.That(tree.IsActive(RedDotPath.SignInDailyClaim), Is.False);
            Assert.That(tree.IsActive(RedDotPath.SignIn), Is.False);
            Assert.That(tree.IsActive(RedDotPath.Lobby), Is.False);
        }

        [Test]
        public void ASiblingKeepsTheAncestorLit()
        {
            var tree = new RedDotTree();
            tree.SetLeaf(RedDotPath.SignInDailyClaim, true);
            tree.SetLeaf(RedDotPath.SignInMilestoneClaim, true);

            tree.SetLeaf(RedDotPath.SignInDailyClaim, false);

            Assert.That(tree.IsActive(RedDotPath.SignInDailyClaim), Is.False);
            Assert.That(tree.IsActive(RedDotPath.SignInMilestoneClaim), Is.True);
            Assert.That(tree.IsActive(RedDotPath.SignIn), Is.True);
        }

        [Test]
        public void MarkingSeenClearsTheNodeButLeavesUnrelatedBranchesAlone()
        {
            var tree = new RedDotTree();
            tree.SetLeaf(RedDotPath.SignInDailyClaim, true);
            tree.SetLeaf(RedDotPath.GachaUnshownResult, true);

            tree.MarkSeen(RedDotPath.SignIn);

            Assert.That(tree.IsActive(RedDotPath.SignIn), Is.False);
            Assert.That(tree.IsActive(RedDotPath.SignInDailyClaim), Is.False);
            Assert.That(tree.IsActive(RedDotPath.Gacha), Is.True);
            Assert.That(tree.IsActive(RedDotPath.Lobby), Is.True);
        }

        [Test]
        public void NewContentAfterMarkingSeenLightsTheDotAgain()
        {
            // 这正是版本号而不是 bool 的理由：看过之后再来一个新的必须重新亮起。
            var tree = new RedDotTree();
            tree.SetLeaf(RedDotPath.SignInDailyClaim, true);
            tree.MarkSeen(RedDotPath.SignInDailyClaim);
            Assert.That(tree.IsActive(RedDotPath.SignInDailyClaim), Is.False);

            tree.Bump(RedDotPath.SignInDailyClaim);

            Assert.That(tree.IsActive(RedDotPath.SignInDailyClaim), Is.True);
            Assert.That(tree.IsActive(RedDotPath.Lobby), Is.True);
        }

        [Test]
        public void ReadingOneConversationLeavesTheOtherUnread()
        {
            var tree = new RedDotTree();
            tree.SetLeaf(RedDotPath.UnreadChat("alice"), true);
            tree.SetLeaf(RedDotPath.UnreadChat("bob"), true);

            tree.MarkSeen(RedDotPath.UnreadChat("alice"));

            Assert.That(tree.IsActive(RedDotPath.UnreadChat("alice")), Is.False);
            Assert.That(tree.IsActive(RedDotPath.UnreadChat("bob")), Is.True);
            Assert.That(tree.IsActive(RedDotPath.SocialUnreadChat), Is.True);
            Assert.That(tree.IsActive(RedDotPath.Social), Is.True);
        }

        [Test]
        public void ReadingTheLastConversationTurnsTheSocialEntryOff()
        {
            var tree = new RedDotTree();
            tree.SetLeaf(RedDotPath.UnreadChat("alice"), true);

            tree.MarkSeen(RedDotPath.UnreadChat("alice"));

            Assert.That(tree.IsActive(RedDotPath.SocialUnreadChat), Is.False);
            Assert.That(tree.IsActive(RedDotPath.Social), Is.False);
            Assert.That(tree.IsActive(RedDotPath.Lobby), Is.False);
        }

        [Test]
        public void RemovingAConversationDropsItsContributionToTheAncestors()
        {
            var tree = new RedDotTree();
            tree.SetLeaf(RedDotPath.UnreadChat("alice"), true);

            tree.Remove(RedDotPath.UnreadChat("alice"));

            Assert.That(tree.IsActive(RedDotPath.SocialUnreadChat), Is.False);
            Assert.That(tree.IsActive(RedDotPath.Social), Is.False);
        }

        [Test]
        public void RestoringPersistedVersionsReproducesTheSeenState()
        {
            var tree = new RedDotTree();

            // 版本 5、已看到 5：看过了，不亮。
            tree.Restore(RedDotPath.SignInDailyClaim, 5, 5);
            Assert.That(tree.IsActive(RedDotPath.SignInDailyClaim), Is.False);

            // 版本 6、已看到 5：有新内容，亮。
            tree.Restore(RedDotPath.GachaUnshownResult, 6, 5);
            Assert.That(tree.IsActive(RedDotPath.GachaUnshownResult), Is.True);
            Assert.That(tree.IsActive(RedDotPath.Gacha), Is.True);
        }

        [Test]
        public void SettingTheSameStateTwiceIsANoOp()
        {
            var tree = new RedDotTree();

            Assert.That(tree.SetLeaf(RedDotPath.SignInDailyClaim, true), Is.True);
            Assert.That(tree.SetLeaf(RedDotPath.SignInDailyClaim, true), Is.False);
            Assert.That(tree.SetLeaf(RedDotPath.SignInDailyClaim, false), Is.True);
            Assert.That(tree.SetLeaf(RedDotPath.SignInDailyClaim, false), Is.False);
        }

        [Test]
        public void ResetClearsEverything()
        {
            var tree = new RedDotTree();
            tree.SetLeaf(RedDotPath.SignInDailyClaim, true);

            tree.Reset();

            Assert.That(tree.IsActive(RedDotPath.SignInDailyClaim), Is.False);
            Assert.That(tree.IsActive(RedDotPath.Lobby), Is.False);
        }
    }

    internal sealed class FakeRedDotGateway : IRedDotGateway
    {
        public List<RedDotStateRecord> Records { get; } = new List<RedDotStateRecord>();

        public List<(string Path, long SeenVersion)> Saved { get; } =
            new List<(string, long)>();

        public int LoadCount { get; private set; }

        public Exception ThrowOnSave { get; set; }

        public UniTask<IReadOnlyList<RedDotStateRecord>> LoadAsync(CancellationToken cancellationToken)
        {
            LoadCount++;
            return UniTask.FromResult<IReadOnlyList<RedDotStateRecord>>(Records.ToArray());
        }

        public UniTask SaveSeenAsync(string path, long seenVersion, CancellationToken cancellationToken)
        {
            if (ThrowOnSave != null)
            {
                return UniTask.FromException(ThrowOnSave);
            }

            Saved.Add((path, seenVersion));
            return UniTask.CompletedTask;
        }
    }

    /// <summary>
    /// 红点控制器：业务模块通过事件总线声明来源，界面只订阅聚合后的只读状态。
    /// </summary>
    public sealed class RedDotControllerTests
    {
        private static (RedDotController RedDot, TestEventBus Bus, FakeRedDotGateway Gateway) Create(
            IServerCapabilities capabilities = null)
        {
            var bus = new TestEventBus();
            var gateway = new FakeRedDotGateway();
            var redDot = new RedDotController(
                bus, gateway, capabilities ?? LobbyTestCapabilities.Full());
            return (redDot, bus, gateway);
        }

        [Test]
        public void NoDotIsShownBeforeAnySourceDeclaresContent()
        {
            var (redDot, _, _) = Create();

            Assert.That(redDot.Current.ActivePaths, Is.Empty);
            Assert.That(redDot.Current.IsActive(RedDotPath.Lobby), Is.False);
        }

        [Test]
        public void ASourceEventLightsTheEntryAndItsAncestors()
        {
            var (redDot, bus, _) = Create();

            bus.Publish(new RedDotSourceChanged(RedDotPath.SignInDailyClaim, true));

            Assert.That(redDot.Current.IsActive(RedDotPath.SignInDailyClaim), Is.True);
            Assert.That(redDot.Current.HasActiveDescendant(RedDotPath.SignIn), Is.True);
            Assert.That(redDot.Current.HasActiveDescendant(RedDotPath.Gacha), Is.False);
        }

        [Test]
        public void ClearingTheSourceTurnsTheDotOff()
        {
            var (redDot, bus, _) = Create();
            bus.Publish(new RedDotSourceChanged(RedDotPath.SignInDailyClaim, true));

            bus.Publish(new RedDotSourceChanged(RedDotPath.SignInDailyClaim, false));

            Assert.That(redDot.Current.IsActive(RedDotPath.SignInDailyClaim), Is.False);
            Assert.That(redDot.Current.HasActiveDescendant(RedDotPath.SignIn), Is.False);
        }

        [Test]
        public void MarkingSeenPersistsTheVersionToTheServer()
        {
            var (redDot, bus, gateway) = Create();
            bus.Publish(new RedDotSourceChanged(RedDotPath.SignInDailyClaim, true));

            redDot.MarkSeen(RedDotPath.SignInDailyClaim);

            Assert.That(redDot.Current.IsActive(RedDotPath.SignInDailyClaim), Is.False);
            Assert.That(gateway.Saved.Count, Is.EqualTo(1));
            Assert.That(gateway.Saved[0].Path, Is.EqualTo(RedDotPath.SignInDailyClaim));
            Assert.That(gateway.Saved[0].SeenVersion, Is.GreaterThan(0));
        }

        [Test]
        public void CompatibilityModeNeverCallsTheServer()
        {
            var (redDot, bus, gateway) = Create(LobbyTestCapabilities.LegacyCloud());

            redDot.ReloadAsync(CancellationToken.None).Forget();
            bus.Publish(new RedDotSourceChanged(RedDotPath.SignInDailyClaim, true));
            redDot.MarkSeen(RedDotPath.SignInDailyClaim);

            Assert.That(gateway.LoadCount, Is.Zero);
            Assert.That(gateway.Saved, Is.Empty);
        }

        [Test]
        public void CompatibilityModeStillAggregatesWithinTheSession()
        {
            // 旧云端不支持持久化，但本次会话里的红点仍然要能亮、能熄。
            var (redDot, bus, _) = Create(LobbyTestCapabilities.LegacyCloud());

            bus.Publish(new RedDotSourceChanged(RedDotPath.SignInDailyClaim, true));

            Assert.That(redDot.Current.HasActiveDescendant(RedDotPath.SignIn), Is.True);
        }

        [Test]
        public void ReloadRestoresSeenVersionsAcrossLogins()
        {
            var (redDot, _, gateway) = Create();
            gateway.Records.Add(new RedDotStateRecord(RedDotPath.SignInDailyClaim, 3, 3));
            gateway.Records.Add(new RedDotStateRecord(RedDotPath.GachaUnshownResult, 4, 1));

            redDot.ReloadAsync(CancellationToken.None).Forget();

            Assert.That(redDot.Current.IsActive(RedDotPath.SignInDailyClaim), Is.False);
            Assert.That(redDot.Current.IsActive(RedDotPath.GachaUnshownResult), Is.True);
        }

        [Test]
        public void APersistenceFailureNeverThrowsIntoTheLobby()
        {
            var (redDot, bus, gateway) = Create();
            gateway.ThrowOnSave = new InvalidOperationException("offline");
            bus.Publish(new RedDotSourceChanged(RedDotPath.SignInDailyClaim, true));

            Assert.DoesNotThrow(() => redDot.MarkSeen(RedDotPath.SignInDailyClaim));
            Assert.That(redDot.Current.IsActive(RedDotPath.SignInDailyClaim), Is.False);
        }

        [Test]
        public void RemovingAConversationDropsItsDot()
        {
            var (redDot, bus, _) = Create();
            bus.Publish(new RedDotSourceChanged(RedDotPath.UnreadChat("alice"), true));

            bus.Publish(new RedDotSourceRemoved(RedDotPath.UnreadChat("alice")));

            Assert.That(redDot.Current.HasActiveDescendant(RedDotPath.Social), Is.False);
        }

        [Test]
        public void SwitchingAccountsClearsEveryDot()
        {
            var (redDot, bus, _) = Create();
            bus.Publish(new RedDotSourceChanged(RedDotPath.SignInDailyClaim, true));

            redDot.Reset();

            Assert.That(redDot.Current.ActivePaths, Is.Empty);
        }

        [Test]
        public void DisposingStopsListeningToTheBus()
        {
            var (redDot, bus, _) = Create();

            redDot.Dispose();

            Assert.DoesNotThrow(() => bus.Publish(new RedDotSourceChanged(RedDotPath.SignInDailyClaim, true)));
        }
    }
}
