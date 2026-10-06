using Naraka.Features.Monster.Model;
using UnityEditor;
using UnityEngine;

namespace Naraka.EditorTools
{
    /// <summary>
    /// 正式暮影妖狼的资源地址与动画映射。
    ///
    /// 这些常量被三处共用：受控导入后的资源核查（`DuskshadowWolfAssetAudit`）、
    /// 场景装配（<see cref="P22CombatSetup"/>）与契约测试。
    /// 放在一处的理由很实际：动画映射如果各写一份，
    /// "装配工具建的 State" 与 "测试断言的 State" 就会各自漂移，
    /// 而漂移的表现是"测试全绿但狼不播动画"。
    ///
    /// 第三方资源保留在它自己的原始目录（`Assets/Polygonal Creatures Pack/`），
    /// 不搬进 `Assets/Game/`，`.meta` 与 GUID 也原样保留 ——
    /// 这样素材包升级时能直接对比，也不会把第三方文件和项目自有资产混在一起。
    /// </summary>
    public static class DuskshadowWolfAssets
    {
        /// <summary>第三方素材根目录。受控导入脚本 `Tools/import_polygonal_wolf.py` 写入这里。</summary>
        public const string ThirdPartyRoot = "Assets/Polygonal Creatures Pack/Polygonal Wolf";

        public const string ModelPath = ThirdPartyRoot + "/FBX/Polygonal Wolf.FBX";
        public const string ThirdPartyMaterialPath = ThirdPartyRoot + "/Materials/Polygonal Wolf Black.mat";
        public const string BaseTexturePath = ThirdPartyRoot + "/Textures/Polygonal Wolf Black.png";
        public const string GlowTexturePath = ThirdPartyRoot + "/Textures/Polygonal Wolf Black Glow.png";

        /// <summary>项目自有产物。第三方目录里什么都不生成。</summary>
        public const string PrefabPath = "Assets/Game/Settings/Monster/DuskshadowWolf.prefab";

        public const string ControllerPath = "Assets/Game/Settings/Monster/DuskshadowWolf.controller";

        public const string MaterialPath = "Assets/Game/Settings/Monster/DuskshadowWolfBody.mat";

        /// <summary>正式模型在 Prefab 下的视觉子物体名。</summary>
        public const string ModelChildName = "Model";

        /// <summary>
        /// 业务动作 → 第三方动画 FBX（不含扩展名）。
        ///
        /// 只用 `WO Root`（without root motion）那一套移动动画：移动由 NavMeshAgent 驱动，
        /// 带 Root 的版本会让动画再推一次世界坐标，表现为双倍位移。
        /// `@Breath Attack` 对应的是**已经存在**的赤瘴吐息技能，
        /// 不因为有这条动画就新增技能或改技能规则。
        /// </summary>
        public static readonly (MonsterAnimation Animation, string Fbx)[] AnimationToFbx =
        {
            (MonsterAnimation.Idle, "Polygonal Wolf@Idle"),
            (MonsterAnimation.Walk, "Polygonal Wolf@Walk Forward WO Root"),
            (MonsterAnimation.Run, "Polygonal Wolf@Run Forward WO Root"),
            (MonsterAnimation.Attack, "Polygonal Wolf@Bite Attack"),
            (MonsterAnimation.Skill, "Polygonal Wolf@Breath Attack"),
            (MonsterAnimation.HitStun, "Polygonal Wolf@Take Damage"),
            (MonsterAnimation.Death, "Polygonal Wolf@Die")
        };

        public static string AnimationFbxPath(string fbx) => $"{ThirdPartyRoot}/FBX/{fbx}.FBX";

        /// <summary>正式模型在不在工程里。不在时装配工具退回灰盒替身，而不是生成一个空壳。</summary>
        public static bool ModelIsImported =>
            AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath) != null;

        /// <summary>
        /// 实测几何（Idle 姿态，`NARAKA/Diag/Duskshadow Wolf Asset Audit` 测得）。
        ///
        /// 这些是摆碰撞体与判定盒的依据，不是猜的：
        /// 脚底 y ≈ −0.07（基本贴地，不需要补偿）、肩高 ≈ 0.69、
        /// 体长 Z ≈ 1.54、体宽 X ≈ 0.50，嘴部骨骼 `RigJaw` 在 (0, 0.40, 0.70)。
        /// </summary>
        public static class Measured
        {
            public const float FootHeight = -0.072f;
            public const float ShoulderHeight = 0.693f;
            public const float BodyLengthZ = 1.539f;
            public const float BodyWidthX = 0.497f;
            public const float JawHeight = 0.396f;
            public const float JawForward = 0.697f;
        }
    }
}
