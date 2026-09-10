using System;
using Naraka.Config;
using Naraka.Core.Application.Config;
using Naraka.Features.Lobby.View;
using Naraka.Infrastructure.Config;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Naraka.P0.Tests
{
    /// <summary>
    /// 大厅左下角头像与头像框的资产链路。
    ///
    /// 这条链路跨了三样东西：生成配置里的 <c>IconKey</c>、美术目录里的贴图文件名、
    /// 以及编辑器扫描出来的 <see cref="LobbyAppearanceCatalog"/>。三者任意一处对不上，
    /// 界面表现都是"头像不显示"，而且不会有任何报错——因为查不到贴图时代码是静默保留占位的。
    ///
    /// 因此这里用**真实**的配置文件与**真实**的目录资产做断言，而不是用替身。
    /// </summary>
    public sealed class LobbyAppearanceAssetTests
    {
        private const string CatalogPath = "Assets/Game/Settings/LobbyAppearanceCatalog.asset";

        private static GameConfigCatalog LoadRealConfig()
        {
            var provider = new StreamingAssetsGameConfigProvider();
            Assert.That(provider.IsLoaded, Is.True, "真实生成配置未能加载：" + provider.LoadError);
            return provider.Catalog;
        }

        private static LobbyAppearanceCatalog LoadRealCatalog()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<LobbyAppearanceCatalog>(CatalogPath);
            Assert.That(catalog, Is.Not.Null, "未找到 " + CatalogPath + "，请先运行编辑器装配菜单。");
            catalog.InvalidateIndex();
            return catalog;
        }

        [Test]
        public void TheGeneratedConfigContainsAvatarsAndFrames()
        {
            var config = LoadRealConfig();

            Assert.That(config.AvatarsInDisplayOrder.Count, Is.GreaterThan(0));
            Assert.That(config.AvatarFramesInDisplayOrder.Count, Is.GreaterThan(0));
        }

        [Test]
        public void EveryConfiguredAvatarHasATextureInTheCatalog()
        {
            var config = LoadRealConfig();
            var catalog = LoadRealCatalog();

            foreach (var avatar in config.AvatarsInDisplayOrder)
            {
                Assert.That(
                    catalog.GetByKey(avatar.IconKey),
                    Is.Not.Null,
                    $"头像 {avatar.AvatarId} 的 IconKey \"{avatar.IconKey}\" 在外观目录里没有对应贴图。");
            }
        }

        [Test]
        public void EveryConfiguredFrameHasATextureInTheCatalog()
        {
            var config = LoadRealConfig();
            var catalog = LoadRealCatalog();

            foreach (var frame in config.AvatarFramesInDisplayOrder)
            {
                Assert.That(
                    catalog.GetByKey(frame.IconKey),
                    Is.Not.Null,
                    $"头像框 {frame.AvatarFrameId} 的 IconKey \"{frame.IconKey}\" 在外观目录里没有对应贴图。");
            }
        }

        /// <summary>
        /// 服务端给新账号发的默认外观必须能在客户端画出来。
        ///
        /// 服务端取的是"按 SortOrder 排序后的第一项"，客户端必须用同一个规则解析，
        /// 否则新账号一登录就是一个查不到贴图的头像 ID。
        /// </summary>
        [Test]
        public void TheServerDefaultAppearanceResolvesToRealTextures()
        {
            var config = LoadRealConfig();
            var catalog = LoadRealCatalog();

            // 客户端的默认解析必须与服务端同规则：按 SortOrder 取第一项。
            Assert.That(config.DefaultAvatarId, Is.EqualTo(config.AvatarsInDisplayOrder[0].AvatarId));
            Assert.That(
                config.DefaultAvatarFrameId,
                Is.EqualTo(config.AvatarFramesInDisplayOrder[0].AvatarFrameId));

            Assert.That(config.TryGetAvatar(config.DefaultAvatarId, out var avatar), Is.True);
            Assert.That(config.TryGetAvatarFrame(config.DefaultAvatarFrameId, out var frame), Is.True);
            Assert.That(catalog.GetByKey(avatar.IconKey), Is.Not.Null, "默认头像贴图缺失。");
            Assert.That(catalog.GetByKey(frame.IconKey), Is.Not.Null, "默认头像框贴图缺失。");
        }

        [Test]
        public void LobbyMainDeclaresTheAvatarElements()
        {
            var layout = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                "Assets/Game/Features/Lobby/View/UI/LobbyMain.uxml");
            Assert.That(layout, Is.Not.Null, "未找到 LobbyMain.uxml。");

            var root = layout.CloneTree();
            Assert.That(
                root.Q<Button>("PlayerAvatarButton"),
                Is.Not.Null,
                "LobbyMain.uxml 缺少 PlayerAvatarButton。");
            Assert.That(
                root.Q<VisualElement>("PlayerAvatarFrame"),
                Is.Not.Null,
                "LobbyMain.uxml 缺少 PlayerAvatarFrame。");
        }
    }
}
