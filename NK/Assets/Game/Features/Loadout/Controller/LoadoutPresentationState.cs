using Naraka.Core.Application.MVC;

namespace Naraka.Features.Loadout.Controller
{
    /// <summary>装备面板当前展示的分页。</summary>
    public enum LoadoutPanel
    {
        None,
        Hero,
        Weapon
    }

    /// <summary>
    /// 英雄与兵器界面的只读展示状态。
    ///
    /// 浏览游标（当前正在预览哪个英雄/兵器）与"已出战"是两件事：玩家可以翻看每一个英雄，
    /// 只有点击选择按钮才会改变服务端保存的出战选择。因此这里同时保存
    /// <see cref="PreviewHeroId"/> 与 <see cref="EquippedHeroId"/>。
    /// </summary>
    public readonly struct LoadoutPresentationState : IPresentationState
    {
        public LoadoutPresentationState(
            LoadoutPanel panel,
            bool isReady,
            string previewHeroId,
            string previewWeaponId,
            string selectedSkillId,
            string equippedHeroId,
            string equippedWeaponId,
            string equippedPetId,
            int equippedWeaponLevel,
            bool isSaving,
            string statusMessage)
        {
            Panel = panel;
            IsReady = isReady;
            PreviewHeroId = previewHeroId ?? string.Empty;
            PreviewWeaponId = previewWeaponId ?? string.Empty;
            SelectedSkillId = selectedSkillId ?? string.Empty;
            EquippedHeroId = equippedHeroId ?? string.Empty;
            EquippedWeaponId = equippedWeaponId ?? string.Empty;
            EquippedPetId = equippedPetId ?? string.Empty;
            EquippedWeaponLevel = equippedWeaponLevel < 1 ? 1 : equippedWeaponLevel;
            IsSaving = isSaving;
            StatusMessage = statusMessage ?? string.Empty;
        }

        public LoadoutPanel Panel { get; }

        /// <summary>配置已加载且服务端资料已到达。为 false 时界面显示加载中，按钮全部禁用。</summary>
        public bool IsReady { get; }

        public string PreviewHeroId { get; }

        public string PreviewWeaponId { get; }

        /// <summary>右侧技能详情当前展示的技能。空字符串表示尚未选择。</summary>
        public string SelectedSkillId { get; }

        public string EquippedHeroId { get; }

        public string EquippedWeaponId { get; }

        public string EquippedPetId { get; }

        /// <summary>当前预览兵器的等级。P1 的武器等级由锻造推进，这里只做展示。</summary>
        public int EquippedWeaponLevel { get; }

        /// <summary>选择请求在途。期间禁用选择按钮，避免重复点击产生多次写请求。</summary>
        public bool IsSaving { get; }

        public string StatusMessage { get; }

        public bool IsPreviewHeroEquipped =>
            PreviewHeroId.Length > 0 && PreviewHeroId == EquippedHeroId;

        public bool IsPreviewWeaponEquipped =>
            PreviewWeaponId.Length > 0 && PreviewWeaponId == EquippedWeaponId;

        public LoadoutPresentationState WithPanel(LoadoutPanel panel) => Copy(panel: panel);

        public LoadoutPresentationState WithPreviewHero(string heroId, string selectedSkillId) =>
            Copy(previewHeroId: heroId, selectedSkillId: selectedSkillId);

        public LoadoutPresentationState WithPreviewWeapon(string weaponId) =>
            Copy(previewWeaponId: weaponId);

        public LoadoutPresentationState WithSelectedSkill(string skillId) =>
            Copy(selectedSkillId: skillId);

        public LoadoutPresentationState WithSaving(bool isSaving) => Copy(isSaving: isSaving);

        public LoadoutPresentationState WithStatusMessage(string statusMessage) =>
            Copy(statusMessage: statusMessage);

        public LoadoutPresentationState WithEquipped(
            bool isReady,
            string heroId,
            string weaponId,
            string petId,
            int weaponLevel) =>
            Copy(
                isReady: isReady,
                equippedHeroId: heroId,
                equippedWeaponId: weaponId,
                equippedPetId: petId,
                equippedWeaponLevel: weaponLevel);

        private LoadoutPresentationState Copy(
            LoadoutPanel? panel = null,
            bool? isReady = null,
            string previewHeroId = null,
            string previewWeaponId = null,
            string selectedSkillId = null,
            string equippedHeroId = null,
            string equippedWeaponId = null,
            string equippedPetId = null,
            int? equippedWeaponLevel = null,
            bool? isSaving = null,
            string statusMessage = null) =>
            new LoadoutPresentationState(
                panel ?? Panel,
                isReady ?? IsReady,
                previewHeroId ?? PreviewHeroId,
                previewWeaponId ?? PreviewWeaponId,
                selectedSkillId ?? SelectedSkillId,
                equippedHeroId ?? EquippedHeroId,
                equippedWeaponId ?? EquippedWeaponId,
                equippedPetId ?? EquippedPetId,
                equippedWeaponLevel ?? EquippedWeaponLevel,
                isSaving ?? IsSaving,
                statusMessage ?? StatusMessage);
    }
}
