using System;
using System.Collections.Generic;

namespace Naraka.Features.Inventory.Model
{
    /// <summary>仓库中的一格。一种物品占一格，数量由服务端权威给出。</summary>
    public readonly struct InventorySlotSnapshot
    {
        public InventorySlotSnapshot(string itemId, long quantity, int slotIndex)
        {
            ItemId = itemId ?? string.Empty;
            Quantity = quantity;
            SlotIndex = slotIndex;
        }

        public string ItemId { get; }

        public long Quantity { get; }

        public int SlotIndex { get; }
    }

    public readonly struct EquipmentSlotSnapshot
    {
        public EquipmentSlotSnapshot(string slotKind, int slotIndex, string itemId)
        {
            SlotKind = slotKind ?? string.Empty;
            SlotIndex = slotIndex;
            ItemId = itemId ?? string.Empty;
        }

        public string SlotKind { get; }

        public int SlotIndex { get; }

        public string ItemId { get; }
    }

    /// <summary>
    /// 服务端仓库快照的不可变副本。
    ///
    /// 只能通过 <see cref="TryCreate"/> 构造，因此负数量、越界槽位或超出容量的数据
    /// 无法进入业务层，也就无法显示到界面上。
    /// </summary>
    public sealed class InventorySnapshot
    {
        /// <summary>魂玉战斗负载固定 6 槽。</summary>
        public const int SoulstoneSlotCount = 6;

        /// <summary>护甲战斗负载固定 1 槽。</summary>
        public const int ArmorSlotCount = 1;

        public const string SoulstoneSlotKind = "Soulstone";
        public const string ArmorSlotKind = "Armor";

        public static readonly InventorySnapshot Empty = new(
            Array.Empty<InventorySlotSnapshot>(), Array.Empty<EquipmentSlotSnapshot>(), 0, 0);

        private InventorySnapshot(
            IReadOnlyList<InventorySlotSnapshot> slots,
            IReadOnlyList<EquipmentSlotSnapshot> equipment,
            int capacity,
            int tier)
        {
            Slots = slots;
            Equipment = equipment;
            Capacity = capacity;
            Tier = tier;
        }

        public IReadOnlyList<InventorySlotSnapshot> Slots { get; }

        public IReadOnlyList<EquipmentSlotSnapshot> Equipment { get; }

        public int Capacity { get; }

        public int Tier { get; }

        public int UsedSlots => Slots.Count;

        public static bool TryCreate(
            IReadOnlyList<InventorySlotSnapshot> slots,
            IReadOnlyList<EquipmentSlotSnapshot> equipment,
            int capacity,
            int tier,
            out InventorySnapshot snapshot)
        {
            snapshot = null;
            if (slots == null || equipment == null || capacity <= 0 || tier < 0 || slots.Count > capacity)
            {
                return false;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var slot in slots)
            {
                if (string.IsNullOrEmpty(slot.ItemId) || slot.Quantity <= 0 || slot.SlotIndex < 0)
                {
                    return false;
                }

                // 一种物品只能占一格：出现两行说明服务端或解码出了问题，不能带进界面。
                if (!seen.Add(slot.ItemId))
                {
                    return false;
                }
            }

            foreach (var slot in equipment)
            {
                var slotCount = SlotCountOf(slot.SlotKind);
                if (slotCount == 0 || slot.SlotIndex < 0 || slot.SlotIndex >= slotCount ||
                    string.IsNullOrEmpty(slot.ItemId))
                {
                    return false;
                }
            }

            snapshot = new InventorySnapshot(slots, equipment, capacity, tier);
            return true;
        }

        public static int SlotCountOf(string slotKind)
        {
            if (string.Equals(slotKind, SoulstoneSlotKind, StringComparison.Ordinal))
            {
                return SoulstoneSlotCount;
            }

            return string.Equals(slotKind, ArmorSlotKind, StringComparison.Ordinal) ? ArmorSlotCount : 0;
        }

        public long QuantityOf(string itemId)
        {
            foreach (var slot in Slots)
            {
                if (string.Equals(slot.ItemId, itemId, StringComparison.Ordinal))
                {
                    return slot.Quantity;
                }
            }

            return 0L;
        }

        /// <summary>取得某个战斗负载槽当前装备的物品。空字符串表示该槽为空。</summary>
        public string EquippedAt(string slotKind, int slotIndex)
        {
            foreach (var slot in Equipment)
            {
                if (string.Equals(slot.SlotKind, slotKind, StringComparison.Ordinal) &&
                    slot.SlotIndex == slotIndex)
                {
                    return slot.ItemId;
                }
            }

            return string.Empty;
        }

        /// <summary>该物品当前被几个战斗负载槽占用。售卖与丢弃时必须保留这么多份。</summary>
        public int EquippedCount(string itemId)
        {
            var count = 0;
            foreach (var slot in Equipment)
            {
                if (string.Equals(slot.ItemId, itemId, StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }
    }
}
