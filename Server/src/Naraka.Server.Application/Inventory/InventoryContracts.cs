using Naraka.Server.Application.Progression;

namespace Naraka.Server.Application.Inventory;

/// <summary>仓库中的一格。一种物品占一格，数量受配置 StackLimit 限制。</summary>
public sealed record InventorySlot(string ItemId, long Quantity, int SlotIndex);

/// <summary>战斗负载槽的种类。</summary>
public static class EquipmentSlotKind
{
    /// <summary>魂玉：固定 6 槽，同名魂玉不可重复。</summary>
    public const string Soulstone = "Soulstone";

    /// <summary>护甲：固定 1 槽。</summary>
    public const string Armor = "Armor";

    public const int SoulstoneSlotCount = 6;
    public const int ArmorSlotCount = 1;

    public static bool IsDefined(string kind) =>
        kind is Soulstone or Armor;

    public static int SlotCountOf(string kind) => kind switch
    {
        Soulstone => SoulstoneSlotCount,
        Armor => ArmorSlotCount,
        _ => 0
    };
}

public sealed record EquipmentSlot(string SlotKind, int SlotIndex, string ItemId);

/// <summary>仓库与装备的完整快照。一次读取就能画出整个界面，避免界面自己拼多次请求的结果。</summary>
public sealed record InventorySnapshot(
    IReadOnlyList<InventorySlot> Slots,
    IReadOnlyList<EquipmentSlot> Equipment,
    int Capacity,
    int Tier);

/// <summary>一次库存变动。正数为获得，负数为消耗。</summary>
public sealed record InventoryDelta(string ItemId, long Amount);

public sealed class InventoryStorageException : Exception
{
    public InventoryStorageException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// 仓库存储。
///
/// 每个写方法都在一个事务内完成，并且都要求调用方提供已经校验过的变动量：
/// 数量、容量与堆叠上限的业务判定属于 Application 层，仓储只负责原子地落盘。
/// </summary>
public interface IInventoryRepository
{
    Task<IReadOnlyList<InventorySlot>> ListSlotsAsync(long accountId, CancellationToken cancellationToken);

    Task<IReadOnlyList<EquipmentSlot>> ListEquipmentAsync(long accountId, CancellationToken cancellationToken);

    /// <summary>
    /// 原子应用一组库存变动。任一物品数量不足时整体失败并返回 false，不产生部分写入。
    /// </summary>
    Task<bool> TryApplyAsync(
        long accountId,
        IReadOnlyList<InventoryDelta> deltas,
        IReadOnlyDictionary<string, int> stackLimits,
        int capacity,
        CancellationToken cancellationToken);

    /// <summary>按给定顺序重排槽位。整理背包与交换槽位都走这里。</summary>
    Task<bool> ReorderAsync(
        long accountId,
        IReadOnlyList<string> itemIdsInSlotOrder,
        CancellationToken cancellationToken);

    /// <summary>写入一个装备槽。<paramref name="itemId"/> 为空表示卸下。</summary>
    Task<bool> SetEquipmentAsync(
        long accountId,
        string slotKind,
        int slotIndex,
        string? itemId,
        CancellationToken cancellationToken);
}

/// <summary>
/// 货币变动与不可变流水的原子写入。购买、售卖、锻造、抽奖与奖励领取共用这一个入口，
/// 因此"余额变化必然伴随一条流水"是结构性保证，而不是每处调用各自记得写。
/// </summary>
public interface ICurrencyLedgerRepository
{
    /// <summary>
    /// 在一个事务内应用货币变动并写入流水。
    /// 余额不足时返回 false 且不写入任何内容；同一 <paramref name="referenceId"/> 重复提交
    /// 会被唯一键拒绝，从而不会重复扣费或重复发放。
    /// </summary>
    Task<CurrencyWriteResult> TryApplyAsync(
        long accountId,
        IReadOnlyList<CurrencyGrant> deltas,
        string reason,
        string referenceId,
        CancellationToken cancellationToken);
}

public sealed record CurrencyWriteResult(bool Applied, bool AlreadyApplied, CurrencyBalances? Balances);
