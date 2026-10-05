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
        public MonsterConfig[] Monsters = Array.Empty<MonsterConfig>();
        public MonsterSkillConfig[] MonsterSkills = Array.Empty<MonsterSkillConfig>();
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

    /// <summary>
    /// 怪物技能的颜色标签。这是玩家侧的操作语言：
    /// 金色可反击、红色不可反击、无色是普通攻击。
    /// </summary>
    public static class ConfigMonsterColorTag
    {
        /// <summary>普通攻击，没有颜色提示，永远不可反击。</summary>
        public const string None = "None";

        /// <summary>金色可反击技能。</summary>
        public const string Gold = "Gold";

        /// <summary>红色不可反击技能。</summary>
        public const string Red = "Red";

        public static readonly string[] All = { None, Gold, Red };
    }

    /// <summary>怪物分类。</summary>
    public static class ConfigMonsterCategory
    {
        public const string Normal = "Normal";
        public const string Elite = "Elite";
        public const string Boss = "Boss";

        public static readonly string[] All = { Normal, Elite, Boss };
    }

    /// <summary>
    /// 数值是否已经过平衡确认。
    ///
    /// <see cref="P2Graybox"/> 表示"能跑通战斗闭环的占位值"，不是最终平衡结果；
    /// 界面、文档与报告都必须按占位值对待，不得当成已确认设计。
    /// </summary>
    public static class ConfigBalanceStatus
    {
        /// <summary>P2 灰盒调试值，未经平衡确认。</summary>
        public const string P2Graybox = "P2Graybox";

        /// <summary>已确认的正式平衡值。</summary>
        public const string Confirmed = "Confirmed";

        public static readonly string[] All = { P2Graybox, Confirmed };
    }

    /// <summary>
    /// 怪物基础属性。
    ///
    /// 距离与速度的单位是 Unity 世界单位，与玩家的移动速度、冲刺距离同一套刻度
    /// （见 NARAKA_待确认问题.md 的 Q-021）。生命、护甲、防御与攻击力
    /// 最终由服务端权威判定，客户端只用于本地灰盒战斗与展示。
    /// </summary>
    [Serializable]
    public sealed class MonsterConfig
    {
        public string MonsterId = string.Empty;
        public string DisplayName = string.Empty;
        public int SortOrder;

        /// <summary>见 <see cref="ConfigMonsterCategory"/>。</summary>
        public string Category = string.Empty;

        /// <summary>见 <see cref="ConfigBalanceStatus"/>。</summary>
        public string BalanceStatus = string.Empty;

        public int Health;
        public int Armor;
        public int Defense;
        public int Attack;

        /// <summary>巡逻速度。</summary>
        public float PatrolSpeed;

        /// <summary>追击速度。</summary>
        public float ChaseSpeed;

        /// <summary>巡逻活动半径，以出生点为圆心。</summary>
        public float PatrolRadius;

        /// <summary>两次巡逻移动之间的停顿时长。</summary>
        public float PatrolPauseSeconds;

        /// <summary>发现玩家的距离。</summary>
        public float PerceptionRadius;

        /// <summary>脱离追击的距离。超出后回到巡逻。</summary>
        public float ChaseRadius;

        /// <summary>普通攻击的可用距离。</summary>
        public float AttackRange;

        /// <summary>超过这个距离进入休眠，停止高频决策。</summary>
        public float DormantDistance;

        /// <summary>行为树决策频率（次/秒）。约 5–10Hz，不是每帧。</summary>
        public int DecisionsPerSecond;

        /// <summary>进入第二阶段的生命比例，例如 0.5 表示半血。</summary>
        public float PhaseHealthRatio;

        public float NormalAttackMultiplier;
        public float NormalAttackCooldownSeconds;

        /// <summary>普通攻击前摇。</summary>
        public float NormalAttackWindupSeconds;

        /// <summary>普通攻击命中窗时长，起点是前摇结束。</summary>
        public float NormalAttackHitSeconds;

        /// <summary>普通攻击后摇。</summary>
        public float NormalAttackRecoverySeconds;

        public float HitStunSeconds;
        public float DeathSeconds;

        /// <summary>表现资源键。正式模型到位前指向灰盒 View。</summary>
        public string ModelKey = string.Empty;
        public string Description = string.Empty;
    }

    /// <summary>
    /// 怪物技能。颜色标签与可反击标记必须一致：
    /// 红色技能永远不可反击，金色技能必须可反击，普通攻击不在本表内。
    /// </summary>
    [Serializable]
    public sealed class MonsterSkillConfig
    {
        public string SkillId = string.Empty;
        public string MonsterId = string.Empty;
        public string DisplayName = string.Empty;
        public int SortOrder;

        /// <summary>见 <see cref="ConfigBalanceStatus"/>。</summary>
        public string BalanceStatus = string.Empty;

        /// <summary>见 <see cref="ConfigMonsterColorTag"/>。</summary>
        public string ColorTag = string.Empty;

        /// <summary>是否带 CounterableSkill 标签。只有它为真时玩家的反击才可能成功。</summary>
        public bool Counterable;

        public float DamageMultiplier;
        public float CooldownSeconds;

        /// <summary>可用距离下限。</summary>
        public float MinRange;

        /// <summary>可用距离上限。</summary>
        public float MaxRange;

        /// <summary>锥形角度，0 表示不是锥形。</summary>
        public float ConeAngleDegrees;

        /// <summary>
        /// 生命比例低于或等于这个值才允许释放。1 表示任何阶段都可以释放。
        /// </summary>
        public float RequiresPhaseAtOrBelow;

        /// <summary>预警时长。预警必须先于伤害窗口，玩家才有反应时间。</summary>
        public float WarningSeconds;

        /// <summary>预警结束之后的前摇。</summary>
        public float WindupSeconds;

        /// <summary>命中窗时长。</summary>
        public float HitSeconds;

        /// <summary>后摇。</summary>
        public float RecoverySeconds;

        /// <summary>最近使用抑制：刚放过的技能在这段时间内不再被选中。</summary>
        public float RecentUseSuppressionSeconds;

        public string Description = string.Empty;
    }
}
