using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Naraka.Features.Dialogue.View;
using Naraka.Features.Quest.View;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Naraka.EditorTools
{
    /// <summary>
    /// P4.1 NPC 与任务界面装配（用户 2026-10-12 授权由 Claude 完成原本的手工装配）。
    ///
    /// 只做加法：新建项目自有的 URP 材质、单状态 Idle 控制器、两个 NPC Prefab，
    /// 在 Map01 中放置两名 NPC 与一块任务 HUD 画布，然后保存 Map01。
    /// 不修改第三方资产与 .meta，不动玩家、镜头、加载、传送门与已有场景对象；
    /// 重复执行时先删除本工具上次创建的同名根对象再重建，因此结果可复现。
    /// </summary>
    public static class P41NpcQuestSetup
    {
        public const string Map01ScenePath = "Assets/Game/Scenes/World/Map01_Task.unity";
        public const string NpcRoot = "Assets/Game/Art/Characters/Npc";
        public const string HudRootName = "P41_QuestHud";
        public const string EventSystemName = "P41_EventSystem";
        public const string NpcGroupName = "P41_Npcs";

        /// <summary>主角模型约 4 单位高（Q-021）；NPC 按同一视觉尺度缩放，略矮于主角。</summary>
        public const float NpcTargetHeight = 3.8f;

        /// <summary>交互球半径。角色约 4 单位高，3.5 约等于贴近 NPC 一个身位。</summary>
        public const float InteractionRadius = 3.5f;

        /// <summary>两个角色模型与 Idle 片段的 Humanoid Avatar 都是"Copy From Other"，来源是这个文件。</summary>
        public const string AvatarSourcePath = "Assets/CombatGirlsCharacterPack/Humanoid_Bot/Models/Humanoid_F.fbx";

        private static readonly NpcSpec[] Npcs =
        {
            new NpcSpec(
                "npc_shuangchi",
                "NPC_ShenShuangchi",
                "Assets/CombatGirlsCharacterPack/School_Katana_Girl/Models/School_Katana_FullBody.fbx",
                "Assets/CombatGirlsCharacterPack/School_Katana_Girl/Animations/Normal/Idle.fbx",
                "Assets/CombatGirlsCharacterPack/School_Katana_Girl/Texture/",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { "Body", "Body/Body.png" },
                    { "Eye", "Body/Eye.png" },
                    { "Face", "Body/Face.png" },
                    { "Hair", "Hair/Katana_Hair_01.png" },
                    { "Uniform", "Cloth/Katana_Cloth_01.png" },
                    { "Sporswear", "Sportswear/Sportswear.png" },
                    { "Weapon_Katana", "Weapon/Katana_01.png" },
                    { "Acc_Glass", "Cloth/Katana_Glass.png" },
                    { "Acc_Glass_Alpha", null }
                },
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    { "Body_School_uniform", new[] { "Body" } },
                    { "Cloth_School_uniform_Shoes", new[] { "Uniform" } },
                    { "Cloth_School_uniform_Skirt", new[] { "Uniform" } },
                    { "Cloth_School_uniform_Top", new[] { "Uniform" } },
                    { "Colth_School_uniform_Armband", new[] { "Uniform" } },
                    { "Face_01", new[] { "Face", "Eye" } },
                    { "Katana_Hair", new[] { "Hair" } },
                    { "Weapon_Katana", new[] { "Weapon_Katana" } },
                    { "Weapon_Katana_sheath", new[] { "Weapon_Katana" } }
                }),
            new NpcSpec(
                "npc_yunsheng",
                "NPC_LuYunsheng",
                "Assets/CombatGirlsCharacterPack/CombatGirl_Sword/Models/CombatGirl_Sword.fbx",
                "Assets/CombatGirlsCharacterPack/CombatGirl_Sword/Animations/Normal/S_Idle.fbx",
                "Assets/CombatGirlsCharacterPack/CombatGirl_Sword/Texture/",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { "Body", "Body/Body.png" },
                    { "eye", "Body/Eye.png" },
                    { "Face", "Body/Face.png" },
                    { "Sword_Hair", "Hair/Sword_Hair.png" },
                    { "Sword_Cloth", "Cloth/Sword_Cloth.png" },
                    { "Squire_Cloth", "Squire_Cloth/Squire_Cloth.png" },
                    { "Weapon_Sword", "Weapon/Weapon_Sword.png" }
                },
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    { "Body_Sword", new[] { "Body" } },
                    { "Sword_Cloth_Belt", new[] { "Sword_Cloth" } },
                    { "Sword_Cloth_Boots", new[] { "Sword_Cloth" } },
                    { "Sword_Cloth_Cloak", new[] { "Sword_Cloth" } },
                    { "Sword_Cloth_Top", new[] { "Sword_Cloth" } },
                    { "Face_01Re", new[] { "Face", "eye" } },
                    { "Sword_Hair", new[] { "Sword_Hair" } },
                    { "Weapon_Sword", new[] { "Weapon_Sword" } }
                })
        };

        // ---------------------------------------------------------------- 只读检查

        [MenuItem("NARAKA/P4.1/Inspect NPC Assets And Map01")]
        public static void Inspect()
        {
            var report = new StringBuilder();
            foreach (var spec in Npcs)
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(spec.ModelPath);
                report.AppendLine($"== {spec.PrefabName}: model {(model == null ? "MISSING" : "ok")}");
                if (model == null)
                {
                    continue;
                }

                var avatar = LoadAvatar();
                report.AppendLine($"   avatar source: {(avatar == null ? "none" : $"{avatar.name} valid={avatar.isValid} human={avatar.isHuman}")}");
                var clip = LoadClip(spec.IdlePath);
                report.AppendLine($"   idle clip: {(clip == null ? "MISSING" : $"{clip.name} {clip.length:F2}s loop={clip.isLooping} human={clip.isHumanMotion}")}");
                foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
                {
                    report.AppendLine(
                        $"   renderer {renderer.name} active={renderer.gameObject.activeSelf} " +
                        $"mats=[{string.Join(",", renderer.sharedMaterials.Select(m => m == null ? "null" : m.name))}] " +
                        $"bounds={renderer.bounds.size}");
                }

                report.AppendLine($"   combined height={CombinedBounds(model).size.y:F3}");
            }

            var scene = EditorSceneManager.OpenScene(Map01ScenePath, OpenSceneMode.Single);
            foreach (var root in scene.GetRootGameObjects())
            {
                report.AppendLine($"   root {root.name} pos={root.transform.position} active={root.activeSelf}");
            }

            report.AppendLine($"   EventSystem in Map01: {UnityEngine.Object.FindObjectOfType<EventSystem>(true) != null}");
            var terrain = UnityEngine.Object.FindObjectOfType<Terrain>();
            report.AppendLine(terrain == null
                ? "   terrain: none"
                : $"   terrain {terrain.name} pos={terrain.transform.position} size={terrain.terrainData.size}");
            WriteReport("p41-inspect.txt", report);
        }

        public static void InspectBatch() => RunBatch(Inspect);

        // ---------------------------------------------------------------- 装配

        [MenuItem("NARAKA/P4.1/Assemble NPCs And Quest HUD Into Map01")]
        public static void Assemble()
        {
            var report = new StringBuilder();
            EnsureFolder(NpcRoot);
            EnsureFolder(NpcRoot + "/Materials");
            EnsureFolder(NpcRoot + "/Animators");

            var prefabs = new Dictionary<string, GameObject>(StringComparer.Ordinal);
            foreach (var spec in Npcs)
            {
                prefabs[spec.NpcId] = BuildNpcPrefab(spec, report);
            }

            AssetDatabase.SaveAssets();

            var scene = EditorSceneManager.OpenScene(Map01ScenePath, OpenSceneMode.Single);
            RemovePrevious(scene);
            PlaceNpcs(scene, prefabs, report);
            BuildHud(scene, report);

            if (!EditorSceneManager.SaveScene(scene, Map01ScenePath))
            {
                throw new InvalidOperationException("保存 Map01 失败。");
            }

            report.AppendLine("Map01 已保存。");
            WriteReport("p41-assemble.txt", report);
        }

        public static void AssembleBatch() => RunBatch(Assemble);

        private static GameObject BuildNpcPrefab(NpcSpec spec, StringBuilder report)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(spec.ModelPath)
                        ?? throw new InvalidOperationException("缺少模型 " + spec.ModelPath);
            var avatar = LoadAvatar();
            if (avatar == null || !avatar.isValid || !avatar.isHuman)
            {
                throw new InvalidOperationException(spec.PrefabName + " 的 Humanoid Avatar 无效，Idle 无法重定向。");
            }

            var clip = LoadClip(spec.IdlePath) ?? throw new InvalidOperationException("缺少 Idle 片段 " + spec.IdlePath);
            var controller = BuildIdleController(spec, clip);

            var root = new GameObject(spec.PrefabName);
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
                PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
                instance.name = "Model";
                instance.transform.SetParent(root.transform, false);

                // Unity 对象的"假 null"不能用 ?? 判断。
                var animator = instance.GetComponent<Animator>();
                if (animator == null)
                {
                    animator = instance.AddComponent<Animator>();
                }
                animator.avatar = avatar;
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

                // 按完整身体底模量身高：手持武器或高举的发饰会撑大整体包围盒，不能代表身高。
                var bodyFull = instance.GetComponentsInChildren<Renderer>(true)
                    .FirstOrDefault(renderer => renderer.name == "Body_Full");
                var height = Mathf.Max(0.01f, bodyFull != null ? bodyFull.bounds.size.y : CombinedBounds(instance).size.y);

                // 模型 FBX 把材质重映射到未导入的包内材质，导入后材质槽为空；按渲染器名重新指定项目自有材质。
                // 全身模型同时带多套服装、多张脸与裸身底模，只保留一套服装与第一张脸，其余隐藏。
                var materials = 0;
                var hidden = new List<string>();
                foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                {
                    if (!spec.Renderers.TryGetValue(renderer.name, out var keys))
                    {
                        // 直接移除不用的变体：留着隐藏的渲染器会带着空材质槽进入场景。
                        hidden.Add(renderer.name);
                        if (renderer.transform.childCount == 0)
                        {
                            UnityEngine.Object.DestroyImmediate(renderer.gameObject);
                        }
                        else
                        {
                            UnityEngine.Object.DestroyImmediate(renderer);
                        }

                        continue;
                    }

                    var shared = renderer.sharedMaterials;
                    var order = SubmeshOrderBySize(renderer, shared.Length);
                    for (var i = 0; i < shared.Length; i++)
                    {
                        // 脸部网格有"脸 + 眼"两个子网格：面积较大的是脸，较小的是眼。
                        var key = keys.Length == 1 ? keys[0] : keys[Math.Min(order[i], keys.Length - 1)];
                        shared[i] = BuildMaterial(spec, key, report);
                        materials++;
                    }

                    renderer.sharedMaterials = shared;
                    renderer.shadowCastingMode = ShadowCastingMode.On;
                }

                report.AppendLine($"   {spec.PrefabName}: 移除变体 {string.Join("、", hidden)}");

                var scale = NpcTargetHeight / height;
                instance.transform.localScale = Vector3.one * scale;
                report.AppendLine(
                    $"   {spec.PrefabName}: 原始高度 {height:F2}，缩放 {scale:F3}，目标高度 {NpcTargetHeight}，材质槽 {materials}");

                // 实体碰撞：玩家不能穿过 NPC。放在不缩放的子物体上，尺寸直接用世界单位。
                var body = new GameObject("BodyCollider");
                body.transform.SetParent(root.transform, false);
                var capsule = body.AddComponent<CapsuleCollider>();
                capsule.radius = 0.55f;
                capsule.height = NpcTargetHeight - 0.2f;
                capsule.center = new Vector3(0f, (NpcTargetHeight - 0.2f) * 0.5f, 0f);

                var sphere = root.AddComponent<SphereCollider>();
                sphere.isTrigger = true;
                sphere.radius = InteractionRadius;
                sphere.center = new Vector3(0f, 1.2f, 0f);
                var volume = root.AddComponent<NpcInteractionVolume>();
                var serialized = new SerializedObject(volume);
                serialized.FindProperty("npcId").stringValue = spec.NpcId;
                serialized.FindProperty("anchor").objectReferenceValue = root.transform;
                serialized.FindProperty("availableMarker").objectReferenceValue = Marker(root, "Marker_Available", "!", new Color(1f, 0.86f, 0.25f));
                serialized.FindProperty("objectiveMarker").objectReferenceValue = Marker(root, "Marker_Objective", "!", new Color(0.45f, 0.8f, 1f));
                serialized.FindProperty("submitMarker").objectReferenceValue = Marker(root, "Marker_Submit", "?", new Color(1f, 0.86f, 0.25f));
                serialized.FindProperty("rewardMarker").objectReferenceValue = Marker(root, "Marker_Reward", "?", new Color(1f, 0.6f, 0.2f));
                serialized.ApplyModifiedPropertiesWithoutUndo();

                var path = NpcRoot + "/" + spec.PrefabName + ".prefab";
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                report.AppendLine($"   {spec.PrefabName}: 已保存 {path}");
                return prefab;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static GameObject Marker(GameObject root, string name, string glyph, Color color)
        {
            var marker = new GameObject(name);
            marker.transform.SetParent(root.transform, false);
            marker.transform.localPosition = new Vector3(0f, NpcTargetHeight + 0.7f, 0f);
            var text = marker.AddComponent<TextMesh>();
            text.text = glyph;
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = 96;
            text.characterSize = 0.12f;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.fontStyle = FontStyle.Bold;
            text.color = color;
            marker.GetComponent<MeshRenderer>().sharedMaterial = text.font.material;
            marker.AddComponent<WorldBillboard>();
            marker.SetActive(false);
            return marker;
        }

        private static AnimatorController BuildIdleController(NpcSpec spec, AnimationClip clip)
        {
            var path = NpcRoot + "/Animators/" + spec.PrefabName + "_Idle.controller";
            AssetDatabase.DeleteAsset(path);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            var state = controller.layers[0].stateMachine.AddState("Idle");
            state.motion = clip;
            controller.layers[0].stateMachine.defaultState = state;
            return controller;
        }

        private static Material BuildMaterial(NpcSpec spec, string slot, StringBuilder report)
        {
            var key = spec.Textures.Keys.FirstOrDefault(name => string.Equals(name, slot, StringComparison.OrdinalIgnoreCase));
            if (key == null)
            {
                report.AppendLine($"   [提示] {spec.PrefabName} 的材质槽 '{slot}' 没有贴图映射，使用 Body 贴图。");
                key = "Body";
            }

            var path = $"{NpcRoot}/Materials/{spec.PrefabName}_{key}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }

            material.name = $"{spec.PrefabName}_{key}";
            material.SetFloat("_Smoothness", 0.15f);
            material.SetFloat("_Metallic", 0f);
            var relative = spec.Textures[key];
            if (relative == null)
            {
                // 镜片：无贴图的半透明材质。
                MakeTransparent(material, new Color(0.85f, 0.9f, 1f, 0.25f));
            }
            else
            {
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(spec.TextureRoot + relative)
                              ?? throw new InvalidOperationException("缺少贴图 " + spec.TextureRoot + relative);
                material.SetTexture("_BaseMap", texture);
                material.SetColor("_BaseColor", Color.white);
                var importer = (TextureImporter)AssetImporter.GetAtPath(spec.TextureRoot + relative);
                var cutout = importer != null && importer.DoesSourceTextureHaveAlpha() &&
                             (key.IndexOf("Hair", StringComparison.OrdinalIgnoreCase) >= 0 ||
                              key.IndexOf("Eye", StringComparison.OrdinalIgnoreCase) >= 0 ||
                              key.IndexOf("Face", StringComparison.OrdinalIgnoreCase) >= 0 ||
                              key.IndexOf("Glass", StringComparison.OrdinalIgnoreCase) >= 0);
                material.SetFloat("_AlphaClip", cutout ? 1f : 0f);
                material.SetFloat("_Cutoff", 0.5f);
                if (cutout)
                {
                    material.EnableKeyword("_ALPHATEST_ON");
                    material.renderQueue = (int)RenderQueue.AlphaTest;
                }
                else
                {
                    material.DisableKeyword("_ALPHATEST_ON");
                    material.renderQueue = -1;
                }

                // 头发与眼部通常是单面片，双面渲染避免从背后看是空的。
                material.SetFloat("_Cull", cutout ? 0f : 2f);
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        private static void MakeTransparent(Material material, Color color)
        {
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;
        }

        // ---------------------------------------------------------------- 场景

        private static void RemovePrevious(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == NpcGroupName || root.name == HudRootName || root.name == EventSystemName)
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }
        }

        private static void PlaceNpcs(Scene scene, Dictionary<string, GameObject> prefabs, StringBuilder report)
        {
            var roots = scene.GetRootGameObjects();
            var portal = roots.FirstOrDefault(root => root.name == "Portal_To_Map02");
            var spawn = roots.FirstOrDefault(root => root.name == "SpawnPoint_Entry");
            var terrain = UnityEngine.Object.FindObjectOfType<Terrain>();
            if (portal == null || spawn == null || terrain == null)
            {
                throw new InvalidOperationException("Map01 缺少 Portal_To_Map02、SpawnPoint_Entry 或 Terrain。");
            }

            Physics.SyncTransforms();
            var group = new GameObject(NpcGroupName);
            SceneManager.MoveGameObjectToScene(group, scene);

            var portalPos = portal.transform.position;
            var spawnPos = spawn.transform.position;
            var toSpawn = Flat(spawnPos - portalPos);
            if (toSpawn.sqrMagnitude < 0.01f)
            {
                toSpawn = Vector3.forward;
            }

            toSpawn.Normalize();
            var side = Vector3.Cross(Vector3.up, toSpawn);

            // 沈霜迟：守在传送门靠出生点一侧，偏开主路 4 米，面向出生点。
            var guardBase = portalPos + (toSpawn * 7f) + (side * 4f);
            var guard = FindGround(terrain, guardBase, spawnPos.y, 6f, report, "沈霜迟");
            Place(group, prefabs["npc_shuangchi"], guard, spawnPos, report);

            // 陆云笙：离沈霜迟 35–55 米、地势平缓、无遮挡的地点，优先朝出生点一侧，
            // 让"接取 → 交谈 → 复命"是一段不长的往返。
            var healer = Vector3.zero;
            var found = false;
            foreach (var distance in new[] { 40f, 35f, 45f, 50f, 55f })
            {
                for (var step = 0; step < 16 && !found; step++)
                {
                    var angle = (step % 2 == 0 ? 1f : -1f) * (30f + ((step / 2) * 20f));
                    var direction = Quaternion.Euler(0f, angle, 0f) * toSpawn;
                    var candidate = guard + (direction * distance);
                    if (TryGround(terrain, candidate, spawnPos.y, out var ground))
                    {
                        healer = ground;
                        found = true;
                    }
                }

                if (found)
                {
                    break;
                }
            }

            if (!found)
            {
                healer = FindGround(terrain, guard + (toSpawn * 40f), spawnPos.y, 10f, report, "陆云笙");
            }

            Place(group, prefabs["npc_yunsheng"], healer, guard, report);
            report.AppendLine(
                $"   传送门 {portalPos}，出生点 {spawnPos}；两名 NPC 相距 {Vector3.Distance(guard, healer):F1} 米。");
        }

        private static void Place(GameObject group, GameObject prefab, Vector3 position, Vector3 lookAt, StringBuilder report)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group.scene);
            instance.transform.SetParent(group.transform, true);
            instance.transform.position = position;
            var facing = Flat(lookAt - position);
            if (facing.sqrMagnitude > 0.01f)
            {
                instance.transform.rotation = Quaternion.LookRotation(facing.normalized, Vector3.up);
            }

            report.AppendLine($"   放置 {prefab.name} 于 {position}，朝向 {instance.transform.eulerAngles.y:F0}°");
        }

        private static Vector3 FindGround(
            Terrain terrain,
            Vector3 around,
            float referenceHeight,
            float searchRadius,
            StringBuilder report,
            string label)
        {
            if (TryGround(terrain, around, referenceHeight, out var ground))
            {
                return ground;
            }

            for (var ring = 1; ring <= 6; ring++)
            {
                for (var step = 0; step < 12; step++)
                {
                    var offset = Quaternion.Euler(0f, step * 30f, 0f) * Vector3.forward * (searchRadius * ring / 6f);
                    if (TryGround(terrain, around + offset, referenceHeight, out ground))
                    {
                        return ground;
                    }
                }
            }

            report.AppendLine($"   [警告] {label} 附近没有找到平坦空地，按地形高度放在 {around}。");
            return new Vector3(around.x, terrain.SampleHeight(around) + terrain.transform.position.y, around.z);
        }

        /// <summary>地形表面、坡度小于 15°、高差不超过 4 米、周围 1.5 米内没有非触发碰撞体。</summary>
        private static bool TryGround(Terrain terrain, Vector3 xz, float referenceHeight, out Vector3 ground)
        {
            var data = terrain.terrainData;
            var origin = terrain.transform.position;
            var normalized = new Vector2((xz.x - origin.x) / data.size.x, (xz.z - origin.z) / data.size.z);
            ground = Vector3.zero;
            if (normalized.x < 0.05f || normalized.x > 0.95f || normalized.y < 0.05f || normalized.y > 0.95f)
            {
                return false;
            }

            var height = terrain.SampleHeight(xz) + origin.y;
            ground = new Vector3(xz.x, height, xz.z);
            if (Mathf.Abs(height - referenceHeight) > 4f ||
                data.GetSteepness(normalized.x, normalized.y) > 15f)
            {
                return false;
            }

            var probe = ground + (Vector3.up * 2.5f);
            foreach (var collider in Physics.OverlapCapsule(ground + (Vector3.up * 1.6f), probe, 1.5f))
            {
                if (!collider.isTrigger && !(collider is TerrainCollider))
                {
                    return false;
                }
            }

            // 头顶不能被建筑覆盖：从高处往下打，第一下必须打在地形上。
            if (Physics.Raycast(ground + (Vector3.up * 60f), Vector3.down, out var hit, 70f) && !(hit.collider is TerrainCollider))
            {
                return false;
            }

            return true;
        }

        // ---------------------------------------------------------------- HUD

        private static void BuildHud(Scene scene, StringBuilder report)
        {
            var font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            var canvasObject = new GameObject(HudRootName, typeof(RectTransform));
            SceneManager.MoveGameObjectToScene(canvasObject, scene);
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasObject.AddComponent<GraphicRaycaster>();

            // 底部对话框
            var dialogue = Child(canvasObject, "DialogueBox", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f));
            Stretch(dialogue, 0f, 0f, 0f, 0f, 260f);
            var dialogueRoot = Panel(dialogue, "Root", new Color(0.04f, 0.05f, 0.07f, 0.82f));
            Inset(dialogueRoot, 260f, 260f, 36f, 0f);
            var speaker = Label(dialogueRoot, "SpeakerName", font, 32, FontStyle.Bold, TextAnchor.UpperLeft, Color.white);
            Anchor(speaker, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(40f, -52f), new Vector2(-40f, -12f));
            var body = Label(dialogueRoot, "Body", font, 30, FontStyle.Normal, TextAnchor.UpperLeft, new Color(0.93f, 0.93f, 0.93f));
            body.supportRichText = true;
            Anchor(body, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(40f, 28f), new Vector2(-80f, -64f));
            var hint = Label(dialogueRoot, "ContinueHint", font, 26, FontStyle.Normal, TextAnchor.LowerRight, new Color(1f, 0.86f, 0.4f));
            hint.text = "▼";
            Anchor(hint, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-80f, 16f), new Vector2(-24f, 56f));
            Bind(dialogue.AddComponent<DialogueBoxView>(), ("root", dialogueRoot.gameObject), ("speakerNameLabel", speaker),
                ("bodyLabel", body), ("continueHint", hint.gameObject));

            // E 交谈提示与短提示
            var prompt = Child(canvasObject, "InteractPrompt", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
            Size(prompt, new Vector2(0f, 330f), new Vector2(520f, 120f));
            var promptRoot = Panel(prompt, "PromptRoot", new Color(0f, 0f, 0f, 0.55f));
            Anchor(promptRoot, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-170f, 0f), new Vector2(170f, 52f));
            var promptLabel = Label(promptRoot, "PromptLabel", font, 26, FontStyle.Normal, TextAnchor.MiddleCenter, Color.white);
            Fill(promptLabel);
            var noticeRoot = Panel(prompt, "NoticeRoot", new Color(0f, 0f, 0f, 0.55f));
            Anchor(noticeRoot, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-260f, 64f), new Vector2(260f, 112f));
            var noticeLabel = Label(noticeRoot, "NoticeLabel", font, 24, FontStyle.Normal, TextAnchor.MiddleCenter, new Color(1f, 0.9f, 0.6f));
            Fill(noticeLabel);
            Bind(prompt.AddComponent<InteractPromptView>(), ("promptRoot", promptRoot.gameObject), ("promptLabel", promptLabel),
                ("noticeRoot", noticeRoot.gameObject), ("noticeLabel", noticeLabel));

            // 任务追踪栏（右侧中部，避开左上小地图、左下头像与底部快捷栏）
            var tracker = Child(canvasObject, "QuestTracker", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));
            Size(tracker, new Vector2(-40f, 140f), new Vector2(440f, 150f));
            var trackerRoot = Panel(tracker, "Root", new Color(0f, 0f, 0f, 0.45f));
            Fill(trackerRoot);
            var title = Label(trackerRoot, "Title", font, 26, FontStyle.Bold, TextAnchor.UpperLeft, new Color(1f, 0.84f, 0.45f));
            Anchor(title, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(20f, -48f), new Vector2(-20f, -12f));
            var objective = Label(trackerRoot, "Objective", font, 22, FontStyle.Normal, TextAnchor.UpperLeft, Color.white);
            Anchor(objective, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(20f, 36f), new Vector2(-20f, -54f));
            var status = Label(trackerRoot, "Status", font, 20, FontStyle.Normal, TextAnchor.LowerLeft, new Color(1f, 0.55f, 0.5f));
            Anchor(status, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(20f, 8f), new Vector2(-20f, 34f));
            Bind(tracker.AddComponent<QuestTrackerView>(), ("root", trackerRoot.gameObject), ("titleLabel", title),
                ("objectiveLabel", objective), ("statusLabel", status));

            // 领取面板
            var reward = Child(canvasObject, "QuestRewardPanel", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            Size(reward, Vector2.zero, new Vector2(640f, 420f));
            var rewardRoot = Panel(reward, "Root", new Color(0.05f, 0.06f, 0.08f, 0.92f));
            Fill(rewardRoot);
            var rewardTitle = Label(rewardRoot, "Title", font, 32, FontStyle.Bold, TextAnchor.MiddleCenter, new Color(1f, 0.84f, 0.45f));
            Anchor(rewardTitle, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(24f, -80f), new Vector2(-24f, -20f));
            var rewards = Label(rewardRoot, "Rewards", font, 26, FontStyle.Normal, TextAnchor.UpperCenter, Color.white);
            Anchor(rewards, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(40f, 120f), new Vector2(-40f, -96f));
            var claim = MakeButton(rewardRoot, "ClaimButton", "领取", font, new Color(0.72f, 0.52f, 0.18f, 1f));
            Anchor(claim.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-230f, 32f), new Vector2(-20f, 92f));
            var close = MakeButton(rewardRoot, "CloseButton", "关闭", font, new Color(0.3f, 0.32f, 0.36f, 1f));
            Anchor(close.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(20f, 32f), new Vector2(230f, 92f));
            var busy = Label(rewardRoot, "Busy", font, 22, FontStyle.Italic, TextAnchor.MiddleCenter, new Color(0.8f, 0.8f, 0.8f));
            busy.text = "领取中…";
            Anchor(busy, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(24f, 96f), new Vector2(-24f, 122f));
            busy.gameObject.SetActive(false);
            Bind(reward.AddComponent<QuestRewardPanelView>(), ("root", rewardRoot.gameObject), ("titleLabel", rewardTitle),
                ("rewardsLabel", rewards), ("claimButton", claim), ("closeButton", close), ("busyRoot", busy.gameObject));

            dialogueRoot.gameObject.SetActive(false);
            promptRoot.gameObject.SetActive(false);
            noticeRoot.gameObject.SetActive(false);
            trackerRoot.gameObject.SetActive(false);
            rewardRoot.gameObject.SetActive(false);

            // 正式场景不放 EventSystem：P2.3 场景派生工具会移除场景里的 EventSystem（保持其幂等）。
            // 领取面板在运行时缺少 EventSystem 时自行创建一个，随地图场景一起卸载。
            report.AppendLine("   任务 HUD：底部对话框、E 提示、右侧追踪栏、领取面板已创建并绑定。");
        }

        private static GameObject Child(GameObject parent, string name, Vector2 min, Vector2 max, Vector2 pivot)
        {
            var child = new GameObject(name, typeof(RectTransform));
            child.transform.SetParent(parent.transform, false);
            var rect = child.GetComponent<RectTransform>();
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.pivot = pivot;
            return child;
        }

        private static void Stretch(GameObject target, float left, float right, float bottom, float top, float height)
        {
            var rect = target.GetComponent<RectTransform>();
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, bottom + height);
        }

        private static void Size(GameObject target, Vector2 position, Vector2 size)
        {
            var rect = target.GetComponent<RectTransform>();
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static RectTransform Panel(GameObject parent, string name, Color color)
        {
            var panel = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            panel.transform.SetParent(parent.transform, false);
            var image = panel.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return panel.GetComponent<RectTransform>();
        }

        private static void Inset(RectTransform rect, float left, float right, float bottom, float top)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        private static void Fill(Component component) => Inset(component.GetComponent<RectTransform>(), 0f, 0f, 0f, 0f);

        private static Text Label(RectTransform parent, string name, Font font, int size, FontStyle style, TextAnchor anchor, Color color)
        {
            var label = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            label.transform.SetParent(parent, false);
            var text = label.GetComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = anchor;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            text.text = string.Empty;
            return text;
        }

        private static void Anchor(Component component, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
        {
            var rect = component.GetComponent<RectTransform>();
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        private static Button MakeButton(RectTransform parent, string name, string caption, Font font, Color color)
        {
            var buttonObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            var image = buttonObject.GetComponent<Image>();
            image.color = color;
            var button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;
            var label = Label(buttonObject.GetComponent<RectTransform>(), "Label", font, 28, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            label.text = caption;
            Fill(label);
            return button;
        }

        private static void Bind(Component view, params (string Field, UnityEngine.Object Value)[] fields)
        {
            var serialized = new SerializedObject(view);
            foreach (var (field, value) in fields)
            {
                var property = serialized.FindProperty(field) ?? throw new InvalidOperationException(view.GetType().Name + " 缺少字段 " + field);
                property.objectReferenceValue = value;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---------------------------------------------------------------- 工具

        private static Avatar LoadAvatar() =>
            AssetDatabase.LoadAllAssetsAtPath(AvatarSourcePath).OfType<Avatar>().FirstOrDefault();

        /// <summary>
        /// 返回每个子网格按包围盒体积从大到小的名次（0 = 最大）。
        /// 用于把"脸/眼"两张贴图对应到导入后没有材质名的两个子网格上。
        /// </summary>
        private static int[] SubmeshOrderBySize(Renderer renderer, int count)
        {
            var order = new int[count];
            var mesh = renderer is SkinnedMeshRenderer skinned
                ? skinned.sharedMesh
                : renderer.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh == null || mesh.subMeshCount < count)
            {
                for (var i = 0; i < count; i++)
                {
                    order[i] = i;
                }

                return order;
            }

            var volumes = Enumerable.Range(0, count)
                .Select(i =>
                {
                    var size = mesh.GetSubMesh(i).bounds.size;
                    return (Index: i, Volume: size.x * size.y + size.y * size.z + size.x * size.z);
                })
                .OrderByDescending(entry => entry.Volume)
                .ToArray();
            for (var rank = 0; rank < volumes.Length; rank++)
            {
                order[volumes[rank].Index] = rank;
            }

            return order;
        }

        private static AnimationClip LoadClip(string path) =>
            AssetDatabase.LoadAllAssetsAtPath(path)
                .OfType<AnimationClip>()
                .FirstOrDefault(clip => !clip.name.StartsWith("__preview__", StringComparison.Ordinal));

        private static Bounds CombinedBounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(false);
            if (renderers.Length == 0)
            {
                return new Bounds(root.transform.position, Vector3.one);
            }

            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return bounds;
        }

        private static Vector3 Flat(Vector3 value) => new Vector3(value.x, 0f, value.z);

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent))
            {
                EnsureFolder(parent);
            }

            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static void WriteReport(string fileName, StringBuilder report)
        {
            var directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "artifacts", "p41"));
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, fileName), report.ToString(), new UTF8Encoding(false));
            Debug.Log(report.ToString());
        }

        private static void RunBatch(Action action)
        {
            try
            {
                action();
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        private sealed class NpcSpec
        {
            public NpcSpec(
                string npcId,
                string prefabName,
                string modelPath,
                string idlePath,
                string textureRoot,
                Dictionary<string, string> textures,
                Dictionary<string, string[]> renderers)
            {
                NpcId = npcId;
                PrefabName = prefabName;
                ModelPath = modelPath;
                IdlePath = idlePath;
                TextureRoot = textureRoot;
                Textures = textures;
                Renderers = renderers;
            }

            public string NpcId { get; }

            public string PrefabName { get; }

            public string ModelPath { get; }

            public string IdlePath { get; }

            public string TextureRoot { get; }

            public Dictionary<string, string> Textures { get; }

            /// <summary>保留的渲染器及其各子网格使用的贴图键。未列出的渲染器（其他服装、表情变体）隐藏。</summary>
            public Dictionary<string, string[]> Renderers { get; }
        }
    }
}
