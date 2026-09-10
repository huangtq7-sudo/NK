using Naraka.Config;
using Naraka.Server.Application.Config;
using Naraka.Server.Application.Progression;

namespace Naraka.Server.Application.Inventory;

public readonly struct InventoryResult
{
    private InventoryResult(LobbyOperationStatus status, InventorySnapshot? snapshot, CurrencyBalances? balances)
    {
        Status = status;
        Snapshot = snapshot;
        Balances = balances;
    }

    public LobbyOperationStatus Status { get; }

    public InventorySnapshot? Snapshot { get; }

    /// <summary>只有引起货币变动的操作（售卖、扩容）才携带余额。</summary>
    public CurrencyBalances? Balances { get; }

    public static InventoryResult Success(InventorySnapshot snapshot, CurrencyBalances? balances = null) =>
        new(LobbyOperationStatus.Success, snapshot, balances);

    public static InventoryResult Failed(LobbyOperationStatus status) => new(status, null, null);
}

/// <summary>
/// 仓库业务。
///
/// 这里是全部数量判定的唯一执行点：堆叠上限、容量、装备占用与售卖价格都来自生成配置，
/// 客户端提交的任何数量都会被重新校验。武器不进入仓库，因此这里永远不会出现 WeaponId。
/// </summary>
public sealed class InventoryService(
    IInventoryRepository inventory,
    ICurrencyLedgerRepository currencies,
    IAccountProfileRepository profiles,
    GameConfig config)
{
    private readonly IInventoryRepository _inventory =
        inventory ?? throw new ArgumentNullException(nameof(inventory));

    private readonly ICurrencyLedgerRepository _currencies =
        currencies ?? throw new ArgumentNullException(nameof(currencies));

    private readonly IAccountProfileRepository _profiles =
        profiles ?? throw new ArgumentNullException(nameof(profiles));

    private readonly GameConfig _config = config ?? throw new ArgumentNullException(nameof(config));

    public async Task<InventoryResult> GetAsync(long accountId, CancellationToken cancellationToken)
    {
        if (accountId <= 0)
        {
            return InventoryResult.Failed(LobbyOperationStatus.InvalidRequest);
        }

        try
        {
            return InventoryResult.Success(await ReadSnapshotAsync(accountId, cancellationToken));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsStorageFault(exception))
        {
            return InventoryResult.Failed(LobbyOperationStatus.DatabaseUnavailable);
        }
        catch (Exception)
        {
            return InventoryResult.Failed(LobbyOperationStatus.InternalError);
        }
    }

    /// <summary>
    /// 丢弃物品。
    ///
    /// 丢弃不产生任何货币收益，因此没有流水。已装备的最后一份不允许丢弃：
    /// 让装备指向一个已经不存在的物品会在战斗里变成一个无法解释的空槽。
    /// </summary>
    public Task<InventoryResult> DiscardAsync(
        long accountId,
        string? itemId,
        long quantity,
        CancellationToken cancellationToken) =>
        RemoveAsync(accountId, itemId, quantity, sell: false, cancellationToken);

    /// <summary>售卖物品。按配置的 SellPrice 结算，余额与不可变流水在同一事务提交。</summary>
    public Task<InventoryResult> SellAsync(
        long accountId,
        string? itemId,
        long quantity,
        string? requestId,
        CancellationToken cancellationToken) =>
        RemoveAsync(accountId, itemId, quantity, sell: true, cancellationToken, requestId);

    private async Task<InventoryResult> RemoveAsync(
        long accountId,
        string? itemId,
        long quantity,
        bool sell,
        CancellationToken cancellationToken,
        string? requestId = null)
    {
        if (accountId <= 0 || quantity <= 0 || string.IsNullOrWhiteSpace(itemId))
        {
            return InventoryResult.Failed(LobbyOperationStatus.InvalidRequest);
        }

        if (!_config.TryGetItem(itemId, out var item) || item is null)
        {
            return InventoryResult.Failed(LobbyOperationStatus.InvalidRequest);
        }

        if (sell && (item.SellPrice <= 0 || string.IsNullOrEmpty(item.SellCurrencyId)))
        {
            return InventoryResult.Failed(LobbyOperationStatus.NotAvailable);
        }

        if (sell && (string.IsNullOrWhiteSpace(requestId) || requestId.Length > 64))
        {
            return InventoryResult.Failed(LobbyOperationStatus.InvalidRequest);
        }

        try
        {
            var slots = await _inventory.ListSlotsAsync(accountId, cancellationToken);
            var owned = slots.FirstOrDefault(slot => slot.ItemId == itemId)?.Quantity ?? 0L;
            if (owned < quantity)
            {
                return InventoryResult.Failed(LobbyOperationStatus.InsufficientItems);
            }

            // 装备中的魂玉或护甲必须保留至少一份。玩家应该先卸下，而不是让装备指向空气。
            var equipment = await _inventory.ListEquipmentAsync(accountId, cancellationToken);
            var equippedCount = equipment.Count(slot => slot.ItemId == itemId);
            if (equippedCount > 0 && owned - quantity < equippedCount)
            {
                return InventoryResult.Failed(LobbyOperationStatus.Forbidden);
            }

            var capacity = await ReadCapacityAsync(accountId, cancellationToken);
            var applied = await _inventory.TryApplyAsync(
                accountId,
                new[] { new InventoryDelta(itemId, -quantity) },
                StackLimits(),
                capacity,
                cancellationToken);

            if (!applied)
            {
                return InventoryResult.Failed(LobbyOperationStatus.InsufficientItems);
            }

            CurrencyBalances? balances = null;
            if (sell)
            {
                var income = checked(item.SellPrice * quantity);
                var write = await _currencies.TryApplyAsync(
                    accountId,
                    new[] { new CurrencyGrant(item.SellCurrencyId, income) },
                    CurrencyLedgerReason.ItemSell,
                    requestId!,
                    cancellationToken);

                if (!write.Applied && !write.AlreadyApplied)
                {
                    return InventoryResult.Failed(LobbyOperationStatus.InternalError);
                }

                balances = write.Balances;
            }

            return InventoryResult.Success(await ReadSnapshotAsync(accountId, cancellationToken), balances);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsStorageFault(exception))
        {
            return InventoryResult.Failed(LobbyOperationStatus.DatabaseUnavailable);
        }
        catch (Exception)
        {
            return InventoryResult.Failed(LobbyOperationStatus.InternalError);
        }
    }

    /// <summary>
    /// 整理背包 / 交换槽位。客户端提交完整的新顺序，服务端只接受"与现有物品集合完全一致"的排列，
    /// 因此重排永远不可能凭空增删物品。
    /// </summary>
    public async Task<InventoryResult> ReorderAsync(
        long accountId,
        IReadOnlyList<string>? itemIdsInSlotOrder,
        CancellationToken cancellationToken)
    {
        if (accountId <= 0 || itemIdsInSlotOrder is null)
        {
            return InventoryResult.Failed(LobbyOperationStatus.InvalidRequest);
        }

        try
        {
            var slots = await _inventory.ListSlotsAsync(accountId, cancellationToken);
            var existing = slots.Select(slot => slot.ItemId).ToHashSet(StringComparer.Ordinal);
            var requested = new HashSet<string>(itemIdsInSlotOrder, StringComparer.Ordinal);

            if (requested.Count != itemIdsInSlotOrder.Count || !existing.SetEquals(requested))
            {
                return InventoryResult.Failed(LobbyOperationStatus.InvalidRequest);
            }

            if (!await _inventory.ReorderAsync(accountId, itemIdsInSlotOrder, cancellationToken))
            {
                return InventoryResult.Failed(LobbyOperationStatus.Conflict);
            }

            return InventoryResult.Success(await ReadSnapshotAsync(accountId, cancellationToken));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsStorageFault(exception))
        {
            return InventoryResult.Failed(LobbyOperationStatus.DatabaseUnavailable);
        }
        catch (Exception)
        {
            return InventoryResult.Failed(LobbyOperationStatus.InternalError);
        }
    }

    /// <summary>
    /// 按配置的自动整理规则重排：先按分类，再按品质从高到低，最后按配置显示顺序。
    /// 规则放在服务端，两端界面才不会给出不同的"整理结果"。
    /// </summary>
    public async Task<InventoryResult> AutoSortAsync(long accountId, CancellationToken cancellationToken)
    {
        if (accountId <= 0)
        {
            return InventoryResult.Failed(LobbyOperationStatus.InvalidRequest);
        }

        try
        {
            var slots = await _inventory.ListSlotsAsync(accountId, cancellationToken);
            var ordered = slots
                .Select(slot => slot.ItemId)
                .OrderBy(CategoryRank)
                .ThenByDescending(QualityRank)
                .ThenBy(SortOrder)
                .ThenBy(itemId => itemId, StringComparer.Ordinal)
                .ToArray();

            return await ReorderAsync(accountId, ordered, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsStorageFault(exception))
        {
            return InventoryResult.Failed(LobbyOperationStatus.DatabaseUnavailable);
        }
        catch (Exception)
        {
            return InventoryResult.Failed(LobbyOperationStatus.InternalError);
        }
    }

    /// <summary>
    /// 装备或卸下魂玉/护甲。
    ///
    /// 规则来自玩法基线：魂玉 6 槽且同名不可重复，护甲 1 槽；
    /// 装备的物品必须至少拥有一份，否则装备会指向一个玩家并不持有的物品。
    /// </summary>
    public async Task<InventoryResult> EquipAsync(
        long accountId,
        string? slotKind,
        int slotIndex,
        string? itemId,
        CancellationToken cancellationToken)
    {
        if (accountId <= 0 || slotKind is null || !EquipmentSlotKind.IsDefined(slotKind))
        {
            return InventoryResult.Failed(LobbyOperationStatus.InvalidRequest);
        }

        if (slotIndex < 0 || slotIndex >= EquipmentSlotKind.SlotCountOf(slotKind))
        {
            return InventoryResult.Failed(LobbyOperationStatus.InvalidRequest);
        }

        var unequip = string.IsNullOrWhiteSpace(itemId);
        if (!unequip)
        {
            if (!_config.TryGetItem(itemId!, out var item) || item is null)
            {
                return InventoryResult.Failed(LobbyOperationStatus.InvalidRequest);
            }

            var expectedCategory = slotKind == EquipmentSlotKind.Soulstone
                ? ConfigItemCategory.Soulstone
                : ConfigItemCategory.Armor;
            if (!string.Equals(item.Category, expectedCategory, StringComparison.Ordinal))
            {
                return InventoryResult.Failed(LobbyOperationStatus.InvalidRequest);
            }
        }

        try
        {
            if (!unequip)
            {
                var slots = await _inventory.ListSlotsAsync(accountId, cancellationToken);
                var owned = slots.FirstOrDefault(slot => slot.ItemId == itemId)?.Quantity ?? 0L;
                if (owned <= 0)
                {
                    return InventoryResult.Failed(LobbyOperationStatus.InsufficientItems);
                }

                var equipment = await _inventory.ListEquipmentAsync(accountId, cancellationToken);
                var duplicate = equipment.Any(slot =>
                    slot.SlotKind == slotKind &&
                    slot.SlotIndex != slotIndex &&
                    slot.ItemId == itemId);
                if (duplicate)
                {
                    // 同名魂玉不可重复：这里明确拒绝，而不是悄悄把另一个槽清空。
                    return InventoryResult.Failed(LobbyOperationStatus.Conflict);
                }
            }

            if (!await _inventory.SetEquipmentAsync(
                    accountId, slotKind, slotIndex, unequip ? null : itemId, cancellationToken))
            {
                return InventoryResult.Failed(LobbyOperationStatus.Conflict);
            }

            return InventoryResult.Success(await ReadSnapshotAsync(accountId, cancellationToken));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsStorageFault(exception))
        {
            return InventoryResult.Failed(LobbyOperationStatus.DatabaseUnavailable);
        }
        catch (Exception)
        {
            return InventoryResult.Failed(LobbyOperationStatus.InternalError);
        }
    }

    /// <summary>
    /// 扩容一档。价格与每档容量全部来自配置；扣费与档位提升在同一事务中完成，
    /// 重复请求由 RequestId 的流水唯一键拦住，不会重复扣费。
    /// </summary>
    public async Task<InventoryResult> ExpandAsync(
        long accountId,
        string? requestId,
        CancellationToken cancellationToken)
    {
        if (accountId <= 0 || string.IsNullOrWhiteSpace(requestId) || requestId.Length > 64)
        {
            return InventoryResult.Failed(LobbyOperationStatus.InvalidRequest);
        }

        try
        {
            var profile = await _profiles.FindProfileAsync(accountId, cancellationToken);
            if (profile is null)
            {
                return InventoryResult.Failed(LobbyOperationStatus.NotFound);
            }

            if (!_config.TryGetNextInventoryTier(profile.InventoryTier, out var next) || next is null)
            {
                return InventoryResult.Failed(LobbyOperationStatus.LimitReached);
            }

            var write = await _currencies.TryApplyAsync(
                accountId,
                new[] { new CurrencyGrant(next.ExpandCurrencyId, -next.ExpandPrice) },
                CurrencyLedgerReason.InventoryExpand,
                requestId!,
                cancellationToken);

            if (write.AlreadyApplied)
            {
                // 重复请求：返回当前状态而不是再扣一次费。
                return InventoryResult.Success(
                    await ReadSnapshotAsync(accountId, cancellationToken), write.Balances);
            }

            if (!write.Applied)
            {
                return InventoryResult.Failed(LobbyOperationStatus.InsufficientCurrency);
            }

            if (!await _profiles.TryAdvanceInventoryTierAsync(
                    accountId, profile.InventoryTier, next.Tier, cancellationToken))
            {
                // 并发下另一个请求已经推进过档位。扣费流水唯一键保证不会双扣，
                // 因此这里只需要把最新状态回给客户端。
                return InventoryResult.Success(
                    await ReadSnapshotAsync(accountId, cancellationToken), write.Balances);
            }

            return InventoryResult.Success(
                await ReadSnapshotAsync(accountId, cancellationToken), write.Balances);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsStorageFault(exception))
        {
            return InventoryResult.Failed(LobbyOperationStatus.DatabaseUnavailable);
        }
        catch (Exception)
        {
            return InventoryResult.Failed(LobbyOperationStatus.InternalError);
        }
    }

    /// <summary>
    /// 发放物品。商店购买、抽奖、签到与奖励领取都通过它入库，
    /// 因此堆叠上限与容量只有一处判定。
    /// </summary>
    public async Task<bool> TryGrantAsync(
        long accountId,
        IReadOnlyList<InventoryDelta> deltas,
        CancellationToken cancellationToken)
    {
        var capacity = await ReadCapacityAsync(accountId, cancellationToken);
        return await _inventory.TryApplyAsync(accountId, deltas, StackLimits(), capacity, cancellationToken);
    }

    public IReadOnlyDictionary<string, int> StackLimits() =>
        _config.Catalog.Items.ToDictionary(item => item.ItemId, item => item.StackLimit, StringComparer.Ordinal);

    private async Task<int> ReadCapacityAsync(long accountId, CancellationToken cancellationToken)
    {
        var profile = await _profiles.FindProfileAsync(accountId, cancellationToken);
        return _config.GetInventoryCapacity(profile?.InventoryTier ?? 0);
    }

    private async Task<InventorySnapshot> ReadSnapshotAsync(long accountId, CancellationToken cancellationToken)
    {
        var profile = await _profiles.FindProfileAsync(accountId, cancellationToken);
        var tier = profile?.InventoryTier ?? 0;
        return new InventorySnapshot(
            await _inventory.ListSlotsAsync(accountId, cancellationToken),
            await _inventory.ListEquipmentAsync(accountId, cancellationToken),
            _config.GetInventoryCapacity(tier),
            tier);
    }

    private int CategoryRank(string itemId) =>
        _config.TryGetItem(itemId, out var item) && item is not null
            ? Array.IndexOf(ConfigItemCategory.All, item.Category)
            : int.MaxValue;

    private int QualityRank(string itemId) =>
        _config.TryGetItem(itemId, out var item) && item is not null
            ? ConfigQuality.IndexOf(item.Quality)
            : -1;

    private int SortOrder(string itemId) =>
        _config.TryGetItem(itemId, out var item) && item is not null ? item.SortOrder : int.MaxValue;

    private static bool IsStorageFault(Exception exception) =>
        exception is InventoryStorageException or AccountProfileStorageException;
}
