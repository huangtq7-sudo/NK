using System.Globalization;
using Naraka.Config;
using Naraka.ConfigCompiler.Csv;

namespace Naraka.ConfigCompiler;

/// <summary>
/// 跨表引用与业务规则校验。经济与抽奖判定完全信任生成后的配置，
/// 因此任何非法配置都必须在这里被拦住，而不是留到运行时。
/// </summary>
public static class CatalogValidator
{
    public static void Validate(NarakaConfigCatalog catalog, DiagnosticBag diagnostics)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var currencies = RequireUniqueIds(
            catalog.Currencies, value => value.CurrencyId, "currencies.csv", "CurrencyId", diagnostics);
        var items = RequireUniqueIds(
            catalog.Items, value => value.ItemId, "items.csv", "ItemId", diagnostics);
        var heroes = RequireUniqueIds(
            catalog.Heroes, value => value.HeroId, "heroes.csv", "HeroId", diagnostics);
        RequireUniqueIds(catalog.HeroSkills, value => value.SkillId, "hero_skills.csv", "SkillId", diagnostics);
        var weapons = RequireUniqueIds(
            catalog.Weapons, value => value.WeaponId, "weapons.csv", "WeaponId", diagnostics);
        RequireUniqueIds(catalog.ForgeRecipes, value => value.RecipeId, "forge_recipes.csv", "RecipeId", diagnostics);
        RequireUniqueIds(catalog.ShopProducts, value => value.ProductId, "shop_products.csv", "ProductId", diagnostics);
        var pools = RequireUniqueIds(
            catalog.GachaPools, value => value.PoolId, "gacha_pools.csv", "PoolId", diagnostics);
        RequireUniqueIds(catalog.GachaEntries, value => value.RewardId, "gacha_entries.csv", "RewardId", diagnostics);
        RequireUniqueIds(catalog.Achievements, value => value.AchievementId, "achievements.csv", "AchievementId", diagnostics);
        RequireUniqueIds(catalog.Avatars, value => value.AvatarId, "avatars.csv", "AvatarId", diagnostics);
        RequireUniqueIds(catalog.AvatarFrames, value => value.AvatarFrameId, "avatar_frames.csv", "AvatarFrameId", diagnostics);
        RequireUniqueIds(catalog.Pets, value => value.PetId, "pets.csv", "PetId", diagnostics);

        ValidateCurrencies(catalog, diagnostics);
        ValidateItems(catalog, currencies, diagnostics);
        ValidateHeroSkills(catalog, heroes, diagnostics);
        ValidateWeaponLevels(catalog, weapons, diagnostics);
        ValidateForgeRecipes(catalog, currencies, items, weapons, diagnostics);
        ValidateShopProducts(catalog, currencies, items, diagnostics);
        ValidateGacha(catalog, currencies, items, pools, diagnostics);
        ValidateSignIn(catalog, currencies, items, diagnostics);
        ValidateAccountLevelRewards(catalog, currencies, items, diagnostics);
        ValidateAchievements(catalog, currencies, items, diagnostics);
        ValidateInventoryCapacity(catalog, currencies, diagnostics);
        ValidateAppearanceAndPets(catalog, diagnostics);
    }

    private static void ValidateCurrencies(NarakaConfigCatalog catalog, DiagnosticBag diagnostics)
    {
        const string file = "currencies.csv";
        if (catalog.Currencies.Length == 0)
        {
            diagnostics.Add(file, 0, "至少需要一种货币。");
        }

        foreach (var currency in catalog.Currencies)
        {
            if (currency.StarterGrant < 0)
            {
                diagnostics.Add(file, 0, $"货币 '{currency.CurrencyId}' 的 StarterGrant 不能为负。");
            }
        }

        RequireDistinctSortOrder(catalog.Currencies, value => value.SortOrder, value => value.CurrencyId, file, diagnostics);
    }

    private static void ValidateItems(
        NarakaConfigCatalog catalog,
        ISet<string> currencies,
        DiagnosticBag diagnostics)
    {
        const string file = "items.csv";
        foreach (var item in catalog.Items)
        {
            if (!ConfigItemCategory.IsDefined(item.Category))
            {
                diagnostics.Add(file, 0, $"物品 '{item.ItemId}' 的 Category '{item.Category}' 不是合法分类。");
            }

            RequireQuality(item.Quality, file, $"物品 '{item.ItemId}'", diagnostics);

            if (item.StackLimit <= 0)
            {
                diagnostics.Add(file, 0, $"物品 '{item.ItemId}' 的 StackLimit 必须大于 0，实际为 {item.StackLimit}。");
            }

            if (item.SellPrice < 0)
            {
                diagnostics.Add(file, 0, $"物品 '{item.ItemId}' 的 SellPrice 不能为负。");
            }

            if (item.SellPrice > 0 && !currencies.Contains(item.SellCurrencyId))
            {
                diagnostics.Add(
                    file,
                    0,
                    $"物品 '{item.ItemId}' 的 SellCurrencyId '{item.SellCurrencyId}' 不存在于 currencies.csv。");
            }
        }

        RequireDistinctSortOrder(catalog.Items, value => value.SortOrder, value => value.ItemId, file, diagnostics);
    }

    private static void ValidateHeroSkills(
        NarakaConfigCatalog catalog,
        ISet<string> heroes,
        DiagnosticBag diagnostics)
    {
        const string file = "hero_skills.csv";
        if (catalog.Heroes.Length == 0)
        {
            diagnostics.Add("heroes.csv", 0, "至少需要一个英雄，否则新账号无法确定默认出战英雄。");
        }

        RequireDistinctSortOrder(
            catalog.Heroes, value => value.SortOrder, value => value.HeroId, "heroes.csv", diagnostics);

        foreach (var skill in catalog.HeroSkills)
        {
            if (!heroes.Contains(skill.HeroId))
            {
                diagnostics.Add(file, 0, $"技能 '{skill.SkillId}' 的 HeroId '{skill.HeroId}' 不存在于 heroes.csv。");
            }

            if (skill.SlotKey is not ("F" or "V"))
            {
                diagnostics.Add(file, 0, $"技能 '{skill.SkillId}' 的 SlotKey 只能是 F 或 V，实际为 '{skill.SlotKey}'。");
            }

            if (skill.CooldownSeconds < 0f || skill.DamageMultiplier < 0f || skill.RangeMeters < 0f)
            {
                diagnostics.Add(file, 0, $"技能 '{skill.SkillId}' 的冷却、倍率与范围都不能为负。");
            }
        }

        // 同一英雄的技能显示顺序必须确定，否则界面上的技能列表会随读取顺序漂移。
        foreach (var group in catalog.HeroSkills.GroupBy(skill => skill.HeroId, StringComparer.Ordinal))
        {
            var orders = group.Select(skill => skill.SortOrder).ToArray();
            if (orders.Length != orders.Distinct().Count())
            {
                diagnostics.Add(file, 0, $"英雄 '{group.Key}' 的技能 SortOrder 存在重复，显示顺序不确定。");
            }

            var slots = group.Select(skill => skill.SlotKey).ToArray();
            if (slots.Length != slots.Distinct(StringComparer.Ordinal).Count())
            {
                diagnostics.Add(file, 0, $"英雄 '{group.Key}' 的技能 SlotKey 存在重复。");
            }
        }
    }

    private static void ValidateWeaponLevels(
        NarakaConfigCatalog catalog,
        ISet<string> weapons,
        DiagnosticBag diagnostics)
    {
        const string file = "weapon_levels.csv";
        if (catalog.Weapons.Length == 0)
        {
            diagnostics.Add("weapons.csv", 0, "至少需要一把武器。");
        }

        RequireDistinctSortOrder(
            catalog.Weapons, value => value.SortOrder, value => value.WeaponId, "weapons.csv", diagnostics);

        foreach (var level in catalog.WeaponLevels)
        {
            if (!weapons.Contains(level.WeaponId))
            {
                diagnostics.Add(file, 0, $"武器等级引用了不存在的 WeaponId '{level.WeaponId}'。");
            }

            if (level.Attack < 0 || level.AttackSpeed <= 0f)
            {
                diagnostics.Add(file, 0, $"武器 '{level.WeaponId}' 第 {level.Level} 级的攻击力或攻速非法。");
            }
        }

        foreach (var weapon in catalog.Weapons)
        {
            var levels = catalog.WeaponLevels
                .Where(level => string.Equals(level.WeaponId, weapon.WeaponId, StringComparison.Ordinal))
                .Select(level => level.Level)
                .OrderBy(level => level)
                .ToArray();

            if (levels.Length != levels.Distinct().Count())
            {
                diagnostics.Add(file, 0, $"武器 '{weapon.WeaponId}' 的等级存在重复。");
            }

            if (weapon.MaxLevel < 1)
            {
                diagnostics.Add("weapons.csv", 0, $"武器 '{weapon.WeaponId}' 的 MaxLevel 必须至少为 1。");
                continue;
            }

            var expected = Enumerable.Range(1, weapon.MaxLevel).ToArray();
            if (!levels.SequenceEqual(expected))
            {
                diagnostics.Add(
                    file,
                    0,
                    $"武器 '{weapon.WeaponId}' 的等级必须是 1..{weapon.MaxLevel} 连续且不重复，实际为 " +
                    $"[{string.Join(',', levels)}]。");
            }
        }
    }

    private static void ValidateForgeRecipes(
        NarakaConfigCatalog catalog,
        ISet<string> currencies,
        ISet<string> items,
        ISet<string> weapons,
        DiagnosticBag diagnostics)
    {
        const string file = "forge_recipes.csv";
        // 同上：重复的 WeaponId 已经单独报错，这里保留首个定义以便继续检查其余规则。
        var maxLevels = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var weapon in catalog.Weapons)
        {
            maxLevels.TryAdd(weapon.WeaponId, weapon.MaxLevel);
        }

        foreach (var recipe in catalog.ForgeRecipes)
        {
            if (!weapons.Contains(recipe.WeaponId))
            {
                diagnostics.Add(file, 0, $"配方 '{recipe.RecipeId}' 的 WeaponId '{recipe.WeaponId}' 不存在。");
                continue;
            }

            if (recipe.ToLevel != recipe.FromLevel + 1)
            {
                diagnostics.Add(
                    file,
                    0,
                    $"配方 '{recipe.RecipeId}' 必须只提升一级：FromLevel={recipe.FromLevel}, ToLevel={recipe.ToLevel}。");
            }

            if (recipe.FromLevel < 1 || recipe.ToLevel > maxLevels[recipe.WeaponId])
            {
                diagnostics.Add(
                    file,
                    0,
                    $"配方 '{recipe.RecipeId}' 的等级区间超出武器 '{recipe.WeaponId}' 的 1..{maxLevels[recipe.WeaponId]}。");
            }

            if (!currencies.Contains(recipe.CurrencyId))
            {
                diagnostics.Add(file, 0, $"配方 '{recipe.RecipeId}' 的 CurrencyId '{recipe.CurrencyId}' 不存在。");
            }

            if (recipe.CurrencyAmount < 0)
            {
                diagnostics.Add(file, 0, $"配方 '{recipe.RecipeId}' 的 CurrencyAmount 不能为负。");
            }

            ValidateRecipeMaterial(file, recipe.RecipeId, 1, recipe.Material1ItemId, recipe.Material1Amount, items, diagnostics);
            ValidateRecipeMaterial(file, recipe.RecipeId, 2, recipe.Material2ItemId, recipe.Material2Amount, items, diagnostics);
        }

        // 每个武器的每一级都必须有唯一一条升级配方，否则锻造界面会出现无法继续的断档。
        foreach (var weapon in catalog.Weapons)
        {
            for (var level = 1; level < weapon.MaxLevel; level++)
            {
                var matches = catalog.ForgeRecipes.Count(recipe =>
                    string.Equals(recipe.WeaponId, weapon.WeaponId, StringComparison.Ordinal) &&
                    recipe.FromLevel == level);
                if (matches != 1)
                {
                    diagnostics.Add(
                        file,
                        0,
                        $"武器 '{weapon.WeaponId}' 从 {level} 级升级的配方必须恰好有 1 条，实际有 {matches} 条。");
                }
            }
        }
    }

    private static void ValidateRecipeMaterial(
        string file,
        string recipeId,
        int slot,
        string itemId,
        int amount,
        ISet<string> items,
        DiagnosticBag diagnostics)
    {
        if (itemId.Length == 0)
        {
            if (amount != 0)
            {
                diagnostics.Add(file, 0, $"配方 '{recipeId}' 的材料槽 {slot} 没有物品却写了数量 {amount}。");
            }

            return;
        }

        if (!items.Contains(itemId))
        {
            diagnostics.Add(file, 0, $"配方 '{recipeId}' 的材料槽 {slot} 引用了不存在的 ItemId '{itemId}'。");
        }

        if (amount <= 0)
        {
            diagnostics.Add(file, 0, $"配方 '{recipeId}' 的材料槽 {slot} 数量必须大于 0。");
        }
    }

    private static void ValidateShopProducts(
        NarakaConfigCatalog catalog,
        ISet<string> currencies,
        ISet<string> items,
        DiagnosticBag diagnostics)
    {
        const string file = "shop_products.csv";

        // ID 重复本身已经单独报错。这里保留首个定义而不是抛异常，
        // 否则一个重复 ID 会中断整轮校验，使用者只能一次修一个错。
        var itemsById = new Dictionary<string, ItemConfig>(StringComparer.Ordinal);
        foreach (var item in catalog.Items)
        {
            itemsById.TryAdd(item.ItemId, item);
        }

        foreach (var product in catalog.ShopProducts)
        {
            if (!items.Contains(product.ItemId))
            {
                diagnostics.Add(file, 0, $"商品 '{product.ProductId}' 的 ItemId '{product.ItemId}' 不存在。");
                continue;
            }

            if (!currencies.Contains(product.CurrencyId))
            {
                diagnostics.Add(file, 0, $"商品 '{product.ProductId}' 的 CurrencyId '{product.CurrencyId}' 不存在。");
            }

            if (product.UnitPrice < 0)
            {
                diagnostics.Add(file, 0, $"商品 '{product.ProductId}' 的 UnitPrice 不能为负。");
            }

            if (product.PurchaseLimit < 0)
            {
                diagnostics.Add(file, 0, $"商品 '{product.ProductId}' 的 PurchaseLimit 不能为负。");
            }

            // 分类必须与物品自身一致，否则商店页签会把物品放到错误的分类下。
            var expectedCategory = itemsById[product.ItemId].Category;
            if (!string.Equals(product.ShopCategory, expectedCategory, StringComparison.Ordinal))
            {
                diagnostics.Add(
                    file,
                    0,
                    $"商品 '{product.ProductId}' 的 ShopCategory '{product.ShopCategory}' 与物品分类 '{expectedCategory}' 不一致。");
            }
        }

        RequireDistinctSortOrder(
            catalog.ShopProducts, value => value.SortOrder, value => value.ProductId, file, diagnostics);
    }

    private static void ValidateGacha(
        NarakaConfigCatalog catalog,
        ISet<string> currencies,
        ISet<string> items,
        ISet<string> pools,
        DiagnosticBag diagnostics)
    {
        const string poolFile = "gacha_pools.csv";
        const string entryFile = "gacha_entries.csv";

        foreach (var pool in catalog.GachaPools)
        {
            if (!currencies.Contains(pool.CurrencyId))
            {
                diagnostics.Add(poolFile, 0, $"奖池 '{pool.PoolId}' 的 CurrencyId '{pool.CurrencyId}' 不存在。");
            }

            if (pool.SinglePrice < 0 || pool.TenPullPrice < 0)
            {
                diagnostics.Add(poolFile, 0, $"奖池 '{pool.PoolId}' 的单抽或十连价格不能为负。");
            }

            RequireQuality(pool.TenPullMinimumQuality, poolFile, $"奖池 '{pool.PoolId}' 的 TenPullMinimumQuality", diagnostics);
            RequireQuality(pool.PityQuality, poolFile, $"奖池 '{pool.PoolId}' 的 PityQuality", diagnostics);

            if (pool.PityCount <= 0)
            {
                diagnostics.Add(poolFile, 0, $"奖池 '{pool.PoolId}' 的 PityCount 必须大于 0。");
            }

            var entries = catalog.GachaEntries
                .Where(entry => string.Equals(entry.PoolId, pool.PoolId, StringComparison.Ordinal))
                .ToArray();

            if (entries.Length == 0)
            {
                diagnostics.Add(entryFile, 0, $"奖池 '{pool.PoolId}' 没有任何奖励条目。");
                continue;
            }

            var totalWeight = entries.Sum(entry => (long)entry.Weight);
            if (totalWeight <= 0)
            {
                diagnostics.Add(entryFile, 0, $"奖池 '{pool.PoolId}' 的总权重必须大于 0，实际为 {totalWeight}。");
            }

            // 十连保底与硬保底都要替换出一个具体奖励；没有对应品质的奖励时保底根本无法兑现。
            RequirePoolQualityAvailable(entries, pool.PoolId, pool.TenPullMinimumQuality, "十连保底", entryFile, diagnostics);
            RequirePoolQualityAvailable(entries, pool.PoolId, pool.PityQuality, $"{pool.PityCount} 抽硬保底", entryFile, diagnostics);
        }

        foreach (var entry in catalog.GachaEntries)
        {
            if (!pools.Contains(entry.PoolId))
            {
                diagnostics.Add(entryFile, 0, $"奖励 '{entry.RewardId}' 的 PoolId '{entry.PoolId}' 不存在。");
            }

            if (!items.Contains(entry.ItemId))
            {
                diagnostics.Add(entryFile, 0, $"奖励 '{entry.RewardId}' 的 ItemId '{entry.ItemId}' 不存在。");
            }

            if (entry.Amount <= 0)
            {
                diagnostics.Add(entryFile, 0, $"奖励 '{entry.RewardId}' 的 Amount 必须大于 0。");
            }

            if (entry.Weight <= 0)
            {
                diagnostics.Add(entryFile, 0, $"奖励 '{entry.RewardId}' 的 Weight 必须大于 0。");
            }

            RequireQuality(entry.Quality, entryFile, $"奖励 '{entry.RewardId}'", diagnostics);
        }
    }

    private static void RequirePoolQualityAvailable(
        IReadOnlyCollection<GachaEntryConfig> entries,
        string poolId,
        string quality,
        string label,
        string file,
        DiagnosticBag diagnostics)
    {
        var threshold = ConfigQuality.IndexOf(quality);
        if (threshold < 0)
        {
            return;
        }

        var available = entries.Any(entry =>
            ConfigQuality.IndexOf(entry.Quality) >= threshold && entry.Weight > 0);
        if (!available)
        {
            diagnostics.Add(
                file,
                0,
                $"奖池 '{poolId}' 的{label}需要 '{quality}' 及以上品质的有效奖励，但奖池中没有。");
        }
    }

    private static void ValidateSignIn(
        NarakaConfigCatalog catalog,
        ISet<string> currencies,
        ISet<string> items,
        DiagnosticBag diagnostics)
    {
        const string rewardFile = "signin_rewards.csv";
        const string milestoneFile = "signin_milestones.csv";

        var days = catalog.SignInRewards.Select(reward => reward.Day).OrderBy(day => day).ToArray();
        if (!days.SequenceEqual(Enumerable.Range(1, 7)))
        {
            diagnostics.Add(
                rewardFile,
                0,
                $"七日签到必须覆盖第 1 至第 7 天且不重复，实际为 [{string.Join(',', days)}]。");
        }

        foreach (var reward in catalog.SignInRewards)
        {
            ValidateReward(rewardFile, reward.RewardId, reward.RewardKind, reward.ItemId, reward.ItemAmount,
                reward.CurrencyId, reward.CurrencyAmount, currencies, items, allowNone: false, diagnostics);
        }

        RequireUniqueIds(catalog.SignInRewards, value => value.RewardId, rewardFile, "RewardId", diagnostics);

        var milestones = catalog.SignInMilestones.Select(value => value.MilestoneDays).ToArray();
        if (milestones.Length != milestones.Distinct().Count())
        {
            diagnostics.Add(milestoneFile, 0, "连续签到节点天数存在重复。");
        }

        foreach (var milestone in catalog.SignInMilestones)
        {
            if (milestone.MilestoneDays is < 1 or > 7)
            {
                diagnostics.Add(
                    milestoneFile,
                    0,
                    $"连续签到节点 '{milestone.RewardId}' 的天数必须在 1 到 7 之间，实际为 {milestone.MilestoneDays}。");
            }

            ValidateReward(milestoneFile, milestone.RewardId, milestone.RewardKind, milestone.ItemId,
                milestone.ItemAmount, milestone.CurrencyId, milestone.CurrencyAmount, currencies, items,
                allowNone: false, diagnostics);
        }

        RequireUniqueIds(catalog.SignInMilestones, value => value.RewardId, milestoneFile, "RewardId", diagnostics);
    }

    private static void ValidateAccountLevelRewards(
        NarakaConfigCatalog catalog,
        ISet<string> currencies,
        ISet<string> items,
        DiagnosticBag diagnostics)
    {
        const string file = "account_level_rewards.csv";
        var levels = catalog.AccountLevelRewards.Select(value => value.Level).OrderBy(level => level).ToArray();
        if (levels.Length == 0)
        {
            diagnostics.Add(file, 0, "至少需要一条账号等级记录。");
            return;
        }

        if (!levels.SequenceEqual(Enumerable.Range(1, levels.Length)))
        {
            diagnostics.Add(
                file,
                0,
                $"账号等级必须从 1 开始连续且不重复，实际为 [{string.Join(',', levels)}]。");
        }

        long previousXp = -1;
        foreach (var reward in catalog.AccountLevelRewards.OrderBy(value => value.Level))
        {
            if (reward.XpToReach < 0)
            {
                diagnostics.Add(file, 0, $"等级 {reward.Level} 的 XpToReach 不能为负。");
            }

            // 经验必须严格递增，否则升级判定会在同一经验值上同时满足两个等级。
            if (reward.XpToReach <= previousXp && reward.Level > 1)
            {
                diagnostics.Add(
                    file,
                    0,
                    $"等级 {reward.Level} 的 XpToReach（{reward.XpToReach}）必须大于上一等级的 {previousXp}。");
            }

            previousXp = reward.XpToReach;

            ValidateReward(file, reward.RewardId, reward.RewardKind, reward.ItemId, reward.ItemAmount,
                reward.CurrencyId, reward.CurrencyAmount, currencies, items, allowNone: true, diagnostics);
        }

        if (catalog.AccountLevelRewards.First(value => value.Level == 1).XpToReach != 0)
        {
            diagnostics.Add(file, 0, "等级 1 的 XpToReach 必须为 0。");
        }

        RequireUniqueIds(catalog.AccountLevelRewards, value => value.RewardId, file, "RewardId", diagnostics);
    }

    private static void ValidateAchievements(
        NarakaConfigCatalog catalog,
        ISet<string> currencies,
        ISet<string> items,
        DiagnosticBag diagnostics)
    {
        const string file = "achievements.csv";
        foreach (var achievement in catalog.Achievements)
        {
            if (!ConfigAchievementCategory.IsDefined(achievement.Category))
            {
                diagnostics.Add(
                    file,
                    0,
                    $"成就 '{achievement.AchievementId}' 的 Category '{achievement.Category}' 不是合法分类。");
            }

            if (achievement.TargetProgress <= 0)
            {
                diagnostics.Add(file, 0, $"成就 '{achievement.AchievementId}' 的 TargetProgress 必须大于 0。");
            }

            if (achievement.AchievementXp < 0)
            {
                diagnostics.Add(file, 0, $"成就 '{achievement.AchievementId}' 的 AchievementXp 不能为负。");
            }

            ValidateReward(file, achievement.AchievementId, achievement.RewardKind, achievement.ItemId,
                achievement.ItemAmount, achievement.CurrencyId, achievement.CurrencyAmount, currencies, items,
                allowNone: true, diagnostics);
        }

        RequireDistinctSortOrder(
            catalog.Achievements, value => value.SortOrder, value => value.AchievementId, file, diagnostics);
    }

    private static void ValidateInventoryCapacity(
        NarakaConfigCatalog catalog,
        ISet<string> currencies,
        DiagnosticBag diagnostics)
    {
        const string file = "inventory_capacity.csv";
        var tiers = catalog.InventoryCapacities.Select(value => value.Tier).OrderBy(tier => tier).ToArray();
        if (tiers.Length == 0)
        {
            diagnostics.Add(file, 0, "至少需要一个仓库容量档位。");
            return;
        }

        if (!tiers.SequenceEqual(Enumerable.Range(0, tiers.Length)))
        {
            diagnostics.Add(file, 0, $"仓库容量档位必须从 0 开始连续，实际为 [{string.Join(',', tiers)}]。");
        }

        var previousCapacity = -1;
        foreach (var tier in catalog.InventoryCapacities.OrderBy(value => value.Tier))
        {
            if (tier.Capacity <= previousCapacity)
            {
                diagnostics.Add(
                    file,
                    0,
                    $"档位 {tier.Tier} 的容量（{tier.Capacity}）必须大于上一档位的 {previousCapacity}。");
            }

            previousCapacity = tier.Capacity;

            if (tier.ExpandPrice < 0)
            {
                diagnostics.Add(file, 0, $"档位 {tier.Tier} 的 ExpandPrice 不能为负。");
            }

            if (tier.Tier == 0 && tier.ExpandPrice != 0)
            {
                diagnostics.Add(file, 0, "档位 0 是初始容量，ExpandPrice 必须为 0。");
            }

            if (!currencies.Contains(tier.ExpandCurrencyId))
            {
                diagnostics.Add(file, 0, $"档位 {tier.Tier} 的 ExpandCurrencyId '{tier.ExpandCurrencyId}' 不存在。");
            }
        }
    }

    private static void ValidateAppearanceAndPets(NarakaConfigCatalog catalog, DiagnosticBag diagnostics)
    {
        if (catalog.Avatars.Length == 0)
        {
            diagnostics.Add("avatars.csv", 0, "至少需要一个头像，否则新账号无法确定默认头像。");
        }

        if (catalog.AvatarFrames.Length == 0)
        {
            diagnostics.Add("avatar_frames.csv", 0, "至少需要一个头像框。");
        }

        RequireDistinctSortOrder(
            catalog.Avatars, value => value.SortOrder, value => value.AvatarId, "avatars.csv", diagnostics);
        RequireDistinctSortOrder(
            catalog.AvatarFrames, value => value.SortOrder, value => value.AvatarFrameId, "avatar_frames.csv", diagnostics);
        RequireDistinctSortOrder(
            catalog.Pets, value => value.SortOrder, value => value.PetId, "pets.csv", diagnostics);

        if (catalog.Pets.Length > 0 && !catalog.Pets.Any(pet => pet.DefaultOwned))
        {
            diagnostics.Add("pets.csv", 0, "至少需要一个 DefaultOwned=1 的宠物作为新账号默认宠物。");
        }
    }

    private static void ValidateReward(
        string file,
        string rewardId,
        string kind,
        string itemId,
        int itemAmount,
        string currencyId,
        long currencyAmount,
        ISet<string> currencies,
        ISet<string> items,
        bool allowNone,
        DiagnosticBag diagnostics)
    {
        if (!ConfigRewardKind.IsDefined(kind))
        {
            diagnostics.Add(file, 0, $"奖励 '{rewardId}' 的 RewardKind '{kind}' 不是合法种类。");
            return;
        }

        switch (kind)
        {
            case ConfigRewardKind.None when !allowNone:
                diagnostics.Add(file, 0, $"奖励 '{rewardId}' 不允许使用 None。");
                break;

            case ConfigRewardKind.None:
                if (itemId.Length != 0 || itemAmount != 0 || currencyId.Length != 0 || currencyAmount != 0)
                {
                    diagnostics.Add(file, 0, $"奖励 '{rewardId}' 为 None，但仍填写了物品或货币。");
                }

                break;

            case ConfigRewardKind.Item:
                if (!items.Contains(itemId))
                {
                    diagnostics.Add(file, 0, $"奖励 '{rewardId}' 的 ItemId '{itemId}' 不存在。");
                }

                if (itemAmount <= 0)
                {
                    diagnostics.Add(file, 0, $"奖励 '{rewardId}' 的 ItemAmount 必须大于 0。");
                }

                if (currencyId.Length != 0 || currencyAmount != 0)
                {
                    diagnostics.Add(file, 0, $"奖励 '{rewardId}' 为物品奖励，不能同时填写货币。");
                }

                break;

            case ConfigRewardKind.Currency:
                if (!currencies.Contains(currencyId))
                {
                    diagnostics.Add(file, 0, $"奖励 '{rewardId}' 的 CurrencyId '{currencyId}' 不存在。");
                }

                if (currencyAmount <= 0)
                {
                    diagnostics.Add(file, 0, $"奖励 '{rewardId}' 的 CurrencyAmount 必须大于 0。");
                }

                if (itemId.Length != 0 || itemAmount != 0)
                {
                    diagnostics.Add(file, 0, $"奖励 '{rewardId}' 为货币奖励，不能同时填写物品。");
                }

                break;

            default:
                break;
        }
    }

    private static void RequireQuality(string quality, string file, string subject, DiagnosticBag diagnostics)
    {
        if (ConfigQuality.IndexOf(quality) < 0)
        {
            diagnostics.Add(
                file,
                0,
                $"{subject} 的品质 '{quality}' 非法，只允许 {string.Join('/', ConfigQuality.Ordered)}。");
        }
    }

    private static HashSet<string> RequireUniqueIds<T>(
        IReadOnlyCollection<T> rows,
        Func<T, string> selector,
        string file,
        string column,
        DiagnosticBag diagnostics)
    {
        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var id = selector(row);
            if (id.Length != 0 && !unique.Add(id))
            {
                diagnostics.Add(file, 0, $"{column} 重复：'{id}'。");
            }
        }

        return unique;
    }

    private static void RequireDistinctSortOrder<T>(
        IReadOnlyCollection<T> rows,
        Func<T, int> sortOrder,
        Func<T, string> id,
        string file,
        DiagnosticBag diagnostics)
    {
        var duplicates = rows
            .GroupBy(sortOrder)
            .Where(group => group.Count() > 1)
            .ToArray();

        foreach (var duplicate in duplicates)
        {
            diagnostics.Add(
                file,
                0,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "SortOrder {0} 被 [{1}] 共用，显示顺序不确定。",
                    duplicate.Key,
                    string.Join(',', duplicate.Select(id).OrderBy(value => value, StringComparer.Ordinal))));
        }
    }
}
