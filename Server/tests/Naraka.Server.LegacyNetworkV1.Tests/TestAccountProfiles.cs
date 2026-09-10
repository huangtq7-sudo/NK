using System.Collections.Concurrent;
using Naraka.Server.Application.Config;
using Naraka.Server.Application.Forge;
using Naraka.Server.Application.Gacha;
using Naraka.Server.Application.Inventory;
using Naraka.Server.Application.Progression;
using Naraka.Server.Application.Social;
using Naraka.Server.Application.Sessions;
using Naraka.Server.Application.Shop;

namespace Naraka.Server.LegacyNetworkV1.Tests;

/// <summary>
/// 内存账号资料仓储与真实生成配置。
///
/// 传输层测试关心的是路由、认证与序列化，因此这里用内存实现替代 MySQL，
/// 但配置仍然读取仓库中真实生成的 naraka-config.json：默认英雄、默认头像与初始赠送数量
/// 必须与线上完全一致，否则这些测试证明不了任何东西。
/// </summary>
internal static class TestAccountProfiles
{
    private static readonly Lazy<GameConfig> LazyConfig = new(() =>
    {
        var json = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Config", "naraka-config.json"),
            System.Text.Encoding.UTF8);
        var result = GameConfig.Load(json, GameConfig.RequiredSchemaVersion);
        return result.Config
               ?? throw new InvalidOperationException("测试无法加载生成配置：" + result.Message);
    });

    public static GameConfig Config => LazyConfig.Value;

    public static AccountProfileService Service(MemoryAccountProfileRepository? repository = null) =>
        new(repository ?? new MemoryAccountProfileRepository(), Config);

    /// <summary>
    /// 传输层测试用的仓库服务。这些测试关心的是路由与认证，
    /// 仓库存储用内存实现即可，但配置仍然是仓库中真实生成的那一份。
    /// </summary>
    public static InventoryService Inventory(MemoryAccountProfileRepository? profiles = null) =>
        new(new MemoryInventoryRepository(),
            new MemoryCurrencyLedgerRepository(),
            profiles ?? new MemoryAccountProfileRepository(),
            Config);

    public static ShopService Shop(MemoryAccountProfileRepository? profiles = null) =>
        new(new MemoryShopRepository(), profiles ?? new MemoryAccountProfileRepository(), Config);

    public static ForgeService Forge(MemoryAccountProfileRepository? profiles = null) =>
        new(new MemoryWeaponRepository(),
            new MemoryInventoryRepository(),
            profiles ?? new MemoryAccountProfileRepository(),
            Config);

    public static GachaService Gacha(MemoryAccountProfileRepository? profiles = null) =>
        new(new MemoryGachaRepository(),
            profiles ?? new MemoryAccountProfileRepository(),
            Config,
            new CryptoGachaRandom());

    public static SignInService SignIn(MemoryAccountProfileRepository? profiles = null) =>
        new(new MemoryProgressionRepository(),
            new MemoryInventoryRepository(),
            profiles ?? new MemoryAccountProfileRepository(),
            Config);

    public static AchievementService Achievements(MemoryAccountProfileRepository? profiles = null)
    {
        var progression = new MemoryProgressionRepository();
        return new AchievementService(
            progression, progression, profiles ?? new MemoryAccountProfileRepository(), Config);
    }

    public static RedDotService RedDots() => new(new MemoryRedDotRepository());

    public static SocialService Socials(IAuthenticatedSessionRegistry? sessions = null) =>
        new(new MemorySocialRepository(),
            new MemoryChatRepository(),
            sessions ?? new InMemoryAuthenticatedSessionRegistry());
}

/// <summary>内存社交仓储。传输层测试只需要一个能返回稳定结果的实现。</summary>
internal sealed class MemorySocialRepository : ISocialRepository
{
    public Task<SocialPlayer?> FindByAccountIdAsync(
        long accountId,
        CancellationToken cancellationToken) =>
        Task.FromResult<SocialPlayer?>(null);

    public Task<SocialPlayer?> FindByDisplayNameAsync(
        string displayName,
        CancellationToken cancellationToken) =>
        Task.FromResult<SocialPlayer?>(null);

    public Task<IReadOnlyList<SocialPlayer>> ListFriendsAsync(
        long accountId,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SocialPlayer>>(Array.Empty<SocialPlayer>());

    public Task<IReadOnlyList<FriendRequestEntry>> ListIncomingRequestsAsync(
        long accountId,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<FriendRequestEntry>>(Array.Empty<FriendRequestEntry>());

    public Task<IReadOnlyList<long>> ListOutgoingRequestTargetsAsync(
        long accountId,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<long>>(Array.Empty<long>());

    public Task<IReadOnlyList<SocialPlayer>> ListBlockedAsync(
        long accountId,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SocialPlayer>>(Array.Empty<SocialPlayer>());

    public Task<bool> IsFriendAsync(
        long accountId,
        long otherAccountId,
        CancellationToken cancellationToken) =>
        Task.FromResult(false);

    public Task<bool> IsBlockedEitherWayAsync(
        long accountId,
        long otherAccountId,
        CancellationToken cancellationToken) =>
        Task.FromResult(false);

    public Task<FriendRequestOutcome> TrySendRequestAsync(
        long requesterAccountId,
        long targetAccountId,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        Task.FromResult(FriendRequestOutcome.Applied);

    public Task<bool> TryAcceptRequestAsync(
        long targetAccountId,
        long requesterAccountId,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        Task.FromResult(true);

    public Task<bool> TryRejectRequestAsync(
        long targetAccountId,
        long requesterAccountId,
        CancellationToken cancellationToken) =>
        Task.FromResult(true);

    public Task<bool> TryRemoveFriendAsync(
        long accountId,
        long friendAccountId,
        CancellationToken cancellationToken) =>
        Task.FromResult(true);

    public Task TryBlockAsync(
        long accountId,
        long blockedAccountId,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task<bool> TryUnblockAsync(
        long accountId,
        long blockedAccountId,
        CancellationToken cancellationToken) =>
        Task.FromResult(true);
}

/// <summary>内存聊天仓储。</summary>
internal sealed class MemoryChatRepository : IChatRepository
{
    public Task<long> EnsureConversationAsync(
        long accountId,
        long peerAccountId,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        Task.FromResult(1L);

    public Task<IReadOnlyList<ChatConversation>> ListConversationsAsync(
        long accountId,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ChatConversation>>(Array.Empty<ChatConversation>());

    public Task<IReadOnlyList<ChatMessage>> ListMessagesAsync(
        long conversationId,
        int limit,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ChatMessage>>(Array.Empty<ChatMessage>());

    public Task<ChatMessage> AppendMessageAsync(
        long conversationId,
        long senderAccountId,
        string body,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        Task.FromResult(new ChatMessage(1, conversationId, senderAccountId, body, nowUtc));

    public Task MarkReadAsync(
        long conversationId,
        long accountId,
        long lastReadMessageId,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task<(long Low, long High)?> FindParticipantsAsync(
        long conversationId,
        CancellationToken cancellationToken) =>
        Task.FromResult<(long, long)?>(null);

    public Task<int> CountRecentMessagesAsync(
        long accountId,
        DateTime sinceUtc,
        CancellationToken cancellationToken) =>
        Task.FromResult(0);
}

/// <summary>内存红点仓储。只保存版本对，不判断任何业务规则。</summary>
internal sealed class MemoryRedDotRepository : IRedDotRepository
{
    private readonly Dictionary<string, RedDotStateRow> _rows = new(StringComparer.Ordinal);

    public Task<IReadOnlyList<RedDotStateRow>> ListAsync(
        long accountId,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RedDotStateRow>>(_rows.Values.ToArray());

    public Task SaveSeenAsync(
        long accountId,
        string path,
        long seenVersion,
        CancellationToken cancellationToken)
    {
        _rows[path] = new RedDotStateRow(path, seenVersion, seenVersion);
        return Task.CompletedTask;
    }
}

/// <summary>内存签到与成就仓储。传输层测试只需要一个能返回稳定结果的实现。</summary>
internal sealed class MemoryProgressionRepository : ISignInRepository, IAchievementRepository
{
    public Task<SignInState?> FindStateAsync(long accountId, CancellationToken cancellationToken) =>
        Task.FromResult<SignInState?>(null);

    public Task<IReadOnlyList<string>> ListClaimedRewardsAsync(
        long accountId,
        string rewardKind,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

    public Task<ClaimOutcome> TryClaimSignInAsync(
        SignInClaimCommand command,
        CancellationToken cancellationToken) =>
        Task.FromResult(ClaimOutcome.Applied);

    public Task<ClaimOutcome> TryClaimRewardAsync(
        RewardClaimCommand command,
        CancellationToken cancellationToken) =>
        Task.FromResult(ClaimOutcome.Applied);

    public Task<IReadOnlyList<AchievementProgress>> ListProgressAsync(
        long accountId,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AchievementProgress>>(Array.Empty<AchievementProgress>());

    public Task<long> GetAchievementXpAsync(long accountId, CancellationToken cancellationToken) =>
        Task.FromResult(0L);

    public Task<ClaimOutcome> TryClaimAchievementAsync(
        RewardClaimCommand command,
        long achievementXpGain,
        CancellationToken cancellationToken) =>
        Task.FromResult(ClaimOutcome.Applied);

    public Task<long> AddProgressAsync(
        long accountId,
        string achievementId,
        long delta,
        CancellationToken cancellationToken) =>
        Task.FromResult(delta);
}

/// <summary>内存抽奖仓储。传输层测试只需要一个能返回稳定结果的实现。</summary>
internal sealed class MemoryGachaRepository : IGachaRepository
{
    public Task<GachaAccountState?> FindStateAsync(
        long accountId,
        string poolId,
        CancellationToken cancellationToken) =>
        Task.FromResult<GachaAccountState?>(new GachaAccountState(poolId, 0, 0));

    public Task<IReadOnlyList<GachaOrder>> ListUnshownOrdersAsync(
        long accountId,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<GachaOrder>>(Array.Empty<GachaOrder>());

    public Task<GachaPullResult> TryPullAsync(GachaPullCommand command, CancellationToken cancellationToken) =>
        Task.FromResult(new GachaPullResult(
            GachaPullOutcome.Applied,
            new GachaOrder(command.OrderId, command.PoolId, command.PullCount, false, command.Rewards)));

    public Task<bool> MarkShownAsync(long accountId, string orderId, CancellationToken cancellationToken) =>
        Task.FromResult(true);
}

/// <summary>内存武器仓储。传输层测试只需要一个能返回稳定结果的实现。</summary>
internal sealed class MemoryWeaponRepository : IWeaponRepository
{
    private readonly Dictionary<long, Dictionary<string, AccountWeapon>> _weapons = new();

    public Task<IReadOnlyList<AccountWeapon>> ListAsync(long accountId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AccountWeapon>>(
            _weapons.TryGetValue(accountId, out var owned)
                ? owned.Values.OrderBy(weapon => weapon.WeaponId, StringComparer.Ordinal).ToArray()
                : Array.Empty<AccountWeapon>());

    public Task EnsureWeaponsAsync(
        long accountId,
        IReadOnlyList<string> weaponIds,
        CancellationToken cancellationToken)
    {
        if (!_weapons.TryGetValue(accountId, out var owned))
        {
            owned = new Dictionary<string, AccountWeapon>(StringComparer.Ordinal);
            _weapons[accountId] = owned;
        }

        foreach (var weaponId in weaponIds)
        {
            if (!owned.ContainsKey(weaponId))
            {
                owned[weaponId] = new AccountWeapon(weaponId, 1, 0, 0);
            }
        }

        return Task.CompletedTask;
    }

    public Task<ForgeUpgradeResult> TryUpgradeAsync(
        ForgeUpgradeCommand command,
        CancellationToken cancellationToken)
    {
        if (!_weapons.TryGetValue(command.AccountId, out var owned) ||
            !owned.TryGetValue(command.WeaponId, out var weapon))
        {
            return Task.FromResult(new ForgeUpgradeResult(ForgeUpgradeOutcome.AccountMissing, 0));
        }

        owned[command.WeaponId] = weapon with { Level = command.ToLevel };
        return Task.FromResult(new ForgeUpgradeResult(ForgeUpgradeOutcome.Applied, command.ToLevel));
    }
}

/// <summary>内存商店仓储。传输层测试只需要一个能返回稳定结果的实现。</summary>
internal sealed class MemoryShopRepository : IShopRepository
{
    public Task<IReadOnlyList<ShopPurchaseCount>> ListPurchasesAsync(
        long accountId,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ShopPurchaseCount>>(Array.Empty<ShopPurchaseCount>());

    public Task<ShopPurchaseResult> TryPurchaseAsync(
        ShopPurchaseCommand command,
        CancellationToken cancellationToken) =>
        Task.FromResult(new ShopPurchaseResult(ShopPurchaseOutcome.Applied, null, command.Quantity));
}

/// <summary>内存仓库存储。只实现事务语义中"要么全成功要么全不写"的可观察部分。</summary>
internal sealed class MemoryInventoryRepository : IInventoryRepository
{
    private readonly Dictionary<long, Dictionary<string, InventorySlot>> _slots = new();
    private readonly Dictionary<long, Dictionary<(string Kind, int Index), string>> _equipment = new();

    public Task<IReadOnlyList<InventorySlot>> ListSlotsAsync(long accountId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<InventorySlot>>(
            _slots.TryGetValue(accountId, out var slots)
                ? slots.Values.OrderBy(slot => slot.SlotIndex).ToArray()
                : Array.Empty<InventorySlot>());

    public Task<IReadOnlyList<EquipmentSlot>> ListEquipmentAsync(
        long accountId,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<EquipmentSlot>>(
            _equipment.TryGetValue(accountId, out var slots)
                ? slots.Select(pair => new EquipmentSlot(pair.Key.Kind, pair.Key.Index, pair.Value))
                    .OrderBy(slot => slot.SlotKind, StringComparer.Ordinal)
                    .ThenBy(slot => slot.SlotIndex)
                    .ToArray()
                : Array.Empty<EquipmentSlot>());

    public Task<bool> TryApplyAsync(
        long accountId,
        IReadOnlyList<InventoryDelta> deltas,
        IReadOnlyDictionary<string, int> stackLimits,
        int capacity,
        CancellationToken cancellationToken)
    {
        if (!_slots.TryGetValue(accountId, out var slots))
        {
            slots = new Dictionary<string, InventorySlot>(StringComparer.Ordinal);
            _slots[accountId] = slots;
        }

        // 先在副本上算完再落盘，保证失败时一格也不改。
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
                draft[group.Key] = new InventorySlot(
                    group.Key, next, existing?.SlotIndex ?? nextSlot++);
            }
        }

        if (draft.Count > capacity)
        {
            return Task.FromResult(false);
        }

        _slots[accountId] = draft;
        return Task.FromResult(true);
    }

    public Task<bool> ReorderAsync(
        long accountId,
        IReadOnlyList<string> itemIdsInSlotOrder,
        CancellationToken cancellationToken)
    {
        if (!_slots.TryGetValue(accountId, out var slots) || slots.Count != itemIdsInSlotOrder.Count)
        {
            return Task.FromResult(false);
        }

        var draft = new Dictionary<string, InventorySlot>(StringComparer.Ordinal);
        for (var index = 0; index < itemIdsInSlotOrder.Count; index++)
        {
            if (!slots.TryGetValue(itemIdsInSlotOrder[index], out var slot))
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
        CancellationToken cancellationToken)
    {
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
}

/// <summary>内存货币流水。同一个引用号只记一次账，与数据库唯一键的作用一致。</summary>
internal sealed class MemoryCurrencyLedgerRepository : ICurrencyLedgerRepository
{
    private readonly Dictionary<long, Dictionary<string, long>> _balances = new();
    private readonly HashSet<(long AccountId, string Reason, string ReferenceId)> _applied = new();

    public List<(long AccountId, string CurrencyId, long Delta, string Reason)> Ledger { get; } = new();

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
        CancellationToken cancellationToken)
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
            Ledger.Add((accountId, grant.CurrencyId, grant.Amount, reason));
        }

        _balances[accountId] = draft;
        return Task.FromResult(new CurrencyWriteResult(true, false, Snapshot(draft)));
    }

    private static CurrencyBalances Snapshot(Dictionary<string, long> balances) =>
        new(new Dictionary<string, long>(balances, StringComparer.Ordinal));
}

internal sealed class MemoryAccountProfileRepository : IAccountProfileRepository
{
    private readonly ConcurrentDictionary<long, AccountProfileRecord> _profiles = new();
    private readonly ConcurrentDictionary<long, Dictionary<string, long>> _balances = new();
    private readonly ConcurrentDictionary<long, HashSet<string>> _grants = new();

    /// <summary>写入的不可变流水。测试据此断言"余额变化必然伴随一条流水"。</summary>
    public List<(long AccountId, string CurrencyId, long Delta, long BalanceAfter, string Reason)> Ledger { get; } =
        new();

    public bool FailWithStorageError { get; set; }

    public Task<AccountProfileRecord?> FindProfileAsync(long accountId, CancellationToken cancellationToken)
    {
        ThrowIfFaulted();
        return Task.FromResult(_profiles.TryGetValue(accountId, out var profile) ? profile : null);
    }

    public Task<CurrencyBalances?> FindBalancesAsync(long accountId, CancellationToken cancellationToken)
    {
        ThrowIfFaulted();
        return Task.FromResult<CurrencyBalances?>(
            _balances.TryGetValue(accountId, out var balances)
                ? new CurrencyBalances(new Dictionary<string, long>(balances, StringComparer.Ordinal))
                : null);
    }

    public Task<AccountProvisionResult> ProvisionAsync(
        long accountId,
        AccountProvisionDefaults defaults,
        CancellationToken cancellationToken)
    {
        ThrowIfFaulted();

        var profileCreated = false;
        var profile = _profiles.GetOrAdd(accountId, _ =>
        {
            profileCreated = true;
            return new AccountProfileRecord(
                defaults.AvatarId,
                defaults.AvatarFrameId,
                defaults.HeroId,
                defaults.WeaponId,
                defaults.PetId,
                0,
                0);
        });

        var balances = _balances.GetOrAdd(accountId, _ => new Dictionary<string, long>(StringComparer.Ordinal));
        var granted = _grants.GetOrAdd(accountId, _ => new HashSet<string>(StringComparer.Ordinal));

        var starterGrantApplied = false;
        lock (granted)
        {
            if (granted.Add(AccountGrantKey.StarterGrant))
            {
                foreach (var grant in defaults.StarterGrants)
                {
                    balances.TryGetValue(grant.CurrencyId, out var current);
                    var next = current + grant.Amount;
                    balances[grant.CurrencyId] = next;
                    Ledger.Add((accountId, grant.CurrencyId, grant.Amount, next,
                        CurrencyLedgerReason.StarterGrant));
                }

                starterGrantApplied = true;
            }
        }

        return Task.FromResult(new AccountProvisionResult(
            profile,
            new CurrencyBalances(new Dictionary<string, long>(balances, StringComparer.Ordinal)),
            profileCreated,
            starterGrantApplied));
    }

    public Task<bool> TryAdvanceInventoryTierAsync(
        long accountId,
        int expectedTier,
        int nextTier,
        CancellationToken cancellationToken)
    {
        ThrowIfFaulted();
        if (!_profiles.TryGetValue(accountId, out var profile) || profile.InventoryTier != expectedTier)
        {
            return Task.FromResult(false);
        }

        _profiles[accountId] = profile with { InventoryTier = nextTier };
        return Task.FromResult(true);
    }

    public Task<bool> UpdateAppearanceAsync(
        long accountId,
        string avatarId,
        string avatarFrameId,
        CancellationToken cancellationToken)
    {
        ThrowIfFaulted();
        if (!_profiles.TryGetValue(accountId, out var profile))
        {
            return Task.FromResult(false);
        }

        _profiles[accountId] = profile with { AvatarId = avatarId, AvatarFrameId = avatarFrameId };
        return Task.FromResult(true);
    }

    public Task<bool> UpdateLoadoutAsync(
        long accountId,
        string heroId,
        string weaponId,
        string petId,
        CancellationToken cancellationToken)
    {
        ThrowIfFaulted();
        if (!_profiles.TryGetValue(accountId, out var profile))
        {
            return Task.FromResult(false);
        }

        _profiles[accountId] = profile with
        {
            SelectedHeroId = heroId,
            SelectedWeaponId = weaponId,
            SelectedPetId = petId
        };
        return Task.FromResult(true);
    }

    private void ThrowIfFaulted()
    {
        if (FailWithStorageError)
        {
            throw new AccountProfileStorageException("注入的存储故障。");
        }
    }
}
