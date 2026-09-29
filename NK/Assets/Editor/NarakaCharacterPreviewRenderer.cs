using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class NarakaCharacterPreviewRenderer
{
    private const string ImportRoot = "Assets/Aquarius Fantasy - High Elves/Demo Scenes/High Elves Sanctuary/Imported Characters";

    [MenuItem("Tools/Naraka/Render Imported Character Previews")]
    public static void RenderAll()
    {
        string outputDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "artifacts", "NarakaUnityPreviews"));
        Directory.CreateDirectory(outputDirectory);

        RenderModel(
            ImportRoot + "/魏青关山海/魏青关山海.fbx",
            "weiqing",
            outputDirectory);
        RenderModel(
            ImportRoot + "/张起灵_水墨/张起灵_水墨.fbx",
            "zhangqiling",
            outputDirectory);

        Debug.Log("Naraka preview rendering complete: " + outputDirectory);
    }

    private static void RenderModel(string assetPath, string outputName, string outputDirectory)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        if (prefab == null)
            throw new InvalidOperationException("Unable to load preview model: " + assetPath);

        Scene previewScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        try
        {
            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            instance.name = prefab.name;
            instance.SetActive(true);
            foreach (SkinnedMeshRenderer renderer in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                renderer.updateWhenOffscreen = true;

            Bounds bounds = CalculateBounds(instance);
            Vector3 center = bounds.center;
            float height = Mathf.Max(bounds.size.y, 0.1f);
            float width = Mathf.Max(bounds.size.x, bounds.size.z);
            Debug.Log($"Naraka preview: {prefab.name}, renderers={instance.GetComponentsInChildren<Renderer>(true).Length}, " +
                      $"center={bounds.center}, size={bounds.size}.");

            GameObject cameraObject = new GameObject("Preview Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.12f, 0.13f, 0.15f, 1f);
            camera.fieldOfView = 26f;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 100f;
            camera.allowHDR = false;
            camera.allowMSAA = true;

            CreateDirectionalLight(previewScene, "Key Light", new Vector3(35f, -35f, 0f), 1.35f, new Color(1f, 0.93f, 0.84f));
            CreateDirectionalLight(previewScene, "Fill Light", new Vector3(20f, 145f, 0f), 0.65f, new Color(0.72f, 0.82f, 1f));
            CreateDirectionalLight(previewScene, "Rim Light", new Vector3(65f, 180f, 0f), 0.8f, new Color(0.75f, 0.85f, 1f));
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.32f, 0.32f, 0.34f);

            float fullDistance = Mathf.Max(height * 0.5f / Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * 0.5f), width * 1.35f) * 1.12f;
            // Render the frontal frame last. On a cold batch-mode start the
            // first URP draw can still be warming shader variants.
            foreach (float yaw in new[] { 90f, 180f, 270f, 0f })
            {
                Vector3 direction = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                PositionCamera(camera, center, direction, fullDistance);
                SaveCamera(camera, Path.Combine(outputDirectory, outputName + "_full_" + ((int)yaw) + ".png"), 720, 960);
            }

            Vector3 faceTarget = new Vector3(center.x, bounds.min.y + height * 0.82f, center.z);
            float faceDistance = Mathf.Max(height * 0.52f, width * 0.72f);
            foreach (float yaw in new[] { 0f, 180f })
            {
                Vector3 direction = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                PositionCamera(camera, faceTarget, direction, faceDistance);
                SaveCamera(camera, Path.Combine(outputDirectory, outputName + "_face_" + ((int)yaw) + ".png"), 720, 720);
            }
        }
        finally
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }
    }

    private static Bounds CalculateBounds(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true)
            .Where(renderer => renderer.enabled)
            .ToArray();
        if (renderers.Length == 0)
            return new Bounds(root.transform.position, Vector3.one);
        Bounds bounds = renderers[0].bounds;
        for (int index = 1; index < renderers.Length; index++)
            bounds.Encapsulate(renderers[index].bounds);
        return bounds;
    }

    private static void PositionCamera(Camera camera, Vector3 target, Vector3 direction, float distance)
    {
        camera.transform.position = target - direction.normalized * distance;
        camera.transform.rotation = Quaternion.LookRotation(target - camera.transform.position, Vector3.up);
    }

    private static void CreateDirectionalLight(Scene scene, string name, Vector3 eulerAngles, float intensity, Color color)
    {
        GameObject lightObject = new GameObject(name);
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = intensity;
        light.color = color;
        light.shadows = LightShadows.Soft;
        lightObject.transform.rotation = Quaternion.Euler(eulerAngles);
    }

    private static void SaveCamera(Camera camera, string path, int width, int height)
    {
        RenderTexture renderTexture = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
        RenderTexture previous = RenderTexture.active;
        Texture2D image = null;
        try
        {
            camera.targetTexture = renderTexture;
            camera.Render();
            RenderTexture.active = renderTexture;
            image = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply(false, false);
            File.WriteAllBytes(path, image.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = null;
            RenderTexture.active = previous;
            if (image != null)
                UnityEngine.Object.DestroyImmediate(image);
            RenderTexture.ReleaseTemporary(renderTexture);
        }
    }
}
