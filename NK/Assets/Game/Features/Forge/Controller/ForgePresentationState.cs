using System;
using System.Collections.Generic;
using Naraka.Core.Application.MVC;

namespace Naraka.Features.Forge.Controller
{
    /// <summary>
    /// 锻造界面的只读展示状态。
    ///
    /// 武器等级、材料持有量与余额来自服务端；配方与"下一级预览"来自两端共享的配置，
    /// 因此不在这里保存。
    /// </summary>
    public readonly struct ForgePresentationState : IPresentationState
    {
        private readonly IReadOnlyList<AccountWeaponSnapshot> _weapons;
        private readonly IReadOnlyList<ForgeMaterialSnapshot> _materials;

        public ForgePresentationState(
            bool isOpen,
            bool isLoading,
            bool isBusy,
            bool hasServerState,
            IReadOnlyList<AccountWeaponSnapshot> weapons,
            IReadOnlyList<ForgeMaterialSnapshot> materials,
            long copper,
            long silk,
            long gold,
            string previewWeaponId,
            string statusMessage)
        {
            IsOpen = isOpen;
            IsLoading = isLoading;
            IsBusy = isBusy;
            HasServerState = hasServerState;
            _weapons = weapons ?? Array.Empty<AccountWeaponSnapshot>();
            _materials = materials ?? Array.Empty<ForgeMaterialSnapshot>();
            Copper = copper;
            Silk = silk;
            Gold = gold;
            PreviewWeaponId = previewWeaponId ?? string.Empty;
            StatusMessage = statusMessage ?? string.Empty;
        }

        public static ForgePresentationState Initial => new ForgePresentationState(
            false, false, false, false,
            Array.Empty<AccountWeaponSnapshot>(), Array.Empty<ForgeMaterialSnapshot>(),
            0, 0, 0, string.Empty, string.Empty);

        public bool IsOpen { get; }

        public bool IsLoading { get; }

        /// <summary>强化请求在途。期间禁用锻造按钮，避免重复点击消耗两次材料。</summary>
        public bool IsBusy { get; }

        /// <summary>是否已经拿到过一次服务端数据。为 false 时界面保持占位。</summary>
        public bool HasServerState { get; }

        public IReadOnlyList<AccountWeaponSnapshot> Weapons => _weapons ?? Array.Empty<AccountWeaponSnapshot>();

        public IReadOnlyList<ForgeMaterialSnapshot> Materials =>
            _materials ?? Array.Empty<ForgeMaterialSnapshot>();

        public long Copper { get; }

        public long Silk { get; }

        public long Gold { get; }

        public string PreviewWeaponId { get; }

        public string StatusMessage { get; }

        /// <summary>武器当前等级。尚未拿到服务端数据时返回 0，界面据此显示占位而不是"1 级"。</summary>
        public int LevelOf(string weaponId)
        {
            foreach (var weapon in Weapons)
            {
                if (string.Equals(weapon.WeaponId, weaponId, StringComparison.Ordinal))
                {
                    return weapon.Level;
                }
            }

            return 0;
        }

        public long MaterialOf(string itemId)
        {
            foreach (var material in Materials)
            {
                if (string.Equals(material.ItemId, itemId, StringComparison.Ordinal))
                {
                    return material.Quantity;
                }
            }

            return 0L;
        }

        public long BalanceOf(string currencyId)
        {
            switch (currencyId)
            {
                case "Copper": return Copper;
                case "Silk": return Silk;
                case "Gold": return Gold;
                default: return 0L;
            }
        }

        public ForgePresentationState WithOpen(bool isOpen) => Copy(isOpen: isOpen);

        public ForgePresentationState WithLoading(bool isLoading) => Copy(isLoading: isLoading);

        public ForgePresentationState WithBusy(bool isBusy) => Copy(isBusy: isBusy);

        public ForgePresentationState WithServerState(
            IReadOnlyList<AccountWeaponSnapshot> weapons,
            IReadOnlyList<ForgeMaterialSnapshot> materials,
            long copper,
            long silk,
            long gold) =>
            Copy(
                hasServerState: true,
                isLoading: false,
                isBusy: false,
                weapons: weapons,
                materials: materials,
                copper: copper,
                silk: silk,
                gold: gold);

        public ForgePresentationState WithPreviewWeapon(string weaponId) => Copy(previewWeaponId: weaponId);

        public ForgePresentationState WithStatusMessage(string statusMessage) =>
            Copy(statusMessage: statusMessage);

        private ForgePresentationState Copy(
            bool? isOpen = null,
            bool? isLoading = null,
            bool? isBusy = null,
            bool? hasServerState = null,
            IReadOnlyList<AccountWeaponSnapshot> weapons = null,
            IReadOnlyList<ForgeMaterialSnapshot> materials = null,
            long? copper = null,
            long? silk = null,
            long? gold = null,
            string previewWeaponId = null,
            string statusMessage = null) =>
            new ForgePresentationState(
                isOpen ?? IsOpen,
                isLoading ?? IsLoading,
                isBusy ?? IsBusy,
                hasServerState ?? HasServerState,
                weapons ?? Weapons,
                materials ?? Materials,
                copper ?? Copper,
                silk ?? Silk,
                gold ?? Gold,
                previewWeaponId ?? PreviewWeaponId,
                statusMessage ?? StatusMessage);
    }
}
