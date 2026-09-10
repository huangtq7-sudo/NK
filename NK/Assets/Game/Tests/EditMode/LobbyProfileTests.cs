using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Naraka.Config;
using Naraka.Features.Lobby.Controller;
using Naraka.Features.Lobby.Model;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Naraka.P0.Tests
{
    internal sealed class FakeLobbyProfileGateway : ILobbyProfileGateway
    {
        private readonly UniTaskCompletionSource<LobbyProfileResult> _pending =
            new UniTaskCompletionSource<LobbyProfileResult>();

        public FakeLobbyProfileGateway()
        {
            Result = LobbyProfileResult.Success(CreateSnapshot("avatar_01", "frame_white"));
        }

        public int LoadCount { get; private set; }

        public int SaveCount { get; private set; }

        public List<(string AvatarId, string FrameId)> SavedAppearances { get; } =
            new List<(string AvatarId, string FrameId)>();

        public bool CompleteImmediately { get; set; } = true;

        public LobbyProfileResult Result { get; set; }

        public Exception ThrowWith { get; set; }

        public static LobbyProfileSnapshot CreateSnapshot(string avatarId, string frameId)
        {
            LobbyProfileSnapshot.TryCreate(
                avatarId, frameId, "hero_gu_chenyue", "weapon_longsword", "pet_lingyu",
                0, 1, 0, 1000, 1000, 1000, out var snapshot);
            return snapshot;
        }

        public UniTask<LobbyProfileResult> RequestProfileAsync(CancellationToken cancellationToken)
        {
            LoadCount++;
            return Respond(cancellationToken);
        }

        public UniTask<LobbyProfileResult> SetAppearanceAsync(
            string avatarId,
            string avatarFrameId,
            CancellationToken cancellationToken)
        {
            SaveCount++;
            SavedAppearances.Add((avatarId, avatarFrameId));
            if (Result.IsSuccess)
            {
                Result = LobbyProfileResult.Success(CreateSnapshot(avatarId, avatarFrameId));
            }

            return Respond(cancellationToken);
        }

        private UniTask<LobbyProfileResult> Respond(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return UniTask.FromCanceled<LobbyProfileResult>(cancellationToken);
            }

            if (ThrowWith != null)
            {
                return UniTask.FromException<LobbyProfileResult>(ThrowWith);
            }

            return CompleteImmediately ? UniTask.FromResult(Result) : _pending.Task;
        }
    }

    /// <summary>
    /// P1.1-B 客户端：账号资料读取、头像持久化与稳定 ID 的行为。
    /// </summary>
    public sealed class LobbyProfileTests
    {
        private static LobbyController CreateController(
            FakeLobbyProfileGateway profiles,
            IServerCapabilitiesFactory capabilities = null)
        {
            LobbyAccountSnapshot.TryCreate(1, 1000, 1000, 1000, out var summary);
            return new LobbyController(
                new LobbyModel(),
                new FakeLobbySceneGateway(),
                "Map1",
                new FakeLobbyAccountGateway { Result = LobbyAccountSummaryResult.Success(summary) },
                profiles,
                capabilities == null
                    ? LobbyTestCapabilities.Full()
                    : capabilities.Create());
        }

        public interface IServerCapabilitiesFactory
        {
            Naraka.Core.Application.Bootstrap.IServerCapabilities Create();
        }

        private sealed class LegacyCloudCapabilities : IServerCapabilitiesFactory
        {
            public Naraka.Core.Application.Bootstrap.IServerCapabilities Create() =>
                LobbyTestCapabilities.LegacyCloud();
        }

        [Test]
        public void EnteringTheLobbyLoadsTheProfile()
        {
            var profiles = new FakeLobbyProfileGateway();
            var controller = CreateController(profiles);

            controller.Enter("player-one", 42);

            Assert.That(profiles.LoadCount, Is.EqualTo(1));
            Assert.That(controller.Current.HasProfile, Is.True);
            Assert.That(controller.Current.SelectedAvatarId, Is.EqualTo("avatar_01"));
            Assert.That(controller.Current.SelectedAvatarFrameId, Is.EqualTo("frame_white"));
        }

        [Test]
        public void ProfileIsNotRequestedFromAServerThatDoesNotSupportIt()
        {
            var profiles = new FakeLobbyProfileGateway();
            var controller = CreateController(profiles, new LegacyCloudCapabilities());

            controller.Enter("player-one", 42);

            // 旧云端不认识协议 21，客户端连发都不能发。
            Assert.That(profiles.LoadCount, Is.Zero);
            Assert.That(controller.Current.HasProfile, Is.False);
        }

        [Test]
        public void AnUnsupportedProfileLinkExplainsItselfInsteadOfFailingSilently()
        {
            // 资料拿不到会连带让外观面板的全部单元格失效。
            // 如果这里静默返回，玩家点开面板、点了头像却毫无反应，界面上也没有任何解释。
            var profiles = new FakeLobbyProfileGateway();
            var controller = CreateController(profiles, new LegacyCloudCapabilities());

            controller.Enter("player-one", 42);

            Assert.That(controller.Current.StatusMessage, Does.Contain("服务器功能尚未升级"));
            Assert.That(
                controller.Current.StatusSource,
                Is.EqualTo(Naraka.Features.Lobby.Controller.LobbyStatusSource.Profile));
        }

        [Test]
        public void AppearanceCannotBeChangedWithoutProfileSoNoRequestIsSent()
        {
            // 资料未知时提交会把玩家原本的头像框意外改成默认框，
            // 因此拒绝提交是对的；需要保证的是它真的一个请求都不发。
            var profiles = new FakeLobbyProfileGateway();
            var controller = CreateController(profiles, new LegacyCloudCapabilities());
            controller.Enter("player-one", 42);

            controller.SelectAvatar("avatar_09");
            controller.SelectFrame("frame_gold");

            Assert.That(profiles.SaveCount, Is.Zero);
        }

        [Test]
        public void SelectingAnAvatarSendsTheStableIdAndAppliesTheServerAnswer()
        {
            var profiles = new FakeLobbyProfileGateway();
            var controller = CreateController(profiles);
            controller.Enter("player-one", 42);

            controller.SelectAvatar("avatar_09");

            Assert.That(profiles.SaveCount, Is.EqualTo(1));
            Assert.That(profiles.SavedAppearances[0].AvatarId, Is.EqualTo("avatar_09"));
            // 头像框保持不变，一次修改不会顺手改掉另一项。
            Assert.That(profiles.SavedAppearances[0].FrameId, Is.EqualTo("frame_white"));
            Assert.That(controller.Current.SelectedAvatarId, Is.EqualTo("avatar_09"));
        }

        [Test]
        public void SelectingTheSameAvatarSendsNothing()
        {
            var profiles = new FakeLobbyProfileGateway();
            var controller = CreateController(profiles);
            controller.Enter("player-one", 42);

            controller.SelectAvatar("avatar_01");

            Assert.That(profiles.SaveCount, Is.Zero);
        }

        [Test]
        public void EmptyIdIsNeverSent()
        {
            var profiles = new FakeLobbyProfileGateway();
            var controller = CreateController(profiles);
            controller.Enter("player-one", 42);

            controller.SelectAvatar(string.Empty);
            controller.SelectFrame(null);

            Assert.That(profiles.SaveCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator SecondClickIsIgnoredWhileTheFirstRequestIsStillInFlight() =>
            UniTask.ToCoroutine(async () =>
            {
                var profiles = new FakeLobbyProfileGateway();
                var controller = CreateController(profiles);
                controller.Enter("player-one", 42);
                profiles.CompleteImmediately = false;

                controller.SelectAvatar("avatar_09");
                await UniTask.Yield();
                controller.SelectAvatar("avatar_10");

                // 请求在途时按钮被禁用，重复点击不能产生第二次写请求。
                Assert.That(profiles.SaveCount, Is.EqualTo(1));
                Assert.That(controller.Current.IsAppearanceSaving, Is.True);
            });

        [Test]
        public void RejectedAppearanceKeepsThePreviousSelection()
        {
            var profiles = new FakeLobbyProfileGateway();
            var controller = CreateController(profiles);
            controller.Enter("player-one", 42);
            profiles.Result = LobbyProfileResult.Failed(LobbyOperationStatus.InvalidRequest);

            controller.SelectAvatar("avatar_09");

            Assert.That(controller.Current.SelectedAvatarId, Is.EqualTo("avatar_01"));
            Assert.That(controller.Current.IsAppearanceSaving, Is.False);
            Assert.That(controller.Current.StatusMessage, Is.Not.Empty);
        }

        [Test]
        public void TransportFailureKeepsThePreviousProfile()
        {
            var profiles = new FakeLobbyProfileGateway();
            var controller = CreateController(profiles);
            controller.Enter("player-one", 42);
            profiles.ThrowWith = new InvalidOperationException("network down");

            controller.SelectAvatar("avatar_09");

            Assert.That(controller.Current.HasProfile, Is.True);
            Assert.That(controller.Current.SelectedAvatarId, Is.EqualTo("avatar_01"));
            Assert.That(controller.Current.StatusMessage, Does.Contain("无法连接服务器"));
        }

        [Test]
        public void SwitchingAccountClearsTheProfile()
        {
            var profiles = new FakeLobbyProfileGateway();
            var controller = CreateController(profiles);
            controller.Enter("player-one", 42);
            profiles.ThrowWith = new InvalidOperationException("network down");

            controller.Enter("player-two", 43);

            Assert.That(controller.Current.HasProfile, Is.False);
            Assert.That(controller.Current.SelectedAvatarId, Is.Empty);
        }

        [Test]
        public void SnapshotRejectsInvalidValues()
        {
            Assert.That(
                LobbyProfileSnapshot.TryCreate(
                    string.Empty, "frame_white", "hero", "weapon", "pet", 0, 1, 0, 0, 0, 0, out _),
                Is.False,
                "空头像 ID 必须被拒绝。");
            Assert.That(
                LobbyProfileSnapshot.TryCreate(
                    "avatar_01", "frame_white", "hero", "weapon", "pet", 0, 0, 0, 0, 0, 0, out _),
                Is.False,
                "账号等级最小为 1。");
            Assert.That(
                LobbyProfileSnapshot.TryCreate(
                    "avatar_01", "frame_white", "hero", "weapon", "pet", 0, 1, 0, -1, 0, 0, out _),
                Is.False,
                "负余额必须被拒绝。");
            Assert.That(
                LobbyProfileSnapshot.TryCreate(
                    "avatar_01", "frame_white", "hero", "weapon", "pet", -1, 1, 0, 0, 0, 0, out _),
                Is.False,
                "负经验必须被拒绝。");
            Assert.That(
                LobbyProfileSnapshot.TryCreate(
                    "avatar_01", "frame_white", "hero", "weapon", "pet", 0, 1, 0, 0, 0, 0, out _),
                Is.True);
        }

        [Test]
        public void OperationStatusValuesMatchTheServerContract()
        {
            // 数值是线级契约，改动会让新客户端把服务端的错误码解释成另一种含义。
            Assert.That((int)LobbyOperationStatus.Success, Is.EqualTo(0));
            Assert.That((int)LobbyOperationStatus.Unauthenticated, Is.EqualTo(1));
            Assert.That((int)LobbyOperationStatus.InvalidRequest, Is.EqualTo(2));
            Assert.That((int)LobbyOperationStatus.NotFound, Is.EqualTo(3));
            Assert.That((int)LobbyOperationStatus.DatabaseUnavailable, Is.EqualTo(4));
            Assert.That((int)LobbyOperationStatus.InternalError, Is.EqualTo(5));
            Assert.That((int)LobbyOperationStatus.Conflict, Is.EqualTo(14));
            // 这两个只在客户端产生，必须远离服务端的编号空间。
            Assert.That((int)LobbyOperationStatus.TransportFailure, Is.EqualTo(100));
            Assert.That((int)LobbyOperationStatus.ServerCapabilityMissing, Is.EqualTo(101));
        }

        [Test]
        public void EveryStatusHasAPlayerReadableMessage()
        {
            foreach (LobbyOperationStatus status in Enum.GetValues(typeof(LobbyOperationStatus)))
            {
                var message = LobbyOperationMessages.Describe(status);
                if (status == LobbyOperationStatus.Success)
                {
                    Assert.That(message, Is.Empty);
                }
                else
                {
                    Assert.That(message, Is.Not.Empty, $"状态 {status} 缺少提示文案。");
                }
            }
        }

        [Test]
        public void CapabilityNamesAreStableWireStrings()
        {
            Assert.That(NarakaServerCapabilities.AccountProfile, Is.EqualTo("account.profile"));
            Assert.That(NarakaServerCapabilities.LobbyAccountSummary, Is.EqualTo("lobby.account.summary"));
        }
    }
}
