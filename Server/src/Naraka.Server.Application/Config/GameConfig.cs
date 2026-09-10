using System.Text.Json;
using Naraka.Config;

namespace Naraka.Server.Application.Config;

/// <summary>配置加载失败的原因。Host 启动时把任何一种都视为致命错误。</summary>
public enum GameConfigLoadStatus
{
    Success = 0,
    Missing = 1,
    Malformed = 2,
    SchemaMismatch = 3,
    Invalid = 4
}

public sealed record GameConfigLoadResult(GameConfigLoadStatus Status, GameConfig? Config, string Message)
{
    public bool IsSuccess => Status == GameConfigLoadStatus.Success && Config is not null;
}

/// <summary>
/// 服务端权威配置。经济、锻造、抽奖、签到与成就的一切判定只信任这里的数值，
/// 绝不接受客户端提交的价格、概率或奖励。
///
/// 只读且线程安全：加载后不可变，可以作为单例被所有连接共享。
/// </summary>
public sealed class GameConfig
{
    /// <summary>
    /// 服务端要求的配置结构版本。与 <c>Naraka.ConfigCompiler</c> 的 SchemaVersion 必须一致，
    /// 由测试断言，因此结构升级时不可能只改一边。
    /// </summary>
    public const string RequiredSchemaVersion = "1.0.0";

    private static readonly JsonSerializerOptions DeserializerOptions = new()
    {
        IncludeFields = true,
        // 生成物的属性名与模型字段名完全一致，因此不启用任何命名策略。
        PropertyNameCaseInsensitive = false
    };

    private readonly Dictionary<string, CurrencyConfig> _currencies;
    private readonly Dictionary<string, ItemConfig> _items;
    private readonly Dictionary<string, HeroConfig> _heroes;
    private readonly Dictionary<string, WeaponConfig> _weapons;
    private readonly Dictionary<(string WeaponId, int Level), WeaponLevelConfig> _weaponLevels;
    private readonly Dictionary<(string WeaponId, int FromLevel), ForgeRecipeConfig> _forgeRecipes;
    private readonly Dictionary<string, ShopProductConfig> _shopProducts;
    private readonly Dictionary<string, GachaPoolConfig> _gachaPools;
    private readonly Dictionary<string, GachaEntryConfig[]> _gachaEntriesByPool;
    private readonly Dictionary<int, SignInRewardConfig> _signInRewardsByDay;
    private readonly Dictionary<int, AccountLevelRewardConfig> _accountLevelsByLevel;
    private readonly Dictionary<string, AchievementConfig> _achievements;
    private readonly Dictionary<string, AvatarConfig> _avatars;
    private readonly Dictionary<string, AvatarFrameConfig> _avatarFrames;
    private readonly Dictionary<string, PetConfig> _pets;

    private GameConfig(NarakaConfigCatalog catalog, string catalogSha256)
    {
        Catalog = catalog;
        CatalogSha256 = catalogSha256;

        _currencies = Index(catalog.Currencies, value => value.CurrencyId);
        _items = Index(catalog.Items, value => value.ItemId);
        _heroes = Index(catalog.Heroes, value => value.HeroId);
        _weapons = Index(catalog.Weapons, value => value.WeaponId);
        _weaponLevels = catalog.WeaponLevels.ToDictionary(value => (value.WeaponId, value.Level));
        _forgeRecipes = catalog.ForgeRecipes.ToDictionary(value => (value.WeaponId, value.FromLevel));
        _shopProducts = Index(catalog.ShopProducts, value => value.ProductId);
        _gachaPools = Index(catalog.GachaPools, value => value.PoolId);
        _gachaEntriesByPool = catalog.GachaEntries
            .GroupBy(entry => entry.PoolId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(entry => entry.RewardId, StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
        _signInRewardsByDay = catalog.SignInRewards.ToDictionary(value => value.Day);
        _accountLevelsByLevel = catalog.AccountLevelRewards.ToDictionary(value => value.Level);
        _achievements = Index(catalog.Achievements, value => value.AchievementId);
        _avatars = Index(catalog.Avatars, value => value.AvatarId);
        _avatarFrames = Index(catalog.AvatarFrames, value => value.AvatarFrameId);
        _pets = Index(catalog.Pets, value => value.PetId);

        HeroesInDisplayOrder = catalog.Heroes.OrderBy(hero => hero.SortOrder).ToArray();
        WeaponsInDisplayOrder = catalog.Weapons.OrderBy(weapon => weapon.SortOrder).ToArray();
        AvatarsInDisplayOrder = catalog.Avatars.OrderBy(avatar => avatar.SortOrder).ToArray();
        AvatarFramesInDisplayOrder = catalog.AvatarFrames.OrderBy(frame => frame.SortOrder).ToArray();
        PetsInDisplayOrder = catalog.Pets.OrderBy(pet => pet.SortOrder).ToArray();
        SignInMilestones = catalog.SignInMilestones.OrderBy(value => value.MilestoneDays).ToArray();
        AccountLevelRewards = catalog.AccountLevelRewards.OrderBy(value => value.Level).ToArray();
        InventoryCapacities = catalog.InventoryCapacities.OrderBy(value => value.Tier).ToArray();
        Achievements = catalog.Achievements.OrderBy(value => value.SortOrder).ToArray();
        ShopProductsInDisplayOrder = catalog.ShopProducts.OrderBy(value => value.SortOrder).ToArray();
    }

    public NarakaConfigCatalog Catalog { get; }

    /// <summary>生成物的 SHA-256。用于与客户端比对，确认两端加载的是同一份配置。</summary>
    public string CatalogSha256 { get; }

    public string ConfigVersion => Catalog.ConfigVersion;

    public string SchemaVersion => Catalog.SchemaVersion;

    public IReadOnlyList<CurrencyConfig> Currencies => Catalog.Currencies;

    public IReadOnlyList<HeroConfig> HeroesInDisplayOrder { get; }

    public IReadOnlyList<WeaponConfig> WeaponsInDisplayOrder { get; }

    public IReadOnlyList<AvatarConfig> AvatarsInDisplayOrder { get; }

    public IReadOnlyList<AvatarFrameConfig> AvatarFramesInDisplayOrder { get; }

    public IReadOnlyList<PetConfig> PetsInDisplayOrder { get; }

    public IReadOnlyList<SignInMilestoneConfig> SignInMilestones { get; }

    public IReadOnlyList<AccountLevelRewardConfig> AccountLevelRewards { get; }

    public IReadOnlyList<InventoryCapacityConfig> InventoryCapacities { get; }

    public IReadOnlyList<AchievementConfig> Achievements { get; }

    public IReadOnlyList<ShopProductConfig> ShopProductsInDisplayOrder { get; }

    /// <summary>新账号的默认出战英雄。稳定 HeroId，绝不依赖列表下标。</summary>
    public string DefaultHeroId => HeroesInDisplayOrder[0].HeroId;

    /// <summary>新账号的默认出战武器。</summary>
    public string DefaultWeaponId => WeaponsInDisplayOrder[0].WeaponId;

    public string DefaultAvatarId => AvatarsInDisplayOrder[0].AvatarId;

    public string DefaultAvatarFrameId => AvatarFramesInDisplayOrder[0].AvatarFrameId;

    /// <summary>新账号默认拥有的宠物。配置保证至少存在一个。</summary>
    public string DefaultPetId => PetsInDisplayOrder.First(pet => pet.DefaultOwned).PetId;

    public int MaximumAccountLevel => AccountLevelRewards[^1].Level;

    public int InitialInventoryCapacity => InventoryCapacities[0].Capacity;

    public int MaximumInventoryTier => InventoryCapacities[^1].Tier;

    /// <summary>
    /// 从 JSON 正文加载。刻意不做文件访问，Application 层保持可测试且不依赖磁盘布局。
    /// </summary>
    public static GameConfigLoadResult Load(string json, string expectedSchemaVersion)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new GameConfigLoadResult(GameConfigLoadStatus.Missing, null, "配置正文为空。");
        }

        NarakaConfigCatalog? catalog;
        try
        {
            catalog = JsonSerializer.Deserialize<NarakaConfigCatalog>(json, DeserializerOptions);
        }
        catch (JsonException exception)
        {
            return new GameConfigLoadResult(
                GameConfigLoadStatus.Malformed, null, "配置 JSON 无法解析：" + exception.Message);
        }

        if (catalog is null)
        {
            return new GameConfigLoadResult(GameConfigLoadStatus.Malformed, null, "配置 JSON 反序列化结果为空。");
        }

        if (!string.Equals(catalog.SchemaVersion, expectedSchemaVersion, StringComparison.Ordinal))
        {
            return new GameConfigLoadResult(
                GameConfigLoadStatus.SchemaMismatch,
                null,
                $"配置 SchemaVersion 为 '{catalog.SchemaVersion}'，服务端要求 '{expectedSchemaVersion}'。");
        }

        // 运行时最低限度的完整性检查。完整的跨表校验由配置编译器完成，
        // 这里只保证服务端不会因为空表在第一次请求时崩溃。
        var missing = DescribeMissingEssentials(catalog);
        if (missing is not null)
        {
            return new GameConfigLoadResult(GameConfigLoadStatus.Invalid, null, missing);
        }

        var sha256 = Convert
            .ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(json)))
            .ToLowerInvariant();

        return new GameConfigLoadResult(
            GameConfigLoadStatus.Success, new GameConfig(catalog, sha256), string.Empty);
    }

    private static string? DescribeMissingEssentials(NarakaConfigCatalog catalog)
    {
        if (catalog.Currencies.Length == 0)
        {
            return "配置缺少货币定义。";
        }

        if (catalog.Heroes.Length == 0)
        {
            return "配置缺少英雄定义，无法确定新账号的默认出战英雄。";
        }

        if (catalog.Weapons.Length == 0)
        {
            return "配置缺少武器定义。";
        }

        if (catalog.Avatars.Length == 0 || catalog.AvatarFrames.Length == 0)
        {
            return "配置缺少头像或头像框定义。";
        }

        if (!catalog.Pets.Any(pet => pet.DefaultOwned))
        {
            return "配置缺少默认拥有的宠物。";
        }

        if (catalog.AccountLevelRewards.Length == 0)
        {
            return "配置缺少账号等级定义。";
        }

        return catalog.InventoryCapacities.Length == 0 ? "配置缺少仓库容量档位定义。" : null;
    }

    public bool TryGetCurrency(string currencyId, out CurrencyConfig? currency) =>
        _currencies.TryGetValue(currencyId ?? string.Empty, out currency);

    public bool TryGetItem(string itemId, out ItemConfig? item) =>
        _items.TryGetValue(itemId ?? string.Empty, out item);

    public bool TryGetHero(string heroId, out HeroConfig? hero) =>
        _heroes.TryGetValue(heroId ?? string.Empty, out hero);

    public bool TryGetWeapon(string weaponId, out WeaponConfig? weapon) =>
        _weapons.TryGetValue(weaponId ?? string.Empty, out weapon);

    public bool TryGetWeaponLevel(string weaponId, int level, out WeaponLevelConfig? weaponLevel) =>
        _weaponLevels.TryGetValue((weaponId ?? string.Empty, level), out weaponLevel);

    /// <summary>取得把武器从 <paramref name="fromLevel"/> 提升一级的配方。达到最高级时返回 false。</summary>
    public bool TryGetForgeRecipe(string weaponId, int fromLevel, out ForgeRecipeConfig? recipe) =>
        _forgeRecipes.TryGetValue((weaponId ?? string.Empty, fromLevel), out recipe);

    public bool TryGetShopProduct(string productId, out ShopProductConfig? product) =>
        _shopProducts.TryGetValue(productId ?? string.Empty, out product);

    public bool TryGetGachaPool(string poolId, out GachaPoolConfig? pool) =>
        _gachaPools.TryGetValue(poolId ?? string.Empty, out pool);

    public IReadOnlyList<GachaEntryConfig> GetGachaEntries(string poolId) =>
        _gachaEntriesByPool.TryGetValue(poolId ?? string.Empty, out var entries)
            ? entries
            : Array.Empty<GachaEntryConfig>();

    public bool TryGetSignInReward(int day, out SignInRewardConfig? reward) =>
        _signInRewardsByDay.TryGetValue(day, out reward);

    public bool TryGetAccountLevel(int level, out AccountLevelRewardConfig? reward) =>
        _accountLevelsByLevel.TryGetValue(level, out reward);

    public bool TryGetAchievement(string achievementId, out AchievementConfig? achievement) =>
        _achievements.TryGetValue(achievementId ?? string.Empty, out achievement);

    public bool TryGetAvatar(string avatarId, out AvatarConfig? avatar) =>
        _avatars.TryGetValue(avatarId ?? string.Empty, out avatar);

    public bool TryGetAvatarFrame(string avatarFrameId, out AvatarFrameConfig? frame) =>
        _avatarFrames.TryGetValue(avatarFrameId ?? string.Empty, out frame);

    public bool TryGetPet(string petId, out PetConfig? pet) =>
        _pets.TryGetValue(petId ?? string.Empty, out pet);

    /// <summary>档位对应的仓库容量。档位越界时回退到最近的合法档位，绝不返回 0 让仓库变成不可用。</summary>
    public int GetInventoryCapacity(int tier)
    {
        if (tier <= InventoryCapacities[0].Tier)
        {
            return InventoryCapacities[0].Capacity;
        }

        return tier >= InventoryCapacities[^1].Tier
            ? InventoryCapacities[^1].Capacity
            : InventoryCapacities.First(capacity => capacity.Tier == tier).Capacity;
    }

    /// <summary>下一次扩容的价格与容量。已达最高档时返回 false。</summary>
    public bool TryGetNextInventoryTier(int currentTier, out InventoryCapacityConfig? next)
    {
        next = InventoryCapacities.FirstOrDefault(capacity => capacity.Tier == currentTier + 1);
        return next is not null;
    }

    /// <summary>账号经验对应的等级。经验只来自任务，这里只做纯查表换算。</summary>
    public int ResolveAccountLevel(long accountXp)
    {
        var level = AccountLevelRewards[0].Level;
        foreach (var reward in AccountLevelRewards)
        {
            if (accountXp < reward.XpToReach)
            {
                break;
            }

            level = reward.Level;
        }

        return level;
    }

    private static Dictionary<string, T> Index<T>(IEnumerable<T> rows, Func<T, string> key) =>
        rows.ToDictionary(key, StringComparer.Ordinal);
}
