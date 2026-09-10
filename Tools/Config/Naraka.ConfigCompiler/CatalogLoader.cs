using Naraka.Config;
using Naraka.ConfigCompiler.Csv;

namespace Naraka.ConfigCompiler;

/// <summary>
/// 把 18 张 CSV 源表读成一份 <see cref="NarakaConfigCatalog"/>。
/// 这里只做逐行解析和逐行的形状校验；跨表引用与业务规则交给 <see cref="CatalogValidator"/>。
/// </summary>
public static class CatalogLoader
{
    /// <summary>源表文件名。顺序即读取顺序，也决定清单中源文件哈希的排列。</summary>
    public static readonly string[] SourceFiles =
    {
        "account_level_rewards.csv",
        "achievements.csv",
        "avatar_frames.csv",
        "avatars.csv",
        "currencies.csv",
        "forge_recipes.csv",
        "gacha_entries.csv",
        "gacha_pools.csv",
        "hero_skills.csv",
        "heroes.csv",
        "inventory_capacity.csv",
        "items.csv",
        "pets.csv",
        "shop_products.csv",
        "signin_milestones.csv",
        "signin_rewards.csv",
        "weapon_levels.csv",
        "weapons.csv"
    };

    public static NarakaConfigCatalog Load(string sourceDirectory, DiagnosticBag diagnostics)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var catalog = new NarakaConfigCatalog
        {
            Currencies = Read(sourceDirectory, "currencies.csv", diagnostics, ReadCurrency),
            Items = Read(sourceDirectory, "items.csv", diagnostics, ReadItem),
            Heroes = Read(sourceDirectory, "heroes.csv", diagnostics, ReadHero),
            HeroSkills = Read(sourceDirectory, "hero_skills.csv", diagnostics, ReadHeroSkill),
            Weapons = Read(sourceDirectory, "weapons.csv", diagnostics, ReadWeapon),
            WeaponLevels = Read(sourceDirectory, "weapon_levels.csv", diagnostics, ReadWeaponLevel),
            ForgeRecipes = Read(sourceDirectory, "forge_recipes.csv", diagnostics, ReadForgeRecipe),
            ShopProducts = Read(sourceDirectory, "shop_products.csv", diagnostics, ReadShopProduct),
            GachaPools = Read(sourceDirectory, "gacha_pools.csv", diagnostics, ReadGachaPool),
            GachaEntries = Read(sourceDirectory, "gacha_entries.csv", diagnostics, ReadGachaEntry),
            SignInRewards = Read(sourceDirectory, "signin_rewards.csv", diagnostics, ReadSignInReward),
            SignInMilestones = Read(sourceDirectory, "signin_milestones.csv", diagnostics, ReadSignInMilestone),
            AccountLevelRewards = Read(sourceDirectory, "account_level_rewards.csv", diagnostics, ReadAccountLevelReward),
            Achievements = Read(sourceDirectory, "achievements.csv", diagnostics, ReadAchievement),
            InventoryCapacities = Read(sourceDirectory, "inventory_capacity.csv", diagnostics, ReadInventoryCapacity),
            Avatars = Read(sourceDirectory, "avatars.csv", diagnostics, ReadAvatar),
            AvatarFrames = Read(sourceDirectory, "avatar_frames.csv", diagnostics, ReadAvatarFrame),
            Pets = Read(sourceDirectory, "pets.csv", diagnostics, ReadPet)
        };

        Sort(catalog);
        return catalog;
    }

    /// <summary>
    /// 按稳定 ID 排序。生成物的确定性完全依赖这一步：源表行序变化不得改变输出字节。
    /// </summary>
    private static void Sort(NarakaConfigCatalog catalog)
    {
        catalog.Currencies = Order(catalog.Currencies, value => value.CurrencyId);
        catalog.Items = Order(catalog.Items, value => value.ItemId);
        catalog.Heroes = Order(catalog.Heroes, value => value.HeroId);
        catalog.HeroSkills = Order(catalog.HeroSkills, value => value.SkillId);
        catalog.Weapons = Order(catalog.Weapons, value => value.WeaponId);
        catalog.WeaponLevels = catalog.WeaponLevels
            .OrderBy(value => value.WeaponId, StringComparer.Ordinal)
            .ThenBy(value => value.Level)
            .ToArray();
        catalog.ForgeRecipes = Order(catalog.ForgeRecipes, value => value.RecipeId);
        catalog.ShopProducts = Order(catalog.ShopProducts, value => value.ProductId);
        catalog.GachaPools = Order(catalog.GachaPools, value => value.PoolId);
        catalog.GachaEntries = catalog.GachaEntries
            .OrderBy(value => value.PoolId, StringComparer.Ordinal)
            .ThenBy(value => value.RewardId, StringComparer.Ordinal)
            .ToArray();
        catalog.SignInRewards = catalog.SignInRewards.OrderBy(value => value.Day).ToArray();
        catalog.SignInMilestones = catalog.SignInMilestones.OrderBy(value => value.MilestoneDays).ToArray();
        catalog.AccountLevelRewards = catalog.AccountLevelRewards.OrderBy(value => value.Level).ToArray();
        catalog.Achievements = Order(catalog.Achievements, value => value.AchievementId);
        catalog.InventoryCapacities = catalog.InventoryCapacities.OrderBy(value => value.Tier).ToArray();
        catalog.Avatars = Order(catalog.Avatars, value => value.AvatarId);
        catalog.AvatarFrames = Order(catalog.AvatarFrames, value => value.AvatarFrameId);
        catalog.Pets = Order(catalog.Pets, value => value.PetId);
    }

    private static T[] Order<T>(T[] source, Func<T, string> key) =>
        source.OrderBy(key, StringComparer.Ordinal).ToArray();

    private static T[] Read<T>(
        string sourceDirectory,
        string fileName,
        DiagnosticBag diagnostics,
        Func<CsvRecord, DiagnosticBag, T> factory)
    {
        var document = CsvDocument.Load(Path.Combine(sourceDirectory, fileName), diagnostics);
        return document.Records.Select(record => factory(record, diagnostics)).ToArray();
    }

    private static CurrencyConfig ReadCurrency(CsvRecord row, DiagnosticBag diagnostics) => new()
    {
        CurrencyId = row.GetRequiredString("CurrencyId", diagnostics),
        DisplayName = row.GetRequiredString("DisplayName", diagnostics),
        SortOrder = row.GetInt32("SortOrder", diagnostics),
        StarterGrant = row.GetInt64("StarterGrant", diagnostics),
        IconKey = row.GetString("IconKey", diagnostics),
        Description = row.GetString("Description", diagnostics)
    };

    private static ItemConfig ReadItem(CsvRecord row, DiagnosticBag diagnostics) => new()
    {
        ItemId = row.GetRequiredString("ItemId", diagnostics),
        DisplayName = row.GetRequiredString("DisplayName", diagnostics),
        Category = row.GetRequiredString("Category", diagnostics),
        Quality = row.GetRequiredString("Quality", diagnostics),
        StackLimit = row.GetInt32("StackLimit", diagnostics),
        SortOrder = row.GetInt32("SortOrder", diagnostics),
        IconKey = row.GetString("IconKey", diagnostics),
        SellCurrencyId = row.GetString("SellCurrencyId", diagnostics),
        SellPrice = row.GetInt64("SellPrice", diagnostics),
        Description = row.GetString("Description", diagnostics)
    };

    private static HeroConfig ReadHero(CsvRecord row, DiagnosticBag diagnostics) => new()
    {
        HeroId = row.GetRequiredString("HeroId", diagnostics),
        DisplayName = row.GetRequiredString("DisplayName", diagnostics),
        Title = row.GetString("Title", diagnostics),
        SortOrder = row.GetInt32("SortOrder", diagnostics),
        Health = row.GetInt32("Health", diagnostics),
        Attack = row.GetInt32("Attack", diagnostics),
        Defense = row.GetInt32("Defense", diagnostics),
        Stamina = row.GetInt32("Stamina", diagnostics),
        MoveSpeed = row.GetSingle("MoveSpeed", diagnostics),
        PortraitKey = row.GetString("PortraitKey", diagnostics),
        Background = row.GetString("Background", diagnostics)
    };

    private static HeroSkillConfig ReadHeroSkill(CsvRecord row, DiagnosticBag diagnostics) => new()
    {
        SkillId = row.GetRequiredString("SkillId", diagnostics),
        HeroId = row.GetRequiredString("HeroId", diagnostics),
        SlotKey = row.GetRequiredString("SlotKey", diagnostics),
        DisplayName = row.GetRequiredString("DisplayName", diagnostics),
        SortOrder = row.GetInt32("SortOrder", diagnostics),
        CooldownSeconds = row.GetSingle("CooldownSeconds", diagnostics),
        DamageMultiplier = row.GetSingle("DamageMultiplier", diagnostics),
        RangeMeters = row.GetSingle("RangeMeters", diagnostics),
        IconKey = row.GetString("IconKey", diagnostics),
        Description = row.GetString("Description", diagnostics),
        EffectSummary = row.GetString("EffectSummary", diagnostics)
    };

    private static WeaponConfig ReadWeapon(CsvRecord row, DiagnosticBag diagnostics) => new()
    {
        WeaponId = row.GetRequiredString("WeaponId", diagnostics),
        DisplayName = row.GetRequiredString("DisplayName", diagnostics),
        SortOrder = row.GetInt32("SortOrder", diagnostics),
        MaxLevel = row.GetInt32("MaxLevel", diagnostics),
        IconKey = row.GetString("IconKey", diagnostics),
        Description = row.GetString("Description", diagnostics)
    };

    private static WeaponLevelConfig ReadWeaponLevel(CsvRecord row, DiagnosticBag diagnostics) => new()
    {
        WeaponId = row.GetRequiredString("WeaponId", diagnostics),
        Level = row.GetInt32("Level", diagnostics),
        Attack = row.GetInt32("Attack", diagnostics),
        AttackSpeed = row.GetSingle("AttackSpeed", diagnostics)
    };

    private static ForgeRecipeConfig ReadForgeRecipe(CsvRecord row, DiagnosticBag diagnostics) => new()
    {
        RecipeId = row.GetRequiredString("RecipeId", diagnostics),
        WeaponId = row.GetRequiredString("WeaponId", diagnostics),
        FromLevel = row.GetInt32("FromLevel", diagnostics),
        ToLevel = row.GetInt32("ToLevel", diagnostics),
        CurrencyId = row.GetRequiredString("CurrencyId", diagnostics),
        CurrencyAmount = row.GetInt64("CurrencyAmount", diagnostics),
        Material1ItemId = row.GetString("Material1ItemId", diagnostics),
        Material1Amount = row.GetInt32("Material1Amount", diagnostics),
        Material2ItemId = row.GetString("Material2ItemId", diagnostics),
        Material2Amount = row.GetInt32("Material2Amount", diagnostics)
    };

    private static ShopProductConfig ReadShopProduct(CsvRecord row, DiagnosticBag diagnostics) => new()
    {
        ProductId = row.GetRequiredString("ProductId", diagnostics),
        ItemId = row.GetRequiredString("ItemId", diagnostics),
        ShopCategory = row.GetRequiredString("ShopCategory", diagnostics),
        CurrencyId = row.GetRequiredString("CurrencyId", diagnostics),
        UnitPrice = row.GetInt64("UnitPrice", diagnostics),
        PurchaseLimit = row.GetInt32("PurchaseLimit", diagnostics),
        SortOrder = row.GetInt32("SortOrder", diagnostics),
        IsAvailable = row.GetBoolean("IsAvailable", diagnostics)
    };

    private static GachaPoolConfig ReadGachaPool(CsvRecord row, DiagnosticBag diagnostics) => new()
    {
        PoolId = row.GetRequiredString("PoolId", diagnostics),
        DisplayName = row.GetRequiredString("DisplayName", diagnostics),
        CurrencyId = row.GetRequiredString("CurrencyId", diagnostics),
        SinglePrice = row.GetInt64("SinglePrice", diagnostics),
        TenPullPrice = row.GetInt64("TenPullPrice", diagnostics),
        TenPullMinimumQuality = row.GetRequiredString("TenPullMinimumQuality", diagnostics),
        PityCount = row.GetInt32("PityCount", diagnostics),
        PityQuality = row.GetRequiredString("PityQuality", diagnostics),
        SortOrder = row.GetInt32("SortOrder", diagnostics),
        Description = row.GetString("Description", diagnostics)
    };

    private static GachaEntryConfig ReadGachaEntry(CsvRecord row, DiagnosticBag diagnostics) => new()
    {
        PoolId = row.GetRequiredString("PoolId", diagnostics),
        RewardId = row.GetRequiredString("RewardId", diagnostics),
        ItemId = row.GetRequiredString("ItemId", diagnostics),
        Amount = row.GetInt32("Amount", diagnostics),
        Quality = row.GetRequiredString("Quality", diagnostics),
        Weight = row.GetInt32("Weight", diagnostics)
    };

    private static SignInRewardConfig ReadSignInReward(CsvRecord row, DiagnosticBag diagnostics) => new()
    {
        Day = row.GetInt32("Day", diagnostics),
        RewardId = row.GetRequiredString("RewardId", diagnostics),
        RewardKind = row.GetRequiredString("RewardKind", diagnostics),
        ItemId = row.GetString("ItemId", diagnostics),
        ItemAmount = row.GetInt32("ItemAmount", diagnostics),
        CurrencyId = row.GetString("CurrencyId", diagnostics),
        CurrencyAmount = row.GetInt64("CurrencyAmount", diagnostics)
    };

    private static SignInMilestoneConfig ReadSignInMilestone(CsvRecord row, DiagnosticBag diagnostics) => new()
    {
        MilestoneDays = row.GetInt32("MilestoneDays", diagnostics),
        RewardId = row.GetRequiredString("RewardId", diagnostics),
        RewardKind = row.GetRequiredString("RewardKind", diagnostics),
        ItemId = row.GetString("ItemId", diagnostics),
        ItemAmount = row.GetInt32("ItemAmount", diagnostics),
        CurrencyId = row.GetString("CurrencyId", diagnostics),
        CurrencyAmount = row.GetInt64("CurrencyAmount", diagnostics)
    };

    private static AccountLevelRewardConfig ReadAccountLevelReward(CsvRecord row, DiagnosticBag diagnostics) => new()
    {
        Level = row.GetInt32("Level", diagnostics),
        XpToReach = row.GetInt64("XpToReach", diagnostics),
        RewardId = row.GetRequiredString("RewardId", diagnostics),
        RewardKind = row.GetRequiredString("RewardKind", diagnostics),
        ItemId = row.GetString("ItemId", diagnostics),
        ItemAmount = row.GetInt32("ItemAmount", diagnostics),
        CurrencyId = row.GetString("CurrencyId", diagnostics),
        CurrencyAmount = row.GetInt64("CurrencyAmount", diagnostics)
    };

    private static AchievementConfig ReadAchievement(CsvRecord row, DiagnosticBag diagnostics) => new()
    {
        AchievementId = row.GetRequiredString("AchievementId", diagnostics),
        Category = row.GetRequiredString("Category", diagnostics),
        DisplayName = row.GetRequiredString("DisplayName", diagnostics),
        Description = row.GetString("Description", diagnostics),
        SortOrder = row.GetInt32("SortOrder", diagnostics),
        TargetProgress = row.GetInt64("TargetProgress", diagnostics),
        AchievementXp = row.GetInt64("AchievementXp", diagnostics),
        SourceEvent = row.GetRequiredString("SourceEvent", diagnostics),
        IsActiveInP1 = row.GetBoolean("IsActiveInP1", diagnostics),
        RewardKind = row.GetRequiredString("RewardKind", diagnostics),
        ItemId = row.GetString("ItemId", diagnostics),
        ItemAmount = row.GetInt32("ItemAmount", diagnostics),
        CurrencyId = row.GetString("CurrencyId", diagnostics),
        CurrencyAmount = row.GetInt64("CurrencyAmount", diagnostics)
    };

    private static InventoryCapacityConfig ReadInventoryCapacity(CsvRecord row, DiagnosticBag diagnostics) => new()
    {
        Tier = row.GetInt32("Tier", diagnostics),
        Capacity = row.GetInt32("Capacity", diagnostics),
        ExpandCurrencyId = row.GetRequiredString("ExpandCurrencyId", diagnostics),
        ExpandPrice = row.GetInt64("ExpandPrice", diagnostics)
    };

    private static AvatarConfig ReadAvatar(CsvRecord row, DiagnosticBag diagnostics) => new()
    {
        AvatarId = row.GetRequiredString("AvatarId", diagnostics),
        DisplayName = row.GetRequiredString("DisplayName", diagnostics),
        SortOrder = row.GetInt32("SortOrder", diagnostics),
        IconKey = row.GetRequiredString("IconKey", diagnostics),
        UnlockRule = row.GetRequiredString("UnlockRule", diagnostics)
    };

    private static AvatarFrameConfig ReadAvatarFrame(CsvRecord row, DiagnosticBag diagnostics) => new()
    {
        AvatarFrameId = row.GetRequiredString("AvatarFrameId", diagnostics),
        DisplayName = row.GetRequiredString("DisplayName", diagnostics),
        SortOrder = row.GetInt32("SortOrder", diagnostics),
        IconKey = row.GetRequiredString("IconKey", diagnostics),
        UnlockRule = row.GetRequiredString("UnlockRule", diagnostics)
    };

    private static PetConfig ReadPet(CsvRecord row, DiagnosticBag diagnostics) => new()
    {
        PetId = row.GetRequiredString("PetId", diagnostics),
        DisplayName = row.GetRequiredString("DisplayName", diagnostics),
        SortOrder = row.GetInt32("SortOrder", diagnostics),
        IconKey = row.GetString("IconKey", diagnostics),
        DefaultOwned = row.GetBoolean("DefaultOwned", diagnostics),
        UnlockRule = row.GetRequiredString("UnlockRule", diagnostics),
        Description = row.GetString("Description", diagnostics)
    };
}
