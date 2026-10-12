using System;
using System.IO;
using System.Linq;
using System.Text;
using Naraka.Features.Quest.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Naraka.EditorTools
{
    /// <summary>
    /// P4.1 装配结果的只读截图：打开 Map01、把 NPC 摆成 Idle 姿态、填一行示例对白后渲染到 PNG。
    /// 不保存场景，不写任何资产；截图写到仓库外层的 artifacts/p41。
    /// </summary>
    public static class P41NpcQuestCapture
    {
        [MenuItem("NARAKA/P4.1/Capture NPC And Dialogue Screenshots")]
        public static void Capture()
        {
            var report = new StringBuilder();
            var scene = EditorSceneManager.OpenScene(P41NpcQuestSetup.Map01ScenePath, OpenSceneMode.Single);
            var volumes = UnityEngine.Object.FindObjectsOfType<NpcInteractionVolume>(true);
            report.AppendLine($"NPC 交互范围：{volumes.Length} 个 [{string.Join("、", volumes.Select(v => v.NpcId))}]");

            foreach (var volume in volumes)
            {
                var animator = volume.GetComponentInChildren<Animator>(true);
                var clip = animator != null && animator.runtimeAnimatorController != null
                    ? animator.runtimeAnimatorController.animationClips.FirstOrDefault()
                    : null;
                if (clip != null)
                {
                    AnimationMode.StartAnimationMode();
                    AnimationMode.BeginSampling();
                    AnimationMode.SampleAnimationClip(animator.gameObject, clip, clip.length * 0.3f);
                    AnimationMode.EndSampling();
                }

                var target = volume.transform.position + (Vector3.up * 2f);
                var eye = volume.transform.position + (volume.transform.forward * 7f) + (Vector3.up * 3f);
                Render(target, eye, null, $"npc-{volume.NpcId}.png", report);
                if (clip != null)
                {
                    AnimationMode.StopAnimationMode();
                }

                var sphere = volume.GetComponent<SphereCollider>();
                report.AppendLine(
                    $"  {volume.NpcId} 位置 {volume.transform.position} 半径 {sphere.radius} Trigger={sphere.isTrigger} " +
                    $"Animator={(animator != null && animator.avatar != null && animator.avatar.isHuman)} Idle={(clip != null ? clip.name : "无")}");
            }

            // 对话框示例：把画布切到相机空间渲染，填入第一句接取对白。
            var hud = GameObject.Find(P41NpcQuestSetup.HudRootName);
            if (hud != null && volumes.Length > 0)
            {
                var dialogueRoot = hud.transform.Find("DialogueBox/Root");
                dialogueRoot.gameObject.SetActive(true);
                dialogueRoot.Find("SpeakerName").GetComponent<Text>().text = "沈霜迟";
                dialogueRoot.Find("Body").GetComponent<Text>().text =
                    "站住。此门之后妖氛弥漫，<color=#00000000>来者何人？</color>";
                var tracker = hud.transform.Find("QuestTracker/Root");
                tracker.gameObject.SetActive(true);
                tracker.Find("Title").GetComponent<Text>().text = "入世·问路";
                tracker.Find("Objective").GetComponent<Text>().text = "前往沈霜迟处接取任务";
                var prompt = hud.transform.Find("InteractPrompt/PromptRoot");
                prompt.gameObject.SetActive(true);
                prompt.Find("PromptLabel").GetComponent<Text>().text = "E  交谈 · 沈霜迟";

                var shen = volumes.First(v => v.NpcId == "npc_shuangchi");
                var target = shen.transform.position + (Vector3.up * 2f);
                var eye = shen.transform.position + (shen.transform.forward * 9f) + (Vector3.up * 3.5f);
                Render(target, eye, hud.GetComponent<Canvas>(), "dialogue-hud.png", report);
            }

            var directory = OutputDirectory();
            File.WriteAllText(Path.Combine(directory, "p41-capture.txt"), report.ToString(), new UTF8Encoding(false));
            Debug.Log(report.ToString());

            // 截图只在内存里改动了场景：重新打开，丢弃改动。
            EditorSceneManager.OpenScene(P41NpcQuestSetup.Map01ScenePath, OpenSceneMode.Single);
        }

        public static void CaptureBatch()
        {
            try
            {
                Capture();
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        private static void Render(Vector3 target, Vector3 eye, Canvas canvas, string fileName, StringBuilder report)
        {
            const int width = 1280;
            const int height = 720;
            var cameraObject = new GameObject("P41_CaptureCamera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.transform.position = eye;
            camera.transform.LookAt(target);
            camera.fieldOfView = 50f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 1500f;
            var texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = texture;

            RenderMode previousMode = default;
            if (canvas != null)
            {
                previousMode = canvas.renderMode;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1f;
                Canvas.ForceUpdateCanvases();
            }

            camera.Render();
            RenderTexture.active = texture;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();
            RenderTexture.active = null;
            var path = Path.Combine(OutputDirectory(), fileName);
            File.WriteAllBytes(path, image.EncodeToPNG());
            report.AppendLine($"  截图 {fileName}");

            if (canvas != null)
            {
                canvas.renderMode = previousMode;
                canvas.worldCamera = null;
            }

            camera.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(image);
            UnityEngine.Object.DestroyImmediate(texture);
            UnityEngine.Object.DestroyImmediate(cameraObject);
        }

        private static string OutputDirectory()
        {
            var directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "artifacts", "p41"));
            Directory.CreateDirectory(directory);
            return directory;
        }
    }
}
