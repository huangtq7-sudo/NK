using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Naraka.Features.Expedition.Controller;
using Naraka.Features.Expedition.Model;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Naraka.P0.Tests
{
    internal sealed class FakeExpeditionGateway : IExpeditionGateway
    {
        private readonly Queue<ExpeditionGatewayResult> _active =
            new Queue<ExpeditionGatewayResult>();
        private readonly Queue<ExpeditionGatewayResult> _start =
            new Queue<ExpeditionGatewayResult>();
        private readonly Queue<ExpeditionGatewayResult> _death =
            new Queue<ExpeditionGatewayResult>();
        private readonly Queue<ExpeditionGatewayResult> _return =
            new Queue<ExpeditionGatewayResult>();

        public int CallCount { get; private set; }

        public List<string> StartRequestIds { get; } = new List<string>();

        public void QueueActive(ExpeditionGatewayResult result) => _active.Enqueue(result);

        public void QueueStart(ExpeditionGatewayResult result) => _start.Enqueue(result);

        public void QueueDeath(ExpeditionGatewayResult result) => _death.Enqueue(result);

        public void QueueReturn(ExpeditionGatewayResult result) => _return.Enqueue(result);

        public UniTask<ExpeditionGatewayResult> GetActiveAsync(
            string requestId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return UniTask.FromResult(_active.Dequeue());
        }

        public UniTask<ExpeditionGatewayResult> StartAsync(
            string requestId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            StartRequestIds.Add(requestId);
            return UniTask.FromResult(_start.Dequeue());
        }

        public UniTask<ExpeditionGatewayResult> RecordDeathAsync(
            string expeditionId,
            string requestId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return UniTask.FromResult(_death.Dequeue());
        }

        public UniTask<ExpeditionGatewayResult> ReturnToLobbyAsync(
            string expeditionId,
            string requestId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return UniTask.FromResult(_return.Dequeue());
        }
    }

    internal sealed class SequenceExpeditionRequestIds : IExpeditionRequestIdSource
    {
        private int _next;

        public string NewId()
        {
            _next++;
            return "request-" + _next;
        }
    }

    public sealed class ExpeditionControllerTests
    {
        [UnityTest]
        public System.Collections.IEnumerator MissingCapabilityNeverSendsAnExpeditionRequest() =>
            UniTask.ToCoroutine(async () =>
            {
                var gateway = new FakeExpeditionGateway();
                using (var controller = Create(gateway, LobbyTestCapabilities.LegacyCloud()))
                {
                    var status = await controller.StartOrResumeAsync(CancellationToken.None);

                    Assert.That(status, Is.EqualTo(ExpeditionOperationStatus.ServerCapabilityMissing));
                    Assert.That(gateway.CallCount, Is.Zero);
                    Assert.That(controller.Current.StatusMessage, Does.Contain("服务器尚未部署"));
                }
            });

        [UnityTest]
        public System.Collections.IEnumerator StartingCreatesAServiceAuthoritativeActiveSnapshot() =>
            UniTask.ToCoroutine(async () =>
            {
                var gateway = new FakeExpeditionGateway();
                gateway.QueueActive(ExpeditionGatewayResult.Failed(ExpeditionOperationStatus.NotFound));
                gateway.QueueStart(ExpeditionGatewayResult.Success(ActiveSnapshot(
                    new ExpeditionAsset(
                        ExpeditionAssetKind.Item, "material_wolf_claw", 2,
                        ExpeditionAssetSource.MonsterDrop))));

                using (var controller = Create(gateway))
                {
                    var status = await controller.StartOrResumeAsync(CancellationToken.None);

                    Assert.That(status, Is.EqualTo(ExpeditionOperationStatus.Success));
                    Assert.That(controller.Current.HasActive, Is.True);
                    Assert.That(controller.Current.Active.ExpeditionId, Is.EqualTo("expedition-1"));
                    Assert.That(controller.Current.Active.TemporaryAssets.Count, Is.EqualTo(1));
                    Assert.That(gateway.StartRequestIds, Has.Count.EqualTo(1));
                }
            });

        [UnityTest]
        public System.Collections.IEnumerator TransportRetryReusesTheStartRequestId() =>
            UniTask.ToCoroutine(async () =>
            {
                var gateway = new FakeExpeditionGateway();
                gateway.QueueActive(ExpeditionGatewayResult.Failed(ExpeditionOperationStatus.NotFound));
                gateway.QueueStart(ExpeditionGatewayResult.Failed(
                    ExpeditionOperationStatus.TransportFailure));
                gateway.QueueActive(ExpeditionGatewayResult.Failed(ExpeditionOperationStatus.NotFound));
                gateway.QueueStart(ExpeditionGatewayResult.Success(ActiveSnapshot()));

                using (var controller = Create(gateway))
                {
                    Assert.That(
                        await controller.StartOrResumeAsync(CancellationToken.None),
                        Is.EqualTo(ExpeditionOperationStatus.TransportFailure));
                    Assert.That(
                        await controller.StartOrResumeAsync(CancellationToken.None),
                        Is.EqualTo(ExpeditionOperationStatus.Success));

                    Assert.That(gateway.StartRequestIds, Has.Count.EqualTo(2));
                    Assert.That(gateway.StartRequestIds[1], Is.EqualTo(gateway.StartRequestIds[0]),
                        "传输结果未知时必须复用RequestId，避免创建两次远征。");
                }
            });

        [UnityTest]
        public System.Collections.IEnumerator DeathClearsTemporaryAssetsAndPublishesTheSummary() =>
            UniTask.ToCoroutine(async () =>
            {
                var lost = new ExpeditionAsset(
                    ExpeditionAssetKind.Currency, "copper", 15,
                    ExpeditionAssetSource.MonsterDrop);
                var gateway = new FakeExpeditionGateway();
                gateway.QueueActive(ExpeditionGatewayResult.Success(ActiveSnapshot(lost)));
                gateway.QueueDeath(ExpeditionGatewayResult.Success(
                    ActiveSnapshot(deathCount: 1),
                    new ExpeditionDeathSummary("expedition-1", 1700000001000, new[] { lost }, 1)));

                using (var controller = Create(gateway))
                {
                    await controller.LoadActiveAsync(CancellationToken.None);
                    var status = await controller.RecordDeathAsync(CancellationToken.None);

                    Assert.That(status, Is.EqualTo(ExpeditionOperationStatus.Success));
                    Assert.That(controller.Current.Active.TemporaryAssets, Is.Empty);
                    Assert.That(controller.Current.Active.DeathCount, Is.EqualTo(1));
                    Assert.That(controller.Current.LastDeath.ClearedAssets.Count, Is.EqualTo(1));
                }
            });

        [UnityTest]
        public System.Collections.IEnumerator ReturnToLobbyClearsActiveAndKeepsSettlementSummary() =>
            UniTask.ToCoroutine(async () =>
            {
                var kept = new ExpeditionAsset(
                    ExpeditionAssetKind.Item, "material_wolf_claw", 2,
                    ExpeditionAssetSource.MonsterDrop);
                var gateway = new FakeExpeditionGateway();
                gateway.QueueActive(ExpeditionGatewayResult.Success(ActiveSnapshot(kept)));
                gateway.QueueReturn(ExpeditionGatewayResult.Success(
                    settlement: new ExpeditionSettlementSummary(
                        "expedition-1", "return-1",
                        ExpeditionSettlementReason.ReturnedToLobby,
                        1700000002000, new[] { kept }, 0)));

                using (var controller = Create(gateway))
                {
                    await controller.LoadActiveAsync(CancellationToken.None);
                    var status = await controller.SettleReturnToLobbyAsync(CancellationToken.None);

                    Assert.That(status, Is.EqualTo(ExpeditionOperationStatus.Success));
                    Assert.That(controller.Current.HasActive, Is.False);
                    Assert.That(controller.Current.Active, Is.Null);
                    Assert.That(controller.Current.LastSettlement.Assets.Count, Is.EqualTo(1));
                }
            });

        private static ExpeditionController Create(
            FakeExpeditionGateway gateway,
            Naraka.Core.Application.Bootstrap.IServerCapabilities capabilities = null) =>
            new ExpeditionController(
                new ExpeditionModel(), gateway, capabilities ?? LobbyTestCapabilities.Full(),
                new SequenceExpeditionRequestIds());

        private static ExpeditionSnapshot ActiveSnapshot(
            ExpeditionAsset asset = default(ExpeditionAsset),
            int deathCount = 0)
        {
            var assets = asset.IsValid
                ? new[] { asset }
                : Array.Empty<ExpeditionAsset>();
            Assert.That(ExpeditionSnapshot.TryCreate(
                "expedition-1", "map-01", 1700000000000,
                ExpeditionState.Active, assets, deathCount, out var snapshot), Is.True);
            return snapshot;
        }
    }
}
