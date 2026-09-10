// <auto-mirrored />
// 权威源文件：Shared/Config/NarakaConfigModels.cs
// 由 Tools/Config/Naraka.ConfigCompiler 镜像复制到
// NK/Assets/Game/Generated/Config/NarakaConfigModels.cs。
// 不要单独修改任一副本：修改权威源文件后重新运行配置编译器；
// `--check` 会在两份副本不一致时以非 0 退出码失败。
//
// 该文件同时被 .NET 10 服务端与 Unity 2021.3 客户端编译，因此只允许使用两端共有的语法：
// 公共字段（Unity JsonUtility 只识别字段）、PascalCase 命名（两端反序列化都按字段名精确匹配）、
// 无 record、无 init 访问器、无可空引用注解。
#nullable disable

using System;

namespace Naraka.Config
{
    /// <summary>
    /// 规范配置总目录。整份配置只有一个 JSON 根对象，因此客户端与服务端共享同一个哈希，
    /// 不可能出现"只更新了一半表"的中间状态。
    /// </summary>
    [Serializable]
    public sealed class NarakaConfigCatalog
    {
        /// <summary>结构版本。字段增删或语义变更时必须提升。</summary>
        public string SchemaVersion = string.Empty;

        /// <summary>内容版本，由生成物 SHA-256 推导，相同源表必然得到相同值。</summary>
        public string ConfigVersion = string.Empty;

        public CurrencyConfig[] Currencies = Array.Empty<CurrencyConfig>();
        public ItemConfig[] Items = Array.Empty<ItemConfig>();
        public HeroConfig[] Heroes = Array.Empty<HeroConfig>();
        public HeroSkillConfig[] HeroSkills = Array.Empty<HeroSkillConfig>();
        public WeaponConfig[] Weapons = Array.Empty<WeaponConfig>();
        public WeaponLevelConfig[] WeaponLevels = Array.Empty<WeaponLevelConfig>();
        public ForgeRecipeConfig[] ForgeRecipes = Array.Empty<ForgeRecipeConfig>();
        public ShopProductConfig[] ShopProducts = Array.Empty<ShopProductConfig>();
        public GachaPoolConfig[] GachaPools = Array.Empty<GachaPoolConfig>();
        public GachaEntryConfig[] GachaEntries = Array.Empty<GachaEntryConfig>();
        public SignInRewardConfig[] SignInRewards = Array.Empty<SignInRewardConfig>();
        public SignInMilestoneConfig[] SignInMilestones = Array.Empty<SignInMilestoneConfig>();
        public AccountLevelRewardConfig[] AccountLevelRewards = Array.Empty<AccountLevelRewardConfig>();
        public AchievementConfig[] Achievements = Array.Empty<AchievementConfig>();
        public InventoryCapacityConfig[] InventoryCapacities = Array.Empty<InventoryCapacityConfig>();
        public AvatarConfig[] Avatars = Array.Empty<AvatarConfig>();
        public AvatarFrameConfig[] AvatarFrames = Array.Empty<AvatarFrameConfig>();
        public PetConfig[] Pets = Array.Empty<PetConfig>();
    }

    /// <summary>物品品质。抽奖、商店与仓库共用同一套五档品质。</summary>
    public static class ConfigQuality
    {
        public const string White = "White";
        public const string Blue = "Blue";
        public const string Purple = "Purple";
        public const string Gold = "Gold";
        public const string Red = "Red";

        /// <summary>由低到高。索引即品质序号，用于"蓝色及以上"这类比较。</summary>
        public static readonly string[] Ordered = { White, Blue, Purple, Gold, Red };

        public static int IndexOf(string quality)
        {
            for (var i = 0; i < Ordered.Length; i++)
            {
                if (string.Equals(Ordered[i], quality, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }
    }

    /// <summary>物品分类。与仓库/商店的分类页签一一对应。</summary>
    public static class ConfigItemCategory
    {
        public const string Soulstone = "Soulstone";
        public const string Material = "Material";
        public const string Special = "Special";
        public const string Consumable = "Consumable";
        public const string Armor = "Armor";

        public static readonly string[] All = { Soulstone, Material, Special, Consumable, Armor };

        public static bool IsDefined(string category) => Array.IndexOf(All, category) >= 0;
    }

    /// <summary>奖励种类。签到、连续奖励、账号等级奖励和成就共用。</summary>
    public static class ConfigRewardKind
    {
        public const string None = "None";
        public const string Item = "Item";
        public const string Currency = "Currency";

        public static readonly string[] All = { None, Item, Currency };

        public static bool IsDefined(string kind) => Array.IndexOf(All, kind) >= 0;
    }

    /// <summary>成就分类。</summary>
    public static class ConfigAchievementCategory
    {
        public const string Adventure = "Adventure";
        public const string Battle = "Battle";
        public const string Forge = "Forge";
        public const string Wealth = "Wealth";

        public static readonly string[] All = { Adventure, Battle, Forge, Wealth };

        public static bool IsDefined(string category) => Array.IndexOf(All, category) >= 0;
    }

    [Serializable]
    public sealed class CurrencyConfig
    {
        public string CurrencyId = string.Empty;
        public string DisplayName = string.Empty;
        public int SortOrder;

        /// <summary>新账号一次性 StarterGrant 赠送数量。服务端权威，客户端只做展示。</summary>
        public long StarterGrant;
        public string IconKey = string.Empty;
        public string Description = string.Empty;
    }

    [Serializable]
    public sealed class ItemConfig
    {
        public string ItemId = string.Empty;
        public string DisplayName = string.Empty;
        public string Category = string.Empty;
        public string Quality = string.Empty;

        /// <summary>单格堆叠上限。所有进入仓库的物品都走堆叠数量模型。</summary>
        public int StackLimit;
        public int SortOrder;
        public string IconKey = string.Empty;
        public string SellCurrencyId = string.Empty;

        /// <summary>售卖单价。为 0 表示不可售卖。</summary>
        public long SellPrice;
        public string Description = string.Empty;
    }

    [Serializable]
    public sealed class HeroConfig
    {
        public string HeroId = string.Empty;
        public string DisplayName = string.Empty;
        public string Title = string.Empty;
        public int SortOrder;
        public int Health;
        public int Attack;
        public int Defense;
        public int Stamina;
        public float MoveSpeed;
        public string PortraitKey = string.Empty;
        public string Background = string.Empty;
    }

    [Serializable]
    public sealed class HeroSkillConfig
    {
        public string SkillId = string.Empty;
        public string HeroId = string.Empty;

        /// <summary>固定技能槽：F 或 V。</summary>
        public string SlotKey = string.Empty;
        public string DisplayName = string.Empty;
        public int SortOrder;
        public float CooldownSeconds;
        public float DamageMultiplier;
        public float RangeMeters;
        public string IconKey = string.Empty;
        public string Description = string.Empty;
        public string EffectSummary = string.Empty;
    }

    [Serializable]
    public sealed class WeaponConfig
    {
        public string WeaponId = string.Empty;
        public string DisplayName = string.Empty;
        public int SortOrder;
        public int MaxLevel;
        public string IconKey = string.Empty;
        public string Description = string.Empty;
    }

    [Serializable]
    public sealed class WeaponLevelConfig
    {
        public string WeaponId = string.Empty;
        public int Level;
        public int Attack;
        public float AttackSpeed;
    }

    [Serializable]
    public sealed class ForgeRecipeConfig
    {
        public string RecipeId = string.Empty;
        public string WeaponId = string.Empty;
        public int FromLevel;
        public int ToLevel;
        public string CurrencyId = string.Empty;
        public long CurrencyAmount;
        public string Material1ItemId = string.Empty;
        public int Material1Amount;
        public string Material2ItemId = string.Empty;
        public int Material2Amount;
    }

    [Serializable]
    public sealed class ShopProductConfig
    {
        public string ProductId = string.Empty;
        public string ItemId = string.Empty;
        public string ShopCategory = string.Empty;
        public string CurrencyId = string.Empty;
        public long UnitPrice;

        /// <summary>账号累计限购数量。0 表示不限购。</summary>
        public int PurchaseLimit;
        public int SortOrder;
        public bool IsAvailable;
    }

    [Serializable]
    public sealed class GachaPoolConfig
    {
        public string PoolId = string.Empty;
        public string DisplayName = string.Empty;
        public string CurrencyId = string.Empty;
        public long SinglePrice;
        public long TenPullPrice;

        /// <summary>十连保底品质：一次十连中至少包含一个该品质及以上的奖励。</summary>
        public string TenPullMinimumQuality = string.Empty;

        /// <summary>硬保底抽数。达到该次数必定产出 PityQuality。</summary>
        public int PityCount;
        public string PityQuality = string.Empty;
        public int SortOrder;
        public string Description = string.Empty;
    }

    [Serializable]
    public sealed class GachaEntryConfig
    {
        public string PoolId = string.Empty;
        public string RewardId = string.Empty;
        public string ItemId = string.Empty;
        public int Amount;
        public string Quality = string.Empty;

        /// <summary>相对权重。必须为正整数，同池权重之和必须大于 0。</summary>
        public int Weight;
    }

    [Serializable]
    public sealed class SignInRewardConfig
    {
        /// <summary>七日签到中的第几天，取值 1-7。</summary>
        public int Day;
        public string RewardId = string.Empty;
        public string RewardKind = string.Empty;
        public string ItemId = string.Empty;
        public int ItemAmount;
        public string CurrencyId = string.Empty;
        public long CurrencyAmount;
    }

    [Serializable]
    public sealed class SignInMilestoneConfig
    {
        /// <summary>连续签到节点天数。</summary>
        public int MilestoneDays;
        public string RewardId = string.Empty;
        public string RewardKind = string.Empty;
        public string ItemId = string.Empty;
        public int ItemAmount;
        public string CurrencyId = string.Empty;
        public long CurrencyAmount;
    }

    [Serializable]
    public sealed class AccountLevelRewardConfig
    {
        public int Level;

        /// <summary>达到该等级所需的累计账号经验。账号经验只能来自任务。</summary>
        public long XpToReach;
        public string RewardId = string.Empty;
        public string RewardKind = string.Empty;
        public string ItemId = string.Empty;
        public int ItemAmount;
        public string CurrencyId = string.Empty;
        public long CurrencyAmount;
    }

    [Serializable]
    public sealed class AchievementConfig
    {
        public string AchievementId = string.Empty;
        public string Category = string.Empty;
        public string DisplayName = string.Empty;
        public string Description = string.Empty;
        public int SortOrder;
        public long TargetProgress;

        /// <summary>成就经验。只累加到 AchievementXp，绝不写入 AccountXp。</summary>
        public long AchievementXp;

        /// <summary>驱动进度的事件名。</summary>
        public string SourceEvent = string.Empty;

        /// <summary>P1 是否已经存在该事件源。为 false 表示等待 P2/P3 接入，进度必须保持 0。</summary>
        public bool IsActiveInP1;
        public string RewardKind = string.Empty;
        public string ItemId = string.Empty;
        public int ItemAmount;
        public string CurrencyId = string.Empty;
        public long CurrencyAmount;
    }

    [Serializable]
    public sealed class InventoryCapacityConfig
    {
        /// <summary>扩容档位。0 为初始容量，扩容价格为 0。</summary>
        public int Tier;
        public int Capacity;
        public string ExpandCurrencyId = string.Empty;
        public long ExpandPrice;
    }

    [Serializable]
    public sealed class AvatarConfig
    {
        public string AvatarId = string.Empty;
        public string DisplayName = string.Empty;
        public int SortOrder;
        public string IconKey = string.Empty;
        public string UnlockRule = string.Empty;
    }

    [Serializable]
    public sealed class AvatarFrameConfig
    {
        public string AvatarFrameId = string.Empty;
        public string DisplayName = string.Empty;
        public int SortOrder;
        public string IconKey = string.Empty;
        public string UnlockRule = string.Empty;
    }

    [Serializable]
    public sealed class PetConfig
    {
        public string PetId = string.Empty;
        public string DisplayName = string.Empty;
        public int SortOrder;
        public string IconKey = string.Empty;

        /// <summary>新账号是否默认拥有。</summary>
        public bool DefaultOwned;
        public string UnlockRule = string.Empty;
        public string Description = string.Empty;
    }
}
