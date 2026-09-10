using Naraka.Config;
using Naraka.Server.Application.Config;
using Naraka.Server.Application.Inventory;
using Naraka.Server.Application.Progression;

namespace Naraka.Server.Application.Tests;

/// <summary>
/// 仓库业务规则：堆叠上限、容量、装备占用、售卖流水与扩容幂等。
/// 使用仓库中真实生成的配置，因此这里断言的价格与上限就是线上会用的那一套。
/// </summary>
public sealed class InventoryServiceTests
{
    private const long AccountId = 42;

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
        InventoryService Service,
        MemoryInventoryRepository Inventory,
        MemoryCurrencyLedger Ledger,
        MemoryProfiles Profiles);

    private static async Task<Fixture> CreateAsync()
    {
        var inventory = new MemoryInventoryRepository();
        var ledger = new MemoryCurrencyLedger();
        var profiles = new MemoryProfiles();
        await profiles.CreateAsync(AccountId);
        return new Fixture(
            new InventoryService(inventory, ledger, profiles, Config), inventory, ledger, profiles);
    }

    [Fact]
    public async Task EmptyInventoryReportsTheInitialCapacity()
    {
        var fixture = await CreateAsync();

        var result = await fixture.Service.GetAsync(AccountId, CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.Success, result.Status);
        Assert.Empty(result.Snapshot!.Slots);
        Assert.Equal(Config.InitialInventoryCapacity, result.Snapshot.Capacity);
        Assert.Equal(0, result.Snapshot.Tier);
    }

    [Fact]
    public async Task GrantStacksTheSameItemIntoOneSlot()
    {
        var fixture = await CreateAsync();

        await fixture.Service.TryGrantAsync(
            AccountId,
            new[] { new InventoryDelta("mat_ore_basic", 10), new InventoryDelta("mat_ore_basic", 5) },
            CancellationToken.None);

        var result = await fixture.Service.GetAsync(AccountId, CancellationToken.None);
        var slot = Assert.Single(result.Snapshot!.Slots);
        Assert.Equal("mat_ore_basic", slot.ItemId);
        Assert.Equal(15, slot.Quantity);
    }

    [Fact]
    public async Task GrantAboveTheStackLimitIsRejectedEntirely()
    {
        var fixture = await CreateAsync();
        Config.TryGetItem("consumable_blood_pack", out var item);

        var granted = await fixture.Service.TryGrantAsync(
            AccountId,
            new[] { new InventoryDelta("consumable_blood_pack", item!.StackLimit + 1) },
            CancellationToken.None);

        Assert.False(granted);
        var result = await fixture.Service.GetAsync(AccountId, CancellationToken.None);
        Assert.Empty(result.Snapshot!.Slots);
    }

    [Fact]
    public async Task EveryConfiguredItemFitsInTheInitialCapacity()
    {
        var fixture = await CreateAsync();

        var granted = await fixture.Service.TryGrantAsync(
            AccountId,
            Config.Catalog.Items.Select(item => new InventoryDelta(item.ItemId, 1)).ToArray(),
            CancellationToken.None);

        // 初始容量必须至少能装下配置里的全部物品各一份，否则新手一开局就会被容量卡住。
        Assert.True(granted);
        Assert.True(Config.Catalog.Items.Length <= Config.InitialInventoryCapacity);
    }

    [Fact]
    public async Task GrantBeyondCapacityIsRejectedEntirely()
    {
        // 配置里的物品种类少于最小容量档位，因此直接以一个小容量驱动仓储，
        // 验证容量判定本身，而不是间接依赖配置恰好溢出。
        var repository = new MemoryInventoryRepository();
        var limits = Config.Catalog.Items.ToDictionary(
            item => item.ItemId, item => item.StackLimit, StringComparer.Ordinal);
        var three = Config.Catalog.Items.Take(3).Select(item => new InventoryDelta(item.ItemId, 1)).ToArray();

        var granted = await repository.TryApplyAsync(
            AccountId, three, limits, capacity: 2, CancellationToken.None);
        var slots = await repository.ListSlotsAsync(AccountId, CancellationToken.None);

        Assert.False(granted);
        Assert.Empty(slots);
    }

    [Fact]
    public async Task DiscardRemovesTheRequestedQuantityWithoutTouchingCurrency()
    {
        var fixture = await CreateAsync();
        await fixture.Service.TryGrantAsync(
            AccountId, new[] { new InventoryDelta("mat_ore_basic", 10) }, CancellationToken.None);

        var result = await fixture.Service.DiscardAsync(
            AccountId, "mat_ore_basic", 4, CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.Success, result.Status);
        Assert.Equal(6, result.Snapshot!.Slots.Single().Quantity);
        Assert.Empty(fixture.Ledger.Entries);
    }

    [Fact]
    public async Task DiscardingEverythingReleasesTheSlot()
    {
        var fixture = await CreateAsync();
        await fixture.Service.TryGrantAsync(
            AccountId, new[] { new InventoryDelta("mat_ore_basic", 3) }, CancellationToken.None);

        var result = await fixture.Service.DiscardAsync(
            AccountId, "mat_ore_basic", 3, CancellationToken.None);

        Assert.Empty(result.Snapshot!.Slots);
    }

    [Fact]
    public async Task DiscardMoreThanOwnedIsRejected()
    {
        var fixture = await CreateAsync();
        await fixture.Service.TryGrantAsync(
            AccountId, new[] { new InventoryDelta("mat_ore_basic", 3) }, CancellationToken.None);

        var result = await fixture.Service.DiscardAsync(
            AccountId, "mat_ore_basic", 4, CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.InsufficientItems, result.Status);
    }

    [Fact]
    public async Task SellPaysTheConfiguredPriceAndWritesOneLedgerEntry()
    {
        var fixture = await CreateAsync();
        Config.TryGetItem("mat_ore_basic", out var item);
        await fixture.Service.TryGrantAsync(
            AccountId, new[] { new InventoryDelta("mat_ore_basic", 10) }, CancellationToken.None);

        var result = await fixture.Service.SellAsync(
            AccountId, "mat_ore_basic", 4, "req-sell-1", CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.Success, result.Status);
        Assert.Equal(6, result.Snapshot!.Slots.Single().Quantity);
        var entry = Assert.Single(fixture.Ledger.Entries);
        Assert.Equal(item!.SellCurrencyId, entry.CurrencyId);
        Assert.Equal(item.SellPrice * 4, entry.Delta);
        Assert.Equal(CurrencyLedgerReason.ItemSell, entry.Reason);
        Assert.Equal(item.SellPrice * 4, result.Balances!.Get(item.SellCurrencyId));
    }

    [Fact]
    public async Task SellWithoutRequestIdIsRejected()
    {
        var fixture = await CreateAsync();
        await fixture.Service.TryGrantAsync(
            AccountId, new[] { new InventoryDelta("mat_ore_basic", 5) }, CancellationToken.None);

        var result = await fixture.Service.SellAsync(
            AccountId, "mat_ore_basic", 1, "  ", CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.InvalidRequest, result.Status);
        Assert.Empty(fixture.Ledger.Entries);
    }

    [Fact]
    public async Task SellingAnItemWithoutAPriceIsRejected()
    {
        var fixture = await CreateAsync();
        // 补签卡的 SellPrice 为 0，因此不可售卖。
        await fixture.Service.TryGrantAsync(
            AccountId, new[] { new InventoryDelta("special_makeup_card", 2) }, CancellationToken.None);

        var result = await fixture.Service.SellAsync(
            AccountId, "special_makeup_card", 1, "req-1", CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.NotAvailable, result.Status);
    }

    [Fact]
    public async Task SellingTheLastEquippedCopyIsRefused()
    {
        var fixture = await CreateAsync();
        await fixture.Service.TryGrantAsync(
            AccountId, new[] { new InventoryDelta("soul_feng_rui", 1) }, CancellationToken.None);
        await fixture.Service.EquipAsync(
            AccountId, EquipmentSlotKind.Soulstone, 0, "soul_feng_rui", CancellationToken.None);

        var result = await fixture.Service.SellAsync(
            AccountId, "soul_feng_rui", 1, "req-1", CancellationToken.None);

        // 玩家应当先卸下：让装备指向一个已经不存在的物品会在战斗里变成一个无法解释的空槽。
        Assert.Equal(LobbyOperationStatus.Forbidden, result.Status);
        Assert.Single((await fixture.Service.GetAsync(AccountId, CancellationToken.None)).Snapshot!.Slots);
    }

    [Fact]
    public async Task SellingASpareCopyOfAnEquippedItemIsAllowed()
    {
        var fixture = await CreateAsync();
        await fixture.Service.TryGrantAsync(
            AccountId, new[] { new InventoryDelta("soul_feng_rui", 3) }, CancellationToken.None);
        await fixture.Service.EquipAsync(
            AccountId, EquipmentSlotKind.Soulstone, 0, "soul_feng_rui", CancellationToken.None);

        var result = await fixture.Service.SellAsync(
            AccountId, "soul_feng_rui", 2, "req-1", CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.Success, result.Status);
        Assert.Equal(1, result.Snapshot!.Slots.Single().Quantity);
    }

    [Fact]
    public async Task EquippingAnItemThatIsNotOwnedIsRejected()
    {
        var fixture = await CreateAsync();

        var result = await fixture.Service.EquipAsync(
            AccountId, EquipmentSlotKind.Soulstone, 0, "soul_feng_rui", CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.InsufficientItems, result.Status);
    }

    [Fact]
    public async Task TheSameSoulstoneCannotOccupyTwoBattleSlots()
    {
        var fixture = await CreateAsync();
        await fixture.Service.TryGrantAsync(
            AccountId, new[] { new InventoryDelta("soul_feng_rui", 2) }, CancellationToken.None);
        await fixture.Service.EquipAsync(
            AccountId, EquipmentSlotKind.Soulstone, 0, "soul_feng_rui", CancellationToken.None);

        var result = await fixture.Service.EquipAsync(
            AccountId, EquipmentSlotKind.Soulstone, 1, "soul_feng_rui", CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.Conflict, result.Status);
    }

    [Theory]
    [InlineData(EquipmentSlotKind.Soulstone, 6)]
    [InlineData(EquipmentSlotKind.Soulstone, -1)]
    [InlineData(EquipmentSlotKind.Armor, 1)]
    public async Task SlotIndexOutsideTheBattleLoadIsRejected(string slotKind, int slotIndex)
    {
        var fixture = await CreateAsync();

        var result = await fixture.Service.EquipAsync(
            AccountId, slotKind, slotIndex, "soul_feng_rui", CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.InvalidRequest, result.Status);
    }

    [Fact]
    public async Task ArmorCannotBeEquippedIntoASoulstoneSlot()
    {
        var fixture = await CreateAsync();
        await fixture.Service.TryGrantAsync(
            AccountId, new[] { new InventoryDelta("armor_cloth", 1) }, CancellationToken.None);

        var result = await fixture.Service.EquipAsync(
            AccountId, EquipmentSlotKind.Soulstone, 0, "armor_cloth", CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.InvalidRequest, result.Status);
    }

    [Fact]
    public async Task UnequippingClearsTheSlot()
    {
        var fixture = await CreateAsync();
        await fixture.Service.TryGrantAsync(
            AccountId, new[] { new InventoryDelta("armor_cloth", 1) }, CancellationToken.None);
        await fixture.Service.EquipAsync(
            AccountId, EquipmentSlotKind.Armor, 0, "armor_cloth", CancellationToken.None);

        var result = await fixture.Service.EquipAsync(
            AccountId, EquipmentSlotKind.Armor, 0, null, CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.Success, result.Status);
        Assert.Empty(result.Snapshot!.Equipment);
    }

    [Fact]
    public async Task ReorderMustListExactlyTheOwnedItems()
    {
        var fixture = await CreateAsync();
        await fixture.Service.TryGrantAsync(
            AccountId,
            new[] { new InventoryDelta("mat_ore_basic", 1), new InventoryDelta("mat_wolf_fang", 1) },
            CancellationToken.None);

        var missing = await fixture.Service.ReorderAsync(
            AccountId, new[] { "mat_ore_basic" }, CancellationToken.None);
        var extra = await fixture.Service.ReorderAsync(
            AccountId, new[] { "mat_ore_basic", "mat_wolf_fang", "mat_wolf_hide" }, CancellationToken.None);
        var duplicated = await fixture.Service.ReorderAsync(
            AccountId, new[] { "mat_ore_basic", "mat_ore_basic" }, CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.InvalidRequest, missing.Status);
        Assert.Equal(LobbyOperationStatus.InvalidRequest, extra.Status);
        Assert.Equal(LobbyOperationStatus.InvalidRequest, duplicated.Status);
    }

    [Fact]
    public async Task ReorderSwapsTwoSlots()
    {
        var fixture = await CreateAsync();
        await fixture.Service.TryGrantAsync(
            AccountId,
            new[] { new InventoryDelta("mat_ore_basic", 1), new InventoryDelta("mat_wolf_fang", 1) },
            CancellationToken.None);

        var result = await fixture.Service.ReorderAsync(
            AccountId, new[] { "mat_wolf_fang", "mat_ore_basic" }, CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.Success, result.Status);
        Assert.Equal("mat_wolf_fang", result.Snapshot!.Slots[0].ItemId);
        Assert.Equal("mat_ore_basic", result.Snapshot.Slots[1].ItemId);
    }

    [Fact]
    public async Task AutoSortGroupsByCategoryThenQuality()
    {
        var fixture = await CreateAsync();
        await fixture.Service.TryGrantAsync(
            AccountId,
            new[]
            {
                new InventoryDelta("mat_ore_basic", 1),
                new InventoryDelta("soul_bu_qu", 1),
                new InventoryDelta("soul_feng_rui", 1),
                new InventoryDelta("consumable_blood_pack", 1)
            },
            CancellationToken.None);

        var result = await fixture.Service.AutoSortAsync(AccountId, CancellationToken.None);

        var order = result.Snapshot!.Slots.Select(slot => slot.ItemId).ToArray();
        // 魂玉在前（分类顺序），且红色排在白色之前。
        Assert.Equal("soul_bu_qu", order[0]);
        Assert.Equal("soul_feng_rui", order[1]);
        Assert.Contains("mat_ore_basic", order);
        Assert.Contains("consumable_blood_pack", order);
    }

    [Fact]
    public async Task ExpandChargesOnceAndRaisesTheCapacity()
    {
        var fixture = await CreateAsync();
        var tierOne = Config.InventoryCapacities[1];
        fixture.Ledger.Seed(AccountId, tierOne.ExpandCurrencyId, tierOne.ExpandPrice * 3);

        var first = await fixture.Service.ExpandAsync(AccountId, "req-expand", CancellationToken.None);
        var repeat = await fixture.Service.ExpandAsync(AccountId, "req-expand", CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.Success, first.Status);
        Assert.Equal(tierOne.Capacity, first.Snapshot!.Capacity);
        Assert.Equal(1, first.Snapshot.Tier);

        // 同一个 RequestId 重复提交只扣一次费，档位也只推进一次。
        Assert.Equal(LobbyOperationStatus.Success, repeat.Status);
        Assert.Equal(1, repeat.Snapshot!.Tier);
        Assert.Single(fixture.Ledger.Entries);
    }

    [Fact]
    public async Task ExpandWithoutEnoughCurrencyIsRejected()
    {
        var fixture = await CreateAsync();

        var result = await fixture.Service.ExpandAsync(AccountId, "req-expand", CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.InsufficientCurrency, result.Status);
        Assert.Equal(0, (await fixture.Service.GetAsync(AccountId, CancellationToken.None)).Snapshot!.Tier);
    }

    [Fact]
    public async Task ExpandStopsAtTheHighestTier()
    {
        var fixture = await CreateAsync();
        fixture.Profiles.SetTier(AccountId, Config.MaximumInventoryTier);

        var result = await fixture.Service.ExpandAsync(AccountId, "req-expand", CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.LimitReached, result.Status);
        Assert.Empty(fixture.Ledger.Entries);
    }

    [Theory]
    [InlineData("item_does_not_exist", 1)]
    [InlineData("mat_ore_basic", 0)]
    [InlineData("mat_ore_basic", -3)]
    [InlineData(null, 1)]
    public async Task InvalidDiscardArgumentsAreRejected(string? itemId, long quantity)
    {
        var fixture = await CreateAsync();

        var result = await fixture.Service.DiscardAsync(AccountId, itemId, quantity, CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.InvalidRequest, result.Status);
    }

    [Fact]
    public async Task StorageFaultBecomesDatabaseUnavailable()
    {
        var fixture = await CreateAsync();
        fixture.Inventory.FailWithStorageError = true;

        var result = await fixture.Service.GetAsync(AccountId, CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.DatabaseUnavailable, result.Status);
    }

    internal sealed class MemoryInventoryRepository : IInventoryRepository
    {
        private readonly Dictionary<long, Dictionary<string, InventorySlot>> _slots = new();
        private readonly Dictionary<long, Dictionary<(string Kind, int Index), string>> _equipment = new();

        public bool FailWithStorageError { get; set; }

        public Task<IReadOnlyList<InventorySlot>> ListSlotsAsync(long accountId, CancellationToken token)
        {
            Guard();
            return Task.FromResult<IReadOnlyList<InventorySlot>>(
                _slots.TryGetValue(accountId, out var slots)
                    ? slots.Values.OrderBy(slot => slot.SlotIndex).ToArray()
                    : Array.Empty<InventorySlot>());
        }

        public Task<IReadOnlyList<EquipmentSlot>> ListEquipmentAsync(long accountId, CancellationToken token)
        {
            Guard();
            return Task.FromResult<IReadOnlyList<EquipmentSlot>>(
                _equipment.TryGetValue(accountId, out var slots)
                    ? slots.Select(pair => new EquipmentSlot(pair.Key.Kind, pair.Key.Index, pair.Value))
                        .OrderBy(slot => slot.SlotKind, StringComparer.Ordinal)
                        .ThenBy(slot => slot.SlotIndex)
                        .ToArray()
                    : Array.Empty<EquipmentSlot>());
        }

        public Task<bool> TryApplyAsync(
            long accountId,
            IReadOnlyList<InventoryDelta> deltas,
            IReadOnlyDictionary<string, int> stackLimits,
            int capacity,
            CancellationToken token)
        {
            Guard();
            if (!_slots.TryGetValue(accountId, out var slots))
            {
                slots = new Dictionary<string, InventorySlot>(StringComparer.Ordinal);
                _slots[accountId] = slots;
            }

            var draft = new Dictionary<string, InventorySlot>(slots, StringComparer.Ordinal);
            var nextSlot = draft.Count == 0 ? 0 : draft.Values.Max(slot => slot.SlotIndex) + 1;

            foreach (var group in deltas.GroupBy(delta => delta.ItemId, StringComparer.Ordinal))
            {
                var amount = group.Sum(delta => delta.Amount);
                if (amount == 0)
                {
                    continue;
                }

                if (!stackLimits.TryGetValue(group.Key, out var limit) || limit <= 0)
                {
                    return Task.FromResult(false);
                }

                draft.TryGetValue(group.Key, out var existing);
                var next = (existing?.Quantity ?? 0L) + amount;
                if (next < 0 || next > limit)
                {
                    return Task.FromResult(false);
                }

                if (next == 0)
                {
                    draft.Remove(group.Key);
                }
                else
                {
                    draft[group.Key] = new InventorySlot(group.Key, next, existing?.SlotIndex ?? nextSlot++);
                }
            }

            if (draft.Count > capacity)
            {
                return Task.FromResult(false);
            }

            _slots[accountId] = draft;
            return Task.FromResult(true);
        }

        public Task<bool> ReorderAsync(long accountId, IReadOnlyList<string> order, CancellationToken token)
        {
            Guard();
            if (!_slots.TryGetValue(accountId, out var slots) || slots.Count != order.Count)
            {
                return Task.FromResult(false);
            }

            var draft = new Dictionary<string, InventorySlot>(StringComparer.Ordinal);
            for (var index = 0; index < order.Count; index++)
            {
                if (!slots.TryGetValue(order[index], out var slot))
                {
                    return Task.FromResult(false);
                }

                draft[slot.ItemId] = slot with { SlotIndex = index };
            }

            _slots[accountId] = draft;
            return Task.FromResult(true);
        }

        public Task<bool> SetEquipmentAsync(
            long accountId,
            string slotKind,
            int slotIndex,
            string? itemId,
            CancellationToken token)
        {
            Guard();
            if (!_equipment.TryGetValue(accountId, out var slots))
            {
                slots = new Dictionary<(string, int), string>();
                _equipment[accountId] = slots;
            }

            if (string.IsNullOrWhiteSpace(itemId))
            {
                slots.Remove((slotKind, slotIndex));
            }
            else
            {
                slots[(slotKind, slotIndex)] = itemId;
            }

            return Task.FromResult(true);
        }

        private void Guard()
        {
            if (FailWithStorageError)
            {
                throw new InventoryStorageException("injected storage fault");
            }
        }
    }

    internal sealed class MemoryCurrencyLedger : ICurrencyLedgerRepository
    {
        private readonly Dictionary<long, Dictionary<string, long>> _balances = new();
        private readonly HashSet<(long, string, string)> _applied = new();

        public List<(long AccountId, string CurrencyId, long Delta, string Reason)> Entries { get; } = new();

        public void Seed(long accountId, string currencyId, long amount)
        {
            if (!_balances.TryGetValue(accountId, out var balances))
            {
                balances = new Dictionary<string, long>(StringComparer.Ordinal);
                _balances[accountId] = balances;
            }

            balances[currencyId] = amount;
        }

        public Task<CurrencyWriteResult> TryApplyAsync(
            long accountId,
            IReadOnlyList<CurrencyGrant> deltas,
            string reason,
            string referenceId,
            CancellationToken token)
        {
            if (!_balances.TryGetValue(accountId, out var balances))
            {
                balances = new Dictionary<string, long>(StringComparer.Ordinal);
                _balances[accountId] = balances;
            }

            if (!_applied.Add((accountId, reason, referenceId)))
            {
                return Task.FromResult(new CurrencyWriteResult(false, true, Snapshot(balances)));
            }

            var draft = new Dictionary<string, long>(balances, StringComparer.Ordinal);
            foreach (var grant in deltas)
            {
                draft.TryGetValue(grant.CurrencyId, out var current);
                var next = current + grant.Amount;
                if (next < 0)
                {
                    _applied.Remove((accountId, reason, referenceId));
                    return Task.FromResult(new CurrencyWriteResult(false, false, null));
                }

                draft[grant.CurrencyId] = next;
            }

            foreach (var grant in deltas)
            {
                Entries.Add((accountId, grant.CurrencyId, grant.Amount, reason));
            }

            _balances[accountId] = draft;
            return Task.FromResult(new CurrencyWriteResult(true, false, Snapshot(draft)));
        }

        private static CurrencyBalances Snapshot(Dictionary<string, long> balances) =>
            new(new Dictionary<string, long>(balances, StringComparer.Ordinal));
    }

    internal sealed class MemoryProfiles : IAccountProfileRepository
    {
        private readonly Dictionary<long, AccountProfileRecord> _profiles = new();

        public Task CreateAsync(long accountId)
        {
            _profiles[accountId] = new AccountProfileRecord(
                "avatar_01", "frame_white", "hero_gu_chenyue", "weapon_longsword", "pet_lingyu", 0, 0);
            return Task.CompletedTask;
        }

        public void SetTier(long accountId, int tier) =>
            _profiles[accountId] = _profiles[accountId] with { InventoryTier = tier };

        public Task<AccountProfileRecord?> FindProfileAsync(long accountId, CancellationToken token) =>
            Task.FromResult(_profiles.TryGetValue(accountId, out var profile) ? profile : null);

        public Task<CurrencyBalances?> FindBalancesAsync(long accountId, CancellationToken token) =>
            Task.FromResult<CurrencyBalances?>(
                new CurrencyBalances(new Dictionary<string, long>(StringComparer.Ordinal)));

        public Task<AccountProvisionResult> ProvisionAsync(
            long accountId,
            AccountProvisionDefaults defaults,
            CancellationToken token) =>
            throw new NotSupportedException();

        public Task<bool> UpdateAppearanceAsync(
            long accountId, string avatarId, string avatarFrameId, CancellationToken token) =>
            throw new NotSupportedException();

        public Task<bool> TryAdvanceInventoryTierAsync(
            long accountId, int expectedTier, int nextTier, CancellationToken token)
        {
            if (!_profiles.TryGetValue(accountId, out var profile) || profile.InventoryTier != expectedTier)
            {
                return Task.FromResult(false);
            }

            _profiles[accountId] = profile with { InventoryTier = nextTier };
            return Task.FromResult(true);
        }

        public Task<bool> UpdateLoadoutAsync(
            long accountId, string heroId, string weaponId, string petId, CancellationToken token) =>
            throw new NotSupportedException();
    }
}
