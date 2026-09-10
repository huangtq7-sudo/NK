using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Naraka.Core.Application.Bootstrap;
using Naraka.Features.Inventory.Controller;
using Naraka.Features.Inventory.Model;
using Naraka.Features.Lobby.Controller;
using Naraka.Features.Lobby.Model;
using NUnit.Framework;

namespace Naraka.P0.Tests
{
    internal sealed class FakeInventoryGateway : IInventoryGateway
    {
        public List<(InventoryOperation Operation, string ItemId, long Quantity, string SlotKind, int SlotIndex,
            IReadOnlyList<string> Order)> Calls { get; } =
            new List<(InventoryOperation, string, long, string, int, IReadOnlyList<string>)>();

        public int LoadCount { get; private set; }

        public InventoryResult Result { get; set; }

        public Exception ThrowWith { get; set; }

        public FakeInventoryGateway()
        {
            Result = InventoryResult.Success(Snapshot(
                new[]
                {
                    new InventorySlotSnapshot("mat_ore_basic", 10, 0),
                    new InventorySlotSnapshot("soul_feng_rui", 2, 1)
                },
                Array.Empty<EquipmentSlotSnapshot>()));
        }

        public static InventorySnapshot Snapshot(
            IReadOnlyList<InventorySlotSnapshot> slots,
            IReadOnlyList<EquipmentSlotSnapshot> equipment,
            int capacity = 60,
            int tier = 0)
        {
            InventorySnapshot.TryCreate(slots, equipment, capacity, tier, out var snapshot);
            return snapshot;
        }

        public UniTask<InventoryResult> RequestInventoryAsync(CancellationToken cancellationToken)
        {
            LoadCount++;
            return Respond();
        }

        public UniTask<InventoryResult> MutateAsync(
            InventoryOperation operation,
            string itemId,
            long quantity,
            string slotKind,
            int slotIndex,
            IReadOnlyList<string> itemOrder,
            CancellationToken cancellationToken)
        {
            Calls.Add((operation, itemId, quantity, slotKind, slotIndex, itemOrder));
            return Respond();
        }

        private UniTask<InventoryResult> Respond() =>
            ThrowWith != null
                ? UniTask.FromException<InventoryResult>(ThrowWith)
                : UniTask.FromResult(Result);
    }

    /// <summary>
    /// 仓库客户端：分类过滤、二次确认、装备占用保护与能力门禁。
    /// </summary>
    public sealed class InventoryControllerTests
    {
        private static (InventoryController Inventory, LobbyController Lobby, FakeInventoryGateway Gateway)
            Create(IServerCapabilities capabilities = null)
        {
            LobbyAccountSnapshot.TryCreate(1, 1000, 1000, 1000, out var summary);
            var caps = capabilities ?? LobbyTestCapabilities.Full();
            var lobby = new LobbyController(
                new LobbyModel(),
                new FakeLobbySceneGateway(),
                "Map1",
                new FakeLobbyAccountGateway { Result = LobbyAccountSummaryResult.Success(summary) },
                new FakeLobbyProfileGateway(),
                caps);

            var gateway = new FakeInventoryGateway();
            var inventory = new InventoryController(
                FakeGameConfigProvider.WithInventoryItems(), gateway, lobby, caps);

            lobby.Enter("player-one", 42);
            lobby.RequestFeature(LobbyFeature.Inventory);
            return (inventory, lobby, gateway);
        }

        [Test]
        public void OpeningTheInventoryEntryLoadsTheSnapshot()
        {
            var (inventory, _, gateway) = Create();

            Assert.That(inventory.Current.IsOpen, Is.True);
            Assert.That(gateway.LoadCount, Is.EqualTo(1));
            Assert.That(inventory.Current.HasSnapshot, Is.True);
            Assert.That(inventory.Current.Snapshot.Slots.Count, Is.EqualTo(2));
        }

        [Test]
        public void ClosingTheEntryClosesThePanel()
        {
            var (inventory, lobby, _) = Create();

            lobby.CloseFeature();

            Assert.That(inventory.Current.IsOpen, Is.False);
        }

        [Test]
        public void CompatibilityModeNeverSendsAnInventoryRequest()
        {
            var (inventory, _, gateway) = Create(LobbyTestCapabilities.LegacyCloud());

            Assert.That(gateway.LoadCount, Is.Zero, "旧云端不认识协议 27，绝不能发出去。");
            Assert.That(inventory.Current.StatusMessage, Does.Contain("服务器功能尚未升级"));
        }

        [Test]
        public void CategoryFilterKeepsOnlyMatchingItems()
        {
            var (inventory, _, _) = Create();

            inventory.SelectCategory(InventoryCategory.Soulstone);

            var visible = inventory.VisibleSlots.Select(slot => slot.ItemId).ToArray();
            Assert.That(visible, Is.EqualTo(new[] { "soul_feng_rui" }));

            inventory.SelectCategory(InventoryCategory.All);
            Assert.That(inventory.VisibleSlots.Count, Is.EqualTo(2));
        }

        [Test]
        public void SwitchingCategoryClearsTheSelection()
        {
            var (inventory, _, _) = Create();
            inventory.SelectItem("mat_ore_basic");

            inventory.SelectCategory(InventoryCategory.Soulstone);

            Assert.That(inventory.Current.SelectedItemId, Is.Empty);
        }

        [Test]
        public void DiscardRequiresConfirmationBeforeAnythingIsSent()
        {
            var (inventory, _, gateway) = Create();
            inventory.SelectItem("mat_ore_basic");

            inventory.RequestDiscard(3);

            Assert.That(inventory.Current.IsConfirmOpen, Is.True);
            Assert.That(inventory.Current.ConfirmQuantity, Is.EqualTo(3));
            Assert.That(gateway.Calls, Is.Empty, "确认之前不得发送任何写请求。");

            inventory.ConfirmPendingOperation();

            Assert.That(gateway.Calls.Count, Is.EqualTo(1));
            Assert.That(gateway.Calls[0].Operation, Is.EqualTo(InventoryOperation.Discard));
            Assert.That(gateway.Calls[0].Quantity, Is.EqualTo(3));
        }

        [Test]
        public void CancellingTheConfirmationSendsNothing()
        {
            var (inventory, _, gateway) = Create();
            inventory.SelectItem("mat_ore_basic");
            inventory.RequestDiscard(1);

            inventory.CancelConfirm();
            inventory.ConfirmPendingOperation();

            Assert.That(inventory.Current.IsConfirmOpen, Is.False);
            Assert.That(gateway.Calls, Is.Empty);
        }

        [Test]
        public void DiscardingMoreThanOwnedIsRefusedLocally()
        {
            var (inventory, _, gateway) = Create();
            inventory.SelectItem("mat_ore_basic");

            inventory.RequestDiscard(11);

            Assert.That(inventory.Current.IsConfirmOpen, Is.False);
            Assert.That(inventory.Current.StatusMessage, Is.Not.Empty);
            Assert.That(gateway.Calls, Is.Empty);
        }

        [Test]
        public void TheLastEquippedCopyCannotBeQueuedForDiscard()
        {
            var (inventory, _, gateway) = Create();
            gateway.Result = InventoryResult.Success(FakeInventoryGateway.Snapshot(
                new[] { new InventorySlotSnapshot("soul_feng_rui", 1, 0) },
                new[] { new EquipmentSlotSnapshot("Soulstone", 0, "soul_feng_rui") }));
            inventory.AutoSort();
            inventory.SelectItem("soul_feng_rui");
            gateway.Calls.Clear();

            inventory.RequestDiscard(1);

            Assert.That(inventory.Current.IsConfirmOpen, Is.False);
            Assert.That(inventory.Current.StatusMessage, Does.Contain("装备"));
            Assert.That(gateway.Calls, Is.Empty);
        }

        [Test]
        public void ASpareCopyOfAnEquippedItemCanStillBeDiscarded()
        {
            var (inventory, _, gateway) = Create();
            gateway.Result = InventoryResult.Success(FakeInventoryGateway.Snapshot(
                new[] { new InventorySlotSnapshot("soul_feng_rui", 3, 0) },
                new[] { new EquipmentSlotSnapshot("Soulstone", 0, "soul_feng_rui") }));
            inventory.AutoSort();
            inventory.SelectItem("soul_feng_rui");

            inventory.RequestDiscard(2);

            Assert.That(inventory.Current.IsConfirmOpen, Is.True);
        }

        [Test]
        public void MoveSendsTheCompleteNewOrder()
        {
            var (inventory, _, gateway) = Create();
            inventory.SelectItem("soul_feng_rui");

            inventory.MoveSelectedTo(0);

            var call = gateway.Calls.Single();
            Assert.That(call.Operation, Is.EqualTo(InventoryOperation.Reorder));
            Assert.That(call.Order, Is.EqualTo(new[] { "soul_feng_rui", "mat_ore_basic" }));
        }

        [Test]
        public void EquipSendsTheSlotAndItem()
        {
            var (inventory, _, gateway) = Create();

            inventory.Equip("Soulstone", 2, "soul_feng_rui");

            var call = gateway.Calls.Single();
            Assert.That(call.Operation, Is.EqualTo(InventoryOperation.Equip));
            Assert.That(call.SlotKind, Is.EqualTo("Soulstone"));
            Assert.That(call.SlotIndex, Is.EqualTo(2));
            Assert.That(call.ItemId, Is.EqualTo("soul_feng_rui"));
        }

        [Test]
        public void EquipWithoutASelectionSendsNothing()
        {
            var (inventory, _, gateway) = Create();

            inventory.Equip("Soulstone", 0, string.Empty);

            Assert.That(gateway.Calls, Is.Empty);
        }

        [Test]
        public void FailureKeepsThePreviousSnapshot()
        {
            var (inventory, _, gateway) = Create();
            var before = inventory.Current.Snapshot.Slots.Count;
            gateway.Result = InventoryResult.Failed(LobbyOperationStatus.InsufficientItems);

            inventory.AutoSort();

            Assert.That(inventory.Current.HasSnapshot, Is.True);
            Assert.That(inventory.Current.Snapshot.Slots.Count, Is.EqualTo(before));
            Assert.That(inventory.Current.StatusMessage, Does.Contain("材料不足"));
            Assert.That(inventory.Current.IsBusy, Is.False);
        }

        [Test]
        public void TransportFailureKeepsThePreviousSnapshot()
        {
            var (inventory, _, gateway) = Create();
            gateway.ThrowWith = new InvalidOperationException("socket down");

            inventory.AutoSort();

            Assert.That(inventory.Current.HasSnapshot, Is.True);
            Assert.That(inventory.Current.StatusMessage, Does.Contain("无法连接服务器"));
        }

        [Test]
        public void SelectionIsClearedWhenTheItemIsGone()
        {
            var (inventory, _, gateway) = Create();
            inventory.SelectItem("mat_ore_basic");
            gateway.Result = InventoryResult.Success(FakeInventoryGateway.Snapshot(
                new[] { new InventorySlotSnapshot("soul_feng_rui", 2, 0) },
                Array.Empty<EquipmentSlotSnapshot>()));

            inventory.AutoSort();

            Assert.That(inventory.Current.SelectedItemId, Is.Empty);
        }

        [Test]
        public void SnapshotRejectsInvalidPayloads()
        {
            Assert.That(
                InventorySnapshot.TryCreate(
                    new[] { new InventorySlotSnapshot("a", 0, 0) },
                    Array.Empty<EquipmentSlotSnapshot>(), 10, 0, out _),
                Is.False,
                "数量为 0 的格子不应存在。");
            Assert.That(
                InventorySnapshot.TryCreate(
                    new[] { new InventorySlotSnapshot("a", 1, 0), new InventorySlotSnapshot("a", 2, 1) },
                    Array.Empty<EquipmentSlotSnapshot>(), 10, 0, out _),
                Is.False,
                "同一种物品只能占一格。");
            Assert.That(
                InventorySnapshot.TryCreate(
                    new[] { new InventorySlotSnapshot("a", 1, 0) },
                    new[] { new EquipmentSlotSnapshot("Soulstone", 6, "a") }, 10, 0, out _),
                Is.False,
                "魂玉只有 6 槽，下标 6 越界。");
            Assert.That(
                InventorySnapshot.TryCreate(
                    new[] { new InventorySlotSnapshot("a", 1, 0), new InventorySlotSnapshot("b", 1, 1) },
                    Array.Empty<EquipmentSlotSnapshot>(), 1, 0, out _),
                Is.False,
                "占用格数不能超过容量。");
            Assert.That(
                InventorySnapshot.TryCreate(
                    new[] { new InventorySlotSnapshot("a", 1, 0) },
                    new[] { new EquipmentSlotSnapshot("Armor", 0, "a") }, 10, 0, out _),
                Is.True);
        }

        [Test]
        public void SnapshotReportsEquippedCounts()
        {
            var snapshot = FakeInventoryGateway.Snapshot(
                new[] { new InventorySlotSnapshot("soul_feng_rui", 3, 0) },
                new[] { new EquipmentSlotSnapshot("Soulstone", 0, "soul_feng_rui") });

            Assert.That(snapshot.EquippedCount("soul_feng_rui"), Is.EqualTo(1));
            Assert.That(snapshot.EquippedAt("Soulstone", 0), Is.EqualTo("soul_feng_rui"));
            Assert.That(snapshot.EquippedAt("Soulstone", 1), Is.Empty);
            Assert.That(snapshot.QuantityOf("soul_feng_rui"), Is.EqualTo(3));
            Assert.That(snapshot.QuantityOf("nothing"), Is.Zero);
        }

        [Test]
        public void BattleLoadSizesMatchTheGameplayBaseline()
        {
            Assert.That(InventorySnapshot.SoulstoneSlotCount, Is.EqualTo(6));
            Assert.That(InventorySnapshot.ArmorSlotCount, Is.EqualTo(1));
            Assert.That(InventorySnapshot.SlotCountOf("Weapon"), Is.Zero, "武器不属于战斗负载槽。");
        }

        [Test]
        public void DisposingReleasesTheLobbySubscription()
        {
            var (inventory, lobby, gateway) = Create();

            inventory.Dispose();
            lobby.CloseFeature();
            lobby.RequestFeature(LobbyFeature.Inventory);

            Assert.That(gateway.LoadCount, Is.EqualTo(1), "释放后不应再跟随大厅状态重新加载。");
        }
    }
}
