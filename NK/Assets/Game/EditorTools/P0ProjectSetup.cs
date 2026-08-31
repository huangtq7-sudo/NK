using System.IO;
using Naraka.Boot;
using Naraka.Features.Account.View;
using Naraka.Features.Bootstrap.View;
using Naraka.Features.Lobby.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace Naraka.EditorTools
{
    public static class P0ProjectSetup
    {
        private const string SettingsDirectory = "Assets/Game/Settings";
        private const string RendererPath = SettingsDirectory + "/NarakaUniversalRenderer.asset";
        private const string PipelinePath = SettingsDirectory + "/NarakaUniversalRenderPipeline.asset";
        private const string PanelSettingsPath = SettingsDirectory + "/P0PanelSettings.asset";
        private const string BootScenePath = "Assets/Scenes/SampleScene.unity";

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
            if (shell.GetComponent<AccountView>() == null)
            {
                shell.AddComponent<AccountView>();
            }

            if (shell.GetComponent<ConfigVersionView>() == null)
            {
                shell.AddComponent<ConfigVersionView>();
            }

            if (shell.GetComponent<LobbyView>() == null)
            {
                shell.AddComponent<LobbyView>();
            }
        }
    }
}
