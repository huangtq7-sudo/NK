using System;
using System.Collections.Generic;
using Naraka.Config;

namespace Naraka.Core.Application.Config
{
    /// <summary>
    /// 客户端只读配置目录。
    ///
    /// 客户端配置<b>只用于展示</b>：图标、名称、说明、排序和"下一级预览"这类界面信息。
    /// 价格、概率、奖励与强化结果一律以服务端返回值为准，View 与 Controller 都不得用这里的数值
    /// 自行推导任何账号资产变化。
    /// </summary>
    public sealed class GameConfigCatalog
    {
        private readonly Dictionary<string, CurrencyConfig> _currencies;
        private readonly Dictionary<string, ItemConfig> _items;
        private readonly Dictionary<string, HeroConfig> _heroes;
        private readonly Dictionary<string, HeroSkillConfig[]> _skillsByHero;
        private readonly Dictionary<string, WeaponConfig> _weapons;
        private readonly Dictionary<string, Dictionary<int, WeaponLevelConfig>> _weaponLevels;
        private readonly Dictionary<string, Dictionary<int, ForgeRecipeConfig>> _forgeRecipes;
        private readonly Dictionary<string, ShopProductConfig> _shopProducts;
        private readonly Dictionary<string, GachaPoolConfig> _gachaPools;
        private readonly Dictionary<string, AchievementConfig> _achievements;
        private readonly Dictionary<string, AchievementConfig[]> _achievementsByCategory;
        private readonly Dictionary<string, AvatarConfig> _avatars;
        private readonly Dictionary<string, AvatarFrameConfig> _avatarFrames;
        private readonly Dictionary<string, PetConfig> _pets;

        public GameConfigCatalog(NarakaConfigCatalog catalog)
        {
            Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

            _currencies = Index(catalog.Currencies, value => value.CurrencyId);
            _items = Index(catalog.Items, value => value.ItemId);
            _heroes = Index(catalog.Heroes, value => value.HeroId);
            _weapons = Index(catalog.Weapons, value => value.WeaponId);
            _shopProducts = Index(catalog.ShopProducts, value => value.ProductId);
            _gachaPools = Index(catalog.GachaPools, value => value.PoolId);
            _achievements = Index(catalog.Achievements, value => value.AchievementId);
            _avatars = Index(catalog.Avatars, value => value.AvatarId);
            _avatarFrames = Index(catalog.AvatarFrames, value => value.AvatarFrameId);
            _pets = Index(catalog.Pets, value => value.PetId);

            _skillsByHero = new Dictionary<string, HeroSkillConfig[]>(StringComparer.Ordinal);
            foreach (var hero in catalog.Heroes)
            {
                var skills = new List<HeroSkillConfig>();
                foreach (var skill in catalog.HeroSkills)
                {
                    if (string.Equals(skill.HeroId, hero.HeroId, StringComparison.Ordinal))
                    {
                        skills.Add(skill);
                    }
                }

                skills.Sort((left, right) => left.SortOrder.CompareTo(right.SortOrder));
                _skillsByHero[hero.HeroId] = skills.ToArray();
            }

            _weaponLevels = new Dictionary<string, Dictionary<int, WeaponLevelConfig>>(StringComparer.Ordinal);
            foreach (var level in catalog.WeaponLevels)
            {
                if (!_weaponLevels.TryGetValue(level.WeaponId, out var byLevel))
                {
                    byLevel = new Dictionary<int, WeaponLevelConfig>();
                    _weaponLevels[level.WeaponId] = byLevel;
                }

                byLevel[level.Level] = level;
            }

            _forgeRecipes = new Dictionary<string, Dictionary<int, ForgeRecipeConfig>>(StringComparer.Ordinal);
            foreach (var recipe in catalog.ForgeRecipes)
            {
                if (!_forgeRecipes.TryGetValue(recipe.WeaponId, out var byLevel))
                {
                    byLevel = new Dictionary<int, ForgeRecipeConfig>();
                    _forgeRecipes[recipe.WeaponId] = byLevel;
                }

                byLevel[recipe.FromLevel] = recipe;
            }

            HeroesInDisplayOrder = SortByOrder(catalog.Heroes, value => value.SortOrder);
            WeaponsInDisplayOrder = SortByOrder(catalog.Weapons, value => value.SortOrder);
            AvatarsInDisplayOrder = SortByOrder(catalog.Avatars, value => value.SortOrder);
            AvatarFramesInDisplayOrder = SortByOrder(catalog.AvatarFrames, value => value.SortOrder);
            PetsInDisplayOrder = SortByOrder(catalog.Pets, value => value.SortOrder);
            ShopProductsInDisplayOrder = SortByOrder(catalog.ShopProducts, value => value.SortOrder);
            AchievementsInDisplayOrder = SortByOrder(catalog.Achievements, value => value.SortOrder);
            CurrenciesInDisplayOrder = SortByOrder(catalog.Currencies, value => value.SortOrder);
            SignInRewardsInDayOrder = SortByOrder(catalog.SignInRewards, value => value.Day);
            SignInMilestonesInDayOrder = SortByOrder(catalog.SignInMilestones, value => value.MilestoneDays);
            AccountLevelRewardsInLevelOrder = SortByOrder(catalog.AccountLevelRewards, value => value.Level);

            _achievementsByCategory = new Dictionary<string, AchievementConfig[]>(StringComparer.Ordinal);
            foreach (var category in ConfigAchievementCategory.All)
            {
                var rows = new List<AchievementConfig>();
                foreach (var achievement in AchievementsInDisplayOrder)
                {
                    if (string.Equals(achievement.Category, category, StringComparison.Ordinal))
                    {
                        rows.Add(achievement);
                    }
                }

                _achievementsByCategory[category] = rows.ToArray();
            }
        }

        public NarakaConfigCatalog Catalog { get; }

        public string ConfigVersion => Catalog.ConfigVersion;

        public string SchemaVersion => Catalog.SchemaVersion;

        public IReadOnlyList<CurrencyConfig> CurrenciesInDisplayOrder { get; }

        public IReadOnlyList<HeroConfig> HeroesInDisplayOrder { get; }

        public IReadOnlyList<WeaponConfig> WeaponsInDisplayOrder { get; }

        public IReadOnlyList<AvatarConfig> AvatarsInDisplayOrder { get; }

        public IReadOnlyList<AvatarFrameConfig> AvatarFramesInDisplayOrder { get; }

        /// <summary>
        /// 新账号的默认头像。
        ///
        /// 取"按 SortOrder 排序后的第一项"，与服务端 <c>GameConfig.DefaultAvatarId</c> 是同一条规则；
        /// 两边一旦用不同规则解析，新账号一登录就会拿到一个客户端画不出来的头像 ID。
        /// 配置保证至少有一项，因此这里不做空表兜底——空表在配置编译阶段就已经被拒绝。
        /// </summary>
        public string DefaultAvatarId => AvatarsInDisplayOrder[0].AvatarId;

        public string DefaultAvatarFrameId => AvatarFramesInDisplayOrder[0].AvatarFrameId;

        public IReadOnlyList<PetConfig> PetsInDisplayOrder { get; }

        public IReadOnlyList<ShopProductConfig> ShopProductsInDisplayOrder { get; }

        public IReadOnlyList<AchievementConfig> AchievementsInDisplayOrder { get; }

        /// <summary>七日签到奖励，按天序 1-7 排列。</summary>
        public IReadOnlyList<SignInRewardConfig> SignInRewardsInDayOrder { get; }

        /// <summary>连续签到节点，按节点天数排列。</summary>
        public IReadOnlyList<SignInMilestoneConfig> SignInMilestonesInDayOrder { get; }

        /// <summary>账号等级奖励，按等级排列。</summary>
        public IReadOnlyList<AccountLevelRewardConfig> AccountLevelRewardsInLevelOrder { get; }

        /// <summary>按分类取成就。未知分类返回空列表。</summary>
        public IReadOnlyList<AchievementConfig> GetAchievementsByCategory(string category) =>
            _achievementsByCategory.TryGetValue(category ?? string.Empty, out var rows)
                ? rows
                : Array.Empty<AchievementConfig>();

        public bool TryGetCurrency(string currencyId, out CurrencyConfig currency) =>
            _currencies.TryGetValue(currencyId ?? string.Empty, out currency);

        public bool TryGetItem(string itemId, out ItemConfig item) =>
            _items.TryGetValue(itemId ?? string.Empty, out item);

        public bool TryGetHero(string heroId, out HeroConfig hero) =>
            _heroes.TryGetValue(heroId ?? string.Empty, out hero);

        public IReadOnlyList<HeroSkillConfig> GetHeroSkills(string heroId) =>
            _skillsByHero.TryGetValue(heroId ?? string.Empty, out var skills)
                ? skills
                : Array.Empty<HeroSkillConfig>();

        public bool TryGetWeapon(string weaponId, out WeaponConfig weapon) =>
            _weapons.TryGetValue(weaponId ?? string.Empty, out weapon);

        public bool TryGetWeaponLevel(string weaponId, int level, out WeaponLevelConfig weaponLevel)
        {
            weaponLevel = null;
            return _weaponLevels.TryGetValue(weaponId ?? string.Empty, out var byLevel) &&
                   byLevel.TryGetValue(level, out weaponLevel);
        }

        /// <summary>
        /// 取得"当前等级 → 下一级"的配方，用于锻造界面的数值预览与材料清单。
        /// 是否真的可以强化仍由服务端判定，界面预览不构成承诺。
        /// </summary>
        public bool TryGetForgeRecipe(string weaponId, int fromLevel, out ForgeRecipeConfig recipe)
        {
            recipe = null;
            return _forgeRecipes.TryGetValue(weaponId ?? string.Empty, out var byLevel) &&
                   byLevel.TryGetValue(fromLevel, out recipe);
        }

        public bool TryGetShopProduct(string productId, out ShopProductConfig product) =>
            _shopProducts.TryGetValue(productId ?? string.Empty, out product);

        public bool TryGetGachaPool(string poolId, out GachaPoolConfig pool) =>
            _gachaPools.TryGetValue(poolId ?? string.Empty, out pool);

        public bool TryGetAchievement(string achievementId, out AchievementConfig achievement) =>
            _achievements.TryGetValue(achievementId ?? string.Empty, out achievement);

        public bool TryGetAvatar(string avatarId, out AvatarConfig avatar) =>
            _avatars.TryGetValue(avatarId ?? string.Empty, out avatar);

        public bool TryGetAvatarFrame(string avatarFrameId, out AvatarFrameConfig frame) =>
            _avatarFrames.TryGetValue(avatarFrameId ?? string.Empty, out frame);

        public bool TryGetPet(string petId, out PetConfig pet) =>
            _pets.TryGetValue(petId ?? string.Empty, out pet);

        /// <summary>物品显示名。未知 ID 直接回显 ID，便于定位配置缺失而不是显示空白。</summary>
        public string GetItemDisplayName(string itemId) =>
            TryGetItem(itemId, out var item) ? item.DisplayName : itemId ?? string.Empty;

        public string GetCurrencyDisplayName(string currencyId) =>
            TryGetCurrency(currencyId, out var currency) ? currency.DisplayName : currencyId ?? string.Empty;

        private static Dictionary<string, T> Index<T>(T[] rows, Func<T, string> key)
        {
            var map = new Dictionary<string, T>(rows.Length, StringComparer.Ordinal);
            foreach (var row in rows)
            {
                map[key(row)] = row;
            }

            return map;
        }

        private static T[] SortByOrder<T>(T[] rows, Func<T, int> order)
        {
            var copy = (T[])rows.Clone();
            Array.Sort(copy, (left, right) => order(left).CompareTo(order(right)));
            return copy;
        }
    }
}
