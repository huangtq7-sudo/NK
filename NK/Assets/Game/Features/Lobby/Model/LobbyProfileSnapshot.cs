namespace Naraka.Features.Lobby.Model
{
    /// <summary>
    /// 服务端账号资料的不可变快照。
    ///
    /// 全部使用稳定配置 ID：配置中插入一个英雄会让所有列表下标错位，而玩家的出战选择
    /// 和头像必须跨版本稳定，因此界面下标绝不能成为持久化的身份。
    /// </summary>
    public readonly struct LobbyProfileSnapshot
    {
        private LobbyProfileSnapshot(
            string avatarId,
            string avatarFrameId,
            string selectedHeroId,
            string selectedWeaponId,
            string selectedPetId,
            long accountXp,
            int accountLevel,
            int inventoryTier,
            long copper,
            long silk,
            long gold)
        {
            AvatarId = avatarId;
            AvatarFrameId = avatarFrameId;
            SelectedHeroId = selectedHeroId;
            SelectedWeaponId = selectedWeaponId;
            SelectedPetId = selectedPetId;
            AccountXp = accountXp;
            AccountLevel = accountLevel;
            InventoryTier = inventoryTier;
            Copper = copper;
            Silk = silk;
            Gold = gold;
        }

        public string AvatarId { get; }

        public string AvatarFrameId { get; }

        public string SelectedHeroId { get; }

        public string SelectedWeaponId { get; }

        public string SelectedPetId { get; }

        public long AccountXp { get; }

        public int AccountLevel { get; }

        public int InventoryTier { get; }

        public long Copper { get; }

        public long Silk { get; }

        public long Gold { get; }

        /// <summary>
        /// 只有全部字段合法才构造成功。非法数据在这里被挡住，因此界面不可能显示出
        /// 空头像 ID 或负余额。
        /// </summary>
        public static bool TryCreate(
            string avatarId,
            string avatarFrameId,
            string selectedHeroId,
            string selectedWeaponId,
            string selectedPetId,
            long accountXp,
            int accountLevel,
            int inventoryTier,
            long copper,
            long silk,
            long gold,
            out LobbyProfileSnapshot snapshot)
        {
            if (string.IsNullOrEmpty(avatarId) ||
                string.IsNullOrEmpty(avatarFrameId) ||
                string.IsNullOrEmpty(selectedHeroId) ||
                string.IsNullOrEmpty(selectedWeaponId) ||
                string.IsNullOrEmpty(selectedPetId) ||
                accountXp < 0L ||
                accountLevel < LobbyAccountSnapshot.MinimumAccountLevel ||
                inventoryTier < 0 ||
                copper < 0L || silk < 0L || gold < 0L)
            {
                snapshot = default;
                return false;
            }

            snapshot = new LobbyProfileSnapshot(
                avatarId, avatarFrameId, selectedHeroId, selectedWeaponId, selectedPetId,
                accountXp, accountLevel, inventoryTier, copper, silk, gold);
            return true;
        }
    }
}
