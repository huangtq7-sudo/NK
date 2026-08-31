using System.IO;
using Naraka.Boot;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Naraka.EditorTools
{
    public static class P0ProjectSetup
    {
        private const string SettingsDirectory = "Assets/Game/Settings";
        private const string RendererPath = SettingsDirectory + "/NarakaUniversalRenderer.asset";
        private const string PipelinePath = SettingsDirectory + "/NarakaUniversalRenderPipeline.asset";
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

            EditorSceneManager.SaveScene(scene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(BootScenePath, true) };
        }
    }
}
