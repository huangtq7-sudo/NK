using System.IO;
using System.Linq;
using Naraka.Boot;
using Naraka.Features.Account.View;
using Naraka.Features.Bootstrap.View;
using Naraka.Features.Loading.View;
using Naraka.Features.Forge.View;
using Naraka.Features.Achievement.View;
using Naraka.Features.Gacha.View;
using Naraka.Features.RedDot.View;
using Naraka.Features.SignIn.View;
using Naraka.Features.Social.View;
using Naraka.Features.Inventory.View;
using Naraka.Features.Loadout.View;
using Naraka.Features.Shop.View;
using Naraka.Features.Lobby.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;
using UnityEngine.Video;

namespace Naraka.EditorTools
{
    public static class P0ProjectSetup
    {
        private const string SettingsDirectory = "Assets/Game/Settings";
        private const string RendererPath = SettingsDirectory + "/NarakaUniversalRenderer.asset";
        private const string PipelinePath = SettingsDirectory + "/NarakaUniversalRenderPipeline.asset";
        private const string PanelSettingsPath = SettingsDirectory + "/P0PanelSettings.asset";
        private const string BootScenePath = "Assets/Scenes/SampleScene.unity";
        private const string AccountLoginUxmlPath =
            "Assets/Game/Features/Account/View/UI/AccountLogin.uxml";
        private const string AccountLoginLayoutField = "loginLayout";
        private const string LobbyMainUxmlPath =
            "Assets/Game/Features/Lobby/View/UI/LobbyMain.uxml";
        private const string LobbyMainLayoutField = "lobbyLayout";
        private const string AppearanceUxmlPath =
            "Assets/Game/Features/Lobby/View/UI/LobbyAppearance.uxml";
        private const string LoadingUxmlPath =
            "Assets/Game/Features/Loading/View/UI/LoadingScreen.uxml";
        private const string AppearanceCatalogPath =
            SettingsDirectory + "/LobbyAppearanceCatalog.asset";
        private const string FeaturePanelUxmlPath =
            "Assets/Game/Features/Lobby/View/UI/LobbyFeaturePanel.uxml";
        private const string FeaturePanelCatalogPath =
            SettingsDirectory + "/LobbyFeaturePanelCatalog.asset";
        private const string FeatureBackgroundPrefix = "ui_bg_";

        // 顺序必须与 LobbyFeature 枚举一致；值是 ui_bg_ 之后的文件名片段。
        private static readonly string[] FeatureBackgroundTokens =
        {
            "hero",
            "weapon",
            "forge",
            "shop",
            "warehouse",
            "checkin",
            "gacha",
            "account_level_reward",
            "friends",
            "chat"
        };
        private const string HeroPanelUxmlPath =
            "Assets/Game/Features/Loadout/View/UI/HeroPanel.uxml";

        private const string WeaponPanelUxmlPath =
            "Assets/Game/Features/Loadout/View/UI/WeaponPanel.uxml";

        private const string InventoryPanelUxmlPath =
            "Assets/Game/Features/Inventory/View/UI/InventoryPanel.uxml";

        private const string ShopPanelUxmlPath =
            "Assets/Game/Features/Shop/View/UI/ShopPanel.uxml";

        private const string ForgePanelUxmlPath =
            "Assets/Game/Features/Forge/View/UI/ForgePanel.uxml";

        private const string GachaPanelUxmlPath =
            "Assets/Game/Features/Gacha/View/UI/GachaPanel.uxml";

        private const string SignInPanelUxmlPath =
            "Assets/Game/Features/SignIn/View/UI/SignInPanel.uxml";

        private const string AchievementPanelUxmlPath =
            "Assets/Game/Features/Achievement/View/UI/AchievementPanel.uxml";

        private const string SocialPanelUxmlPath =
            "Assets/Game/Features/Social/View/UI/SocialPanel.uxml";

        private const string LoadoutIconCatalogPath =
            SettingsDirectory + "/LoadoutIconCatalog.asset";

        /// <summary>装备界面用到的贴图前缀：英雄立绘、技能图标与兵器图标。</summary>
        private static readonly string[] LoadoutIconPrefixes =
        {
            "hero_portrait_",
            "skill_",
            "nav_weapon",
            // 仓库与商店的物品图标共用同一份目录，配置里的 IconKey 就是这些文件名。
            "item_",
            "material_",
            "armor_",
            "souljade_",
            "currency_",
            "slice",
            "pet_",
            // 抽奖结果卡背按品质取用。
            "gacha_card"
        };

        private const string MaterialDirectory = "Assets/Game/Art/UI/Material";
        private const string AvatarPrefix = "avatar";
        private const string FramePrefix = "gacha_card";
        private const string CursorTexturePath = MaterialDirectory + "/MouseCursor.png";
        private const string LobbyVideoPath =
            "Assets/Game/Art/UI/Backgrounds/Lobby_Animation-1.mp4";

        [MenuItem("NARAKA/Setup/Apply P0 Project Settings")]
        public static void Apply()
        {
            Directory.CreateDirectory(SettingsDirectory);

            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, RendererPath);
            }

            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
            }

            GraphicsSettings.renderPipelineAsset = pipeline;
            QualitySettings.renderPipeline = pipeline;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.companyName = "NARAKA";
            PlayerSettings.productName = "NARAKA";

            EnsureBootScene();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("NARAKA P0 project settings applied successfully.");
        }

        private static void EnsureBootScene()
        {
            var scene = EditorSceneManager.OpenScene(BootScenePath, OpenSceneMode.Single);
            if (Object.FindObjectOfType<GameLifetimeScope>() == null)
            {
                var gameObject = new GameObject("GameLifetimeScope");
                gameObject.AddComponent<GameLifetimeScope>();
                EditorSceneManager.MarkSceneDirty(scene);
            }

            EnsureP0ClientShell();

            EditorSceneManager.SaveScene(scene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(BootScenePath, true) };
        }

        private static void EnsureP0ClientShell()
        {
            var panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (panelSettings == null)
            {
                panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(panelSettings, PanelSettingsPath);
            }

            panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panelSettings.referenceResolution = new Vector2Int(1920, 1080);
            panelSettings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panelSettings.match = 0.5f;
            EditorUtility.SetDirty(panelSettings);

            var accountView = Object.FindObjectOfType<AccountView>();
            var shell = accountView != null ? accountView.gameObject : new GameObject("P0ClientShell");
            var document = shell.GetComponent<UIDocument>() ?? shell.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            var account = shell.GetComponent<AccountView>() ?? shell.AddComponent<AccountView>();
            var lobby = shell.GetComponent<LobbyView>() ?? shell.AddComponent<LobbyView>();

            // ConfigVersionView最后添加，使全新装配的Shell也让版本预检覆盖层位于最上层。
            if (shell.GetComponent<ConfigVersionView>() == null)
            {
                shell.AddComponent<ConfigVersionView>();
            }

            var appearance = shell.GetComponent<LobbyAppearanceView>()
                ?? shell.AddComponent<LobbyAppearanceView>();
            var featurePanel = shell.GetComponent<LobbyFeaturePanelView>()
                ?? shell.AddComponent<LobbyFeaturePanelView>();
            var heroPanel = shell.GetComponent<HeroPanelView>() ?? shell.AddComponent<HeroPanelView>();
            var weaponPanel = shell.GetComponent<WeaponPanelView>() ?? shell.AddComponent<WeaponPanelView>();
            var inventoryPanel = shell.GetComponent<InventoryPanelView>()
                ?? shell.AddComponent<InventoryPanelView>();
            var shopPanel = shell.GetComponent<ShopPanelView>() ?? shell.AddComponent<ShopPanelView>();
            var forgePanel = shell.GetComponent<ForgePanelView>() ?? shell.AddComponent<ForgePanelView>();
            var gachaPanel = shell.GetComponent<GachaPanelView>() ?? shell.AddComponent<GachaPanelView>();
            var signInPanel = shell.GetComponent<SignInPanelView>() ?? shell.AddComponent<SignInPanelView>();
            var achievementPanel = shell.GetComponent<AchievementPanelView>()
                ?? shell.AddComponent<AchievementPanelView>();
            var socialPanel = shell.GetComponent<SocialPanelView>() ?? shell.AddComponent<SocialPanelView>();

            // 红点角标没有自己的 UXML：它把角标挂到已有的大厅入口按钮上。
            if (shell.GetComponent<RedDotBadgeView>() == null)
            {
                shell.AddComponent<RedDotBadgeView>();
            }
            var loading = shell.GetComponent<LoadingView>() ?? shell.AddComponent<LoadingView>();
            var cursor = shell.GetComponent<GameCursor>() ?? shell.AddComponent<GameCursor>();

            RemoveLegacyUiFontComponent(shell);

            var catalog = EnsureAppearanceCatalog();
            AssignReference(account, AccountLoginLayoutField,
                AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(AccountLoginUxmlPath));
            AssignReference(lobby, LobbyMainLayoutField,
                AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(LobbyMainUxmlPath));
            AssignReference(lobby, "catalog", catalog);
            AssignReference(lobby, "backgroundClip", ConfigureLobbyVideo());
            AssignReference(appearance, "appearanceLayout",
                AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(AppearanceUxmlPath));
            AssignReference(appearance, "catalog", catalog);
            AssignReference(loading, "loadingLayout",
                AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(LoadingUxmlPath));
            AssignReference(featurePanel, "featurePanelLayout",
                AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(FeaturePanelUxmlPath));
            AssignReference(featurePanel, "catalog", EnsureFeaturePanelCatalog());
            var loadoutIcons = EnsureLoadoutIconCatalog();
            AssignReference(heroPanel, "heroLayout",
                AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(HeroPanelUxmlPath));
            AssignReference(heroPanel, "icons", loadoutIcons);
            AssignReference(weaponPanel, "weaponLayout",
                AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(WeaponPanelUxmlPath));
            AssignReference(weaponPanel, "icons", loadoutIcons);
            AssignReference(inventoryPanel, "inventoryLayout",
                AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(InventoryPanelUxmlPath));
            AssignReference(inventoryPanel, "icons", loadoutIcons);
            AssignReference(shopPanel, "shopLayout",
                AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(ShopPanelUxmlPath));
            AssignReference(shopPanel, "icons", loadoutIcons);
            AssignReference(forgePanel, "forgeLayout",
                AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(ForgePanelUxmlPath));
            AssignReference(forgePanel, "icons", loadoutIcons);
            AssignReference(gachaPanel, "gachaLayout",
                AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(GachaPanelUxmlPath));
            AssignReference(gachaPanel, "icons", loadoutIcons);
            AssignReference(signInPanel, "signInLayout",
                AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(SignInPanelUxmlPath));
            AssignReference(achievementPanel, "achievementLayout",
                AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(AchievementPanelUxmlPath));
            AssignReference(socialPanel, "socialLayout",
                AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(SocialPanelUxmlPath));
            var cursorTexture = ConfigureCursorTexture();
            AssignReference(cursor, "cursorTexture", cursorTexture);
            AssignVector2(cursor, "hotspot", ComputeCursorHotspot());
        }

        // GameUiFont 曾用系统动态字体覆盖 UI 根节点字体，导致运行时所有文字不显示，已废弃。
        // 这里清理旧场景里可能残留的组件，包括脚本已删除后留下的空引用。
        private static void RemoveLegacyUiFontComponent(GameObject shell)
        {
            var components = shell.GetComponents<Component>();
            for (var i = components.Length - 1; i >= 0; i--)
            {
                var component = components[i];
                if (component == null)
                {
                    continue;
                }

                if (component.GetType().Name == "GameUiFont")
                {
                    Object.DestroyImmediate(component);
                    Debug.Log("已移除遗留的 GameUiFont 组件。");
                }
            }

            // 脚本文件已删除时，上面的循环只会看到 null，必须单独清理缺失脚本条目。
            var removed = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(shell);
            if (removed > 0)
            {
                Debug.Log($"已清理 {removed} 个缺失脚本组件。");
            }
        }

        private static LobbyFeaturePanelCatalog EnsureFeaturePanelCatalog()
        {
            Directory.CreateDirectory(SettingsDirectory);
            var catalog = AssetDatabase.LoadAssetAtPath<LobbyFeaturePanelCatalog>(FeaturePanelCatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<LobbyFeaturePanelCatalog>();
                AssetDatabase.CreateAsset(catalog, FeaturePanelCatalogPath);
            }

            var paths = AssetDatabase.FindAssets("t:Texture2D", new[] { MaterialDirectory })
                .Select(AssetDatabase.GUIDToAssetPath)
                .ToArray();

            var serialized = new SerializedObject(catalog);
            var property = serialized.FindProperty("backgrounds");
            if (property == null)
            {
                Debug.LogError("LobbyFeaturePanelCatalog 缺少序列化字段 backgrounds。");
                return catalog;
            }

            property.arraySize = FeatureBackgroundTokens.Length;
            var matched = 0;
            for (var i = 0; i < FeatureBackgroundTokens.Length; i++)
            {
                var expected = FeatureBackgroundPrefix + FeatureBackgroundTokens[i] + "_";
                var path = paths.FirstOrDefault(candidate =>
                    Path.GetFileName(candidate).StartsWith(expected, System.StringComparison.OrdinalIgnoreCase));
                if (path == null)
                {
                    Debug.LogWarning($"未找到功能面板背景图：{expected}*。");
                    property.GetArrayElementAtIndex(i).objectReferenceValue = null;
                    continue;
                }

                property.GetArrayElementAtIndex(i).objectReferenceValue =
                    AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                matched++;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);
            Debug.Log($"功能面板背景目录：匹配到 {matched}/{FeatureBackgroundTokens.Length} 项。");
            return catalog;
        }

        /// <summary>
        /// 扫描装备界面需要的贴图。按前缀匹配而不是逐个登记文件名：
        /// 新增一个英雄或技能图标后只需要重新扫描，不必改代码。
        /// </summary>
        private static LoadoutIconCatalog EnsureLoadoutIconCatalog()
        {
            Directory.CreateDirectory(SettingsDirectory);
            var catalog = AssetDatabase.LoadAssetAtPath<LoadoutIconCatalog>(LoadoutIconCatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<LoadoutIconCatalog>();
                AssetDatabase.CreateAsset(catalog, LoadoutIconCatalogPath);
            }

            var paths = AssetDatabase.FindAssets("t:Texture2D", new[] { MaterialDirectory })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => LoadoutIconPrefixes.Any(prefix =>
                    Path.GetFileName(path).StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase)))
                .OrderBy(path => path, System.StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var serialized = new SerializedObject(catalog);
            var property = serialized.FindProperty("textures");
            if (property == null)
            {
                Debug.LogError("LoadoutIconCatalog 缺少序列化字段 textures。");
                return catalog;
            }

            property.arraySize = paths.Length;
            for (var i = 0; i < paths.Length; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue =
                    AssetDatabase.LoadAssetAtPath<Texture2D>(paths[i]);
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            catalog.InvalidateIndex();
            EditorUtility.SetDirty(catalog);
            Debug.Log($"装备图标目录：扫描到 {paths.Length} 张贴图。");
            return catalog;
        }

        [MenuItem("NARAKA/Setup/Rescan Loadout Icons")]
        public static void RescanLoadoutIcons()
        {
            EnsureLoadoutIconCatalog();
            AssetDatabase.SaveAssets();
        }

        [MenuItem("NARAKA/Setup/Rescan Appearance Catalog")]
        public static void RescanAppearanceCatalog()
        {
            EnsureAppearanceCatalog();
            AssetDatabase.SaveAssets();
        }

        private static LobbyAppearanceCatalog EnsureAppearanceCatalog()
        {
            Directory.CreateDirectory(SettingsDirectory);
            var catalog = AssetDatabase.LoadAssetAtPath<LobbyAppearanceCatalog>(AppearanceCatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<LobbyAppearanceCatalog>();
                AssetDatabase.CreateAsset(catalog, AppearanceCatalogPath);
            }

            var serialized = new SerializedObject(catalog);
            WriteTextureArray(serialized, "avatars", AvatarPrefix);
            WriteTextureArray(serialized, "frames", FramePrefix);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);
            return catalog;
        }

        private static void WriteTextureArray(SerializedObject serialized, string fieldName, string prefix)
        {
            var paths = AssetDatabase.FindAssets("t:Texture2D", new[] { MaterialDirectory })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => Path.GetFileName(path).StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, System.StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var property = serialized.FindProperty(fieldName);
            if (property == null)
            {
                Debug.LogError($"LobbyAppearanceCatalog 缺少序列化字段 {fieldName}。");
                return;
            }

            property.arraySize = paths.Length;
            for (var i = 0; i < paths.Length; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue =
                    AssetDatabase.LoadAssetAtPath<Texture2D>(paths[i]);
            }

            Debug.Log($"外观目录 {fieldName}：扫描到 {paths.Length} 项（前缀 {prefix}）。");
        }

        // 源视频为 H.264 High Profile，Unity 直接播放时会报时间戳偏移与色彩标准缺失，
        // 表现为循环卡顿和颜色偏差。开启 Transcode 让 Unity 重新编码成自身可靠支持的格式。
        private static VideoClip ConfigureLobbyVideo()
        {
            var importer = AssetImporter.GetAtPath(LobbyVideoPath) as VideoClipImporter;
            if (importer == null)
            {
                Debug.LogError($"未找到大厅背景视频：{LobbyVideoPath}。");
                return null;
            }

            var settings = importer.GetTargetSettings("Standalone") ?? new VideoImporterTargetSettings();
            var needsReimport = !settings.enableTranscoding || importer.importAudio;
            settings.enableTranscoding = true;
            settings.codec = VideoCodec.H264;
            settings.bitrateMode = VideoBitrateMode.High;
            settings.spatialQuality = VideoSpatialQuality.HighSpatialQuality;
            settings.resizeMode = VideoResizeMode.OriginalSize;
            importer.SetTargetSettings("Standalone", settings);
            importer.importAudio = false;
            if (needsReimport)
            {
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<VideoClip>(LobbyVideoPath);
        }

        // 硬件指针要求贴图使用 Cursor 导入类型，否则可能被压缩或带Mipmap而显示异常。
        private static Texture2D ConfigureCursorTexture()
        {
            var importer = AssetImporter.GetAtPath(CursorTexturePath) as TextureImporter;
            if (importer == null)
            {
                Debug.LogError($"未找到指针贴图：{CursorTexturePath}。");
                return null;
            }

            if (importer.textureType != TextureImporterType.Cursor)
            {
                importer.textureType = TextureImporterType.Cursor;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(CursorTexturePath);
        }

        // 热点取箭头尖端：自上而下找到第一行足够不透明的像素，再取该行不透明像素的水平中心。
        // 阈值设得高是为了跳过抗锯齿边缘，否则算出的x会向图形较宽的一侧偏移。
        private static Vector2 ComputeCursorHotspot()
        {
            if (!File.Exists(CursorTexturePath))
            {
                return Vector2.zero;
            }

            var probe = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!probe.LoadImage(File.ReadAllBytes(CursorTexturePath)))
                {
                    Debug.LogError($"无法解码指针贴图：{CursorTexturePath}。");
                    return Vector2.zero;
                }

                var pixels = probe.GetPixels32();
                for (var y = 0; y < probe.height; y++)
                {
                    var minX = int.MaxValue;
                    var maxX = int.MinValue;
                    for (var x = 0; x < probe.width; x++)
                    {
                        // Texture2D 的原点在左下，指针热点以左上为原点，这里翻转行号。
                        if (pixels[(probe.height - 1 - y) * probe.width + x].a <= 200)
                        {
                            continue;
                        }

                        if (x < minX)
                        {
                            minX = x;
                        }

                        if (x > maxX)
                        {
                            maxX = x;
                        }
                    }

                    if (maxX >= minX)
                    {
                        var hotspot = new Vector2((minX + maxX) / 2, y);
                        Debug.Log($"指针热点计算结果：{hotspot}（贴图 {probe.width}x{probe.height}）。");
                        return hotspot;
                    }
                }

                return Vector2.zero;
            }
            finally
            {
                Object.DestroyImmediate(probe);
            }
        }

        private static void AssignVector2(Component view, string fieldName, Vector2 value)
        {
            var serialized = new SerializedObject(view);
            var property = serialized.FindProperty(fieldName);
            if (property == null)
            {
                Debug.LogError($"{view.GetType().Name} 缺少序列化字段 {fieldName}。");
                return;
            }

            if (property.vector2Value == value)
            {
                return;
            }

            property.vector2Value = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(view);
            EditorSceneManager.MarkSceneDirty(view.gameObject.scene);
        }

        private static void AssignReference(Component view, string fieldName, UnityEngine.Object value)
        {
            if (value == null)
            {
                // 视频等可选资源缺失时保留现有引用，不覆盖为空。
                Debug.LogWarning($"{view.GetType().Name}.{fieldName} 的目标资源不存在，已跳过绑定。");
                return;
            }

            var serialized = new SerializedObject(view);
            var property = serialized.FindProperty(fieldName);
            if (property == null)
            {
                Debug.LogError($"{view.GetType().Name} 缺少序列化字段 {fieldName}。");
                return;
            }

            if (property.objectReferenceValue == value)
            {
                return;
            }

            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(view);
            EditorSceneManager.MarkSceneDirty(view.gameObject.scene);
        }
    }
}
