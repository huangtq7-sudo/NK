using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace Naraka.P1.Tests
{
    /// <summary>
    /// Guards the serialized UI Toolkit contract used by the dedicated P1 panel views.
    /// A renamed or mistyped element otherwise compiles successfully and only fails when
    /// the player opens that panel, so every element queried by a view is asserted here.
    /// </summary>
    public sealed class DedicatedPanelUxmlTests
    {
        public sealed class RequiredElement
        {
            public RequiredElement(string name, Type type)
            {
                Name = name;
                Type = type;
            }

            public string Name { get; }

            public Type Type { get; }
        }

        public sealed class PanelSpec
        {
            public PanelSpec(string name, string assetPath, params RequiredElement[] elements)
            {
                Name = name;
                AssetPath = assetPath;
                Elements = elements;
            }

            public string Name { get; }

            public string AssetPath { get; }

            public IReadOnlyList<RequiredElement> Elements { get; }
        }

        private static IEnumerable<TestCaseData> PanelCases()
        {
            foreach (var spec in CreatePanelSpecs())
            {
                yield return new TestCaseData(spec)
                    .SetName(spec.Name + "PanelUxmlMatchesViewBindings");
            }
        }

        [TestCaseSource(nameof(PanelCases))]
        public void DedicatedPanelUxmlMatchesViewBindings(PanelSpec spec)
        {
            var layout = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(spec.AssetPath);
            Assert.That(layout, Is.Not.Null, "未找到 " + spec.AssetPath + "。");

            var root = layout.CloneTree();
            foreach (var requirement in spec.Elements)
            {
                var element = root.Q<VisualElement>(requirement.Name);
                Assert.That(
                    element,
                    Is.Not.Null,
                    spec.Name + "面板缺少View依赖的元素：" + requirement.Name + "。");
                Assert.That(
                    requirement.Type.IsInstanceOfType(element),
                    Is.True,
                    spec.Name + "面板元素" + requirement.Name + "应为" +
                    requirement.Type.Name + "，实际为" + element.GetType().Name + "。");
            }
        }

        private static IReadOnlyList<PanelSpec> CreatePanelSpecs()
        {
            return new[]
            {
                new PanelSpec(
                    "Hero",
                    "Assets/Game/Features/Loadout/View/UI/HeroPanel.uxml",
                    Element<VisualElement>("HeroScreen"),
                    Element<VisualElement>("HeroPortrait"),
                    Element<VisualElement>("HeroSkillBar"),
                    Element<Label>("HeroNameLabel"),
                    Element<Label>("HeroTitleLabel"),
                    Element<Label>("HeroBackgroundLabel"),
                    Element<Label>("HeroHealthLabel"),
                    Element<Label>("HeroAttackLabel"),
                    Element<Label>("HeroDefenseLabel"),
                    Element<Label>("HeroSpeedLabel"),
                    Element<Label>("HeroSkillNameLabel"),
                    Element<Label>("HeroSkillCooldownLabel"),
                    Element<Label>("HeroSkillDescriptionLabel"),
                    Element<Label>("HeroSkillEffectLabel"),
                    Element<Label>("HeroStatusLabel"),
                    Element<Button>("HeroPreviousButton"),
                    Element<Button>("HeroNextButton"),
                    Element<Button>("HeroEquipButton"),
                    Element<Button>("HeroCloseButton")),
                new PanelSpec(
                    "Weapon",
                    "Assets/Game/Features/Loadout/View/UI/WeaponPanel.uxml",
                    Element<VisualElement>("WeaponScreen"),
                    Element<VisualElement>("WeaponIcon"),
                    Element<Label>("WeaponNameLabel"),
                    Element<Label>("WeaponDescriptionLabel"),
                    Element<Label>("WeaponLevelLabel"),
                    Element<Label>("WeaponAttackLabel"),
                    Element<Label>("WeaponAttackSpeedLabel"),
                    Element<Label>("WeaponProficiencyLabel"),
                    Element<Label>("WeaponKillCountLabel"),
                    Element<Label>("WeaponUniqueNoteLabel"),
                    Element<Label>("WeaponStatusLabel"),
                    Element<Button>("WeaponPreviousButton"),
                    Element<Button>("WeaponNextButton"),
                    Element<Button>("WeaponEquipButton"),
                    Element<Button>("WeaponCloseButton")),
                new PanelSpec(
                    "Inventory",
                    "Assets/Game/Features/Inventory/View/UI/InventoryPanel.uxml",
                    Element<VisualElement>("InventoryScreen"),
                    Element<VisualElement>("InventoryGrid"),
                    Element<VisualElement>("InventoryEquipBar"),
                    Element<VisualElement>("InventoryDetailIcon"),
                    Element<VisualElement>("InventoryConfirm"),
                    Element<Label>("InventoryCapacityLabel"),
                    Element<Label>("InventoryDetailName"),
                    Element<Label>("InventoryDetailQuality"),
                    Element<Label>("InventoryDetailOwned"),
                    Element<Label>("InventoryDetailStack"),
                    Element<Label>("InventoryDetailPrice"),
                    Element<Label>("InventoryDetailDescription"),
                    Element<Label>("InventoryConfirmMessage"),
                    Element<Label>("InventoryStatusLabel"),
                    Element<Button>("InventoryCloseButton"),
                    Element<Button>("InventorySortButton"),
                    Element<Button>("InventoryExpandButton"),
                    Element<Button>("InventoryDiscardButton"),
                    Element<Button>("InventorySellButton"),
                    Element<Button>("InventoryConfirmOk"),
                    Element<Button>("InventoryConfirmCancel")),
                new PanelSpec(
                    "Shop",
                    "Assets/Game/Features/Shop/View/UI/ShopPanel.uxml",
                    Element<VisualElement>("ShopScreen"),
                    Element<VisualElement>("ShopList"),
                    Element<VisualElement>("ShopDetailIcon"),
                    Element<VisualElement>("ShopPurchaseDialog"),
                    Element<Label>("ShopDetailName"),
                    Element<Label>("ShopDetailQuality"),
                    Element<Label>("ShopDetailStack"),
                    Element<Label>("ShopDetailOwned"),
                    Element<Label>("ShopDetailLimit"),
                    Element<Label>("ShopDetailDescription"),
                    Element<Label>("ShopDialogTitle"),
                    Element<Label>("ShopQuantityLabel"),
                    Element<Label>("ShopTotalPriceLabel"),
                    Element<Label>("ShopStatusLabel"),
                    Element<SliderInt>("ShopQuantitySlider"),
                    Element<Button>("ShopCloseButton"),
                    Element<Button>("ShopBuyButton"),
                    Element<Button>("ShopQuantityMinus"),
                    Element<Button>("ShopQuantityPlus"),
                    Element<Button>("ShopDialogConfirm"),
                    Element<Button>("ShopDialogCancel")),
                new PanelSpec(
                    "Forge",
                    "Assets/Game/Features/Forge/View/UI/ForgePanel.uxml",
                    Element<VisualElement>("ForgeScreen"),
                    Element<VisualElement>("ForgeWeaponIcon"),
                    Element<VisualElement>("ForgeCostList"),
                    Element<Label>("ForgeWeaponNameLabel"),
                    Element<Label>("ForgeLevelLabel"),
                    Element<Label>("ForgeAttackPreviewLabel"),
                    Element<Label>("ForgeAttackSpeedPreviewLabel"),
                    Element<Label>("ForgeMaxLevelLabel"),
                    Element<Label>("ForgeSuccessRateLabel"),
                    Element<Label>("ForgeStatusLabel"),
                    Element<Button>("ForgePreviousButton"),
                    Element<Button>("ForgeNextButton"),
                    Element<Button>("ForgeUpgradeButton"),
                    Element<Button>("ForgeCloseButton")),
                new PanelSpec(
                    "Gacha",
                    "Assets/Game/Features/Gacha/View/UI/GachaPanel.uxml",
                    Element<VisualElement>("GachaScreen"),
                    Element<VisualElement>("GachaPreviewList"),
                    Element<VisualElement>("GachaResultOverlay"),
                    Element<VisualElement>("GachaResultList"),
                    Element<Label>("GachaPoolNameLabel"),
                    Element<Label>("GachaPityLabel"),
                    Element<Label>("GachaTotalPullsLabel"),
                    Element<Label>("GachaPendingNoticeLabel"),
                    Element<Label>("GachaSinglePriceLabel"),
                    Element<Label>("GachaTenPriceLabel"),
                    Element<Label>("GachaResultTitle"),
                    Element<Label>("GachaStatusLabel"),
                    Element<Button>("GachaPullOnceButton"),
                    Element<Button>("GachaPullTenButton"),
                    Element<Button>("GachaSkipButton"),
                    Element<Button>("GachaConfirmButton"),
                    Element<Button>("GachaCloseButton")),
                new PanelSpec(
                    "SignIn",
                    "Assets/Game/Features/SignIn/View/UI/SignInPanel.uxml",
                    Element<VisualElement>("SignInScreen"),
                    Element<VisualElement>("SignInDayGrid"),
                    Element<VisualElement>("SignInMilestones"),
                    Element<VisualElement>("SignInMilestoneList"),
                    Element<Label>("SignInStreakLabel"),
                    Element<Label>("SignInMakeupCardLabel"),
                    Element<Label>("SignInEmptyLabel"),
                    Element<Label>("SignInStatusLabel"),
                    Element<Button>("SignInClaimButton"),
                    Element<Button>("SignInMakeupButton"),
                    Element<Button>("SignInCloseButton")),
                new PanelSpec(
                    "Achievement",
                    "Assets/Game/Features/Achievement/View/UI/AchievementPanel.uxml",
                    Element<VisualElement>("AchievementScreen"),
                    Element<VisualElement>("AchievementCategories"),
                    Element<VisualElement>("AchievementList"),
                    Element<ScrollView>("AchievementScroll"),
                    Element<Label>("AchievementAccountLabel"),
                    Element<Label>("AchievementXpLabel"),
                    Element<Label>("AchievementEmptyLabel"),
                    Element<Label>("AchievementStatusLabel"),
                    Element<Button>("AchievementTabAccountLevel"),
                    Element<Button>("AchievementTabAchievement"),
                    Element<Button>("AchievementCloseButton")),
                new PanelSpec(
                    "Social",
                    "Assets/Game/Features/Social/View/UI/SocialPanel.uxml",
                    Element<VisualElement>("SocialScreen"),
                    Element<VisualElement>("SocialList"),
                    Element<ScrollView>("SocialListScroll"),
                    Element<VisualElement>("SocialChat"),
                    Element<VisualElement>("SocialChatMessages"),
                    Element<ScrollView>("SocialChatScroll"),
                    Element<VisualElement>("SocialSearchRow"),
                    Element<TextField>("SocialSearchField"),
                    Element<TextField>("SocialChatInput"),
                    Element<Label>("SocialSearchResultLabel"),
                    Element<Label>("SocialChatPeerLabel"),
                    Element<Label>("SocialEmptyLabel"),
                    Element<Label>("SocialStatusLabel"),
                    Element<Button>("SocialSearchButton"),
                    Element<Button>("SocialSearchAddButton"),
                    Element<Button>("SocialChatSendButton"),
                    Element<Button>("SocialTabFriends"),
                    Element<Button>("SocialTabRequests"),
                    Element<Button>("SocialTabChat"),
                    Element<Button>("SocialTabBlocked"),
                    Element<Button>("SocialCloseButton"))
            };
        }

        private static RequiredElement Element<TElement>(string name)
            where TElement : VisualElement
        {
            return new RequiredElement(name, typeof(TElement));
        }
    }
}
