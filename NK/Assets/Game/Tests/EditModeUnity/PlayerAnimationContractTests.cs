using Naraka.EditorTools;
using Naraka.Features.Character.Model;
using Naraka.Features.Character.View;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Naraka.Unity.EditMode.Tests
{
    /// <summary>
    /// 动画与配置之间的契约。
    ///
    /// 这些断言存在的原因是一个真实事故：初版把每个动作时长都写成了估计值
    /// （待机动作 3 秒 vs 实际 15.07 秒、Burst02 2 秒 vs 实际 5.5 秒），
    /// 状态机按配置时长结束动作，于是所有比配置长的动画都被拦腰截断。
    /// 时长只有一个真相 —— 动画片段本身。换动画之后这里会立刻失败，
    /// 而不是等到有人在游戏里看见动作被砍掉。
    /// </summary>
    public sealed class PlayerAnimationContractTests
    {
        private static PlayerTuningAsset LoadTuning()
        {
            var asset = AssetDatabase.LoadAssetAtPath<PlayerTuningAsset>(P2SceneSetup.PlayerTuningPath);
            Assert.That(asset, Is.Not.Null, $"找不到 {P2SceneSetup.PlayerTuningPath}。");
            return asset;
        }

        private static float ClipLength(string fbx)
        {
            var clip = P2AnimationSetup.LoadClip($"{P2AnimationSetup.AnimationRoot}/{fbx}.fbx");
            Assert.That(clip, Is.Not.Null, $"找不到动画 {fbx}。");
            return clip.length;
        }

        [TestCase("AM_Stand1_Action03_SEQ1", "idleVariationDurationSeconds")]
        [TestCase("Stop_Walk_R", "stopWalkSeconds")]
        [TestCase("Stop_Run_R", "stopRunSeconds")]
        [TestCase("Run_Turnback", "runTurnbackSeconds")]
        [TestCase("Behit_B_L", "hitStunSeconds")]
        [TestCase("AM_Death", "deathSeconds")]
        public void ConfiguredDurationMatchesTheClipLength(string fbx, string field)
        {
            var serialized = new SerializedObject(LoadTuning());
            var property = serialized.FindProperty(field);
            Assert.That(property, Is.Not.Null, $"PlayerTuningAsset 缺少字段 {field}。");

            Assert.That(
                property.floatValue, Is.EqualTo(ClipLength(fbx)).Within(0.001f),
                $"{field} 必须等于 {fbx} 的实际长度，否则动画会被拦腰截断。" +
                "请执行菜单 NARAKA/Setup/Rebuild Player Animator。");
        }

        [TestCase("Move_F", "dash")]
        [TestCase("Attack01", "combo1")]
        [TestCase("AM_Summon", "combo2")]
        [TestCase("Attack02", "combo3")]
        [TestCase("Attack10", "charge")]
        [TestCase("AM_Skill01", "skillF")]
        [TestCase("Attack04_1", "skillV")]
        [TestCase("Burst02", "spawnLobbyToMap01")]
        [TestCase("Burst01", "spawnMap01ToMap02")]
        public void ConfiguredAttackDurationMatchesTheClipLength(string fbx, string field)
        {
            var serialized = new SerializedObject(LoadTuning());
            var entry = serialized.FindProperty(field);
            Assert.That(entry, Is.Not.Null, $"PlayerTuningAsset 缺少字段 {field}。");
            var clipSeconds = entry.FindPropertyRelative("clipSeconds");

            Assert.That(
                clipSeconds.floatValue, Is.EqualTo(ClipLength(fbx)).Within(0.001f),
                $"{field}.clipSeconds 必须等于 {fbx} 的实际长度。");
        }

        [TestCase("combo1")]
        [TestCase("combo2")]
        [TestCase("combo3")]
        [TestCase("charge")]
        [TestCase("skillF")]
        [TestCase("skillV")]
        public void AttackWindowsStayInsideTheActionDuration(string field)
        {
            var serialized = new SerializedObject(LoadTuning());
            var entry = serialized.FindProperty(field);
            var duration = entry.FindPropertyRelative("clipSeconds").floatValue;

            foreach (var window in new[]
                     {
                         "hitWindowStart", "hitWindowEnd", "comboWindowStart", "comboWindowEnd"
                     })
            {
                var value = entry.FindPropertyRelative(window).floatValue;
                Assert.That(
                    value, Is.InRange(0f, duration + 0.001f),
                    $"{field}.{window} 落在动作时长之外，永远不会被触发。");
            }

            Assert.That(
                entry.FindPropertyRelative("hitWindowEnd").floatValue,
                Is.GreaterThan(entry.FindPropertyRelative("hitWindowStart").floatValue),
                $"{field} 的命中窗必须有正长度。");
        }

        [Test]
        public void LoopingClipsAreTheOnlyOnesMarkedLooping()
        {
            foreach (var (_, fbx) in P2AnimationSetup.StateToFbx)
            {
                var clip = P2AnimationSetup.LoadClip($"{P2AnimationSetup.AnimationRoot}/{fbx}.fbx");
                if (clip == null)
                {
                    continue;
                }

                var shouldLoop = System.Array.IndexOf(P2AnimationSetup.LoopingFbx, fbx) >= 0;
                Assert.That(
                    clip.isLooping, Is.EqualTo(shouldLoop),
                    $"{fbx} 的循环设置与映射表不一致（应为 {shouldLoop}）。");
            }
        }

        [Test]
        public void PlayerPrefabCancelsTheBakedRootMotion()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Settings/Player/Changli_Player.prefab");
            Assert.That(prefab, Is.Not.Null, "找不到玩家 Prefab。");

            var canceller = prefab.GetComponentInChildren<RootMotionCanceller>(true);
            Assert.That(
                canceller, Is.Not.Null,
                "玩家 Prefab 必须挂 RootMotionCanceller：" +
                "这些动画在根骨骼上烘焙了水平位移，不抵消就会在动作结束时回退一小步。");

            var animator = prefab.GetComponentInChildren<Animator>(true);
            Assert.That(animator, Is.Not.Null);
            Assert.That(
                animator.applyRootMotion, Is.False,
                "Root Motion 不是位移真相，位移必须由代码驱动（ADR-0015）。");
        }

        [Test]
        public void BakedRootMotionIsLargeEnoughToMatter()
        {
            // 这条测试记录的是"为什么需要抵消器"这件事实本身。
            // 如果将来动画真的换成了根节点完全不动的版本，它会失败并提醒我们抵消器可以撤掉。
            var delta = RootTranslationDelta("Move_F");

            Assert.That(
                delta.magnitude, Is.GreaterThan(1f),
                "Move_F 仍然在根节点上烘焙了整体位移，因此 RootMotionCanceller 必须保留。");
        }

        [TestCase("Move_F", "dash", TestName = "冲刺的烘焙位移等于配置位移")]
        [TestCase("Attack04_1", "skillV", TestName = "V 技能的烘焙位移等于配置位移")]
        public void TheBakedRootDisplacementMatchesTheConfiguredDisplacement(
            string fbx, string field)
        {
            // 这条断言是导入期轴向还原是否正确的最强证据。
            //
            // 配置里的位移值（冲刺 12.276、V 技能 17.838）当初就是从旧片段烘焙的
            // 根位移量出来的。2026-10-04 的新导出把整条根通道绕 X 转了 +90° 并缩了
            // 1/2.54，于是这些值一度"失去了可测量的来源"。导入期还原之后它们又对上了，
            // 而且是分毫不差 —— 还原错一点点这里就会失败。
            var serialized = new SerializedObject(LoadTuning());
            var entry = serialized.FindProperty(field);
            Assert.That(entry, Is.Not.Null, $"PlayerTuningAsset 缺少字段 {field}。");
            var configured = entry.FindPropertyRelative("forwardDisplacement").floatValue;

            var delta = RootTranslationDelta(fbx);
            var horizontal = new Vector2(delta.x, delta.z).magnitude;

            Assert.That(
                horizontal, Is.EqualTo(configured).Within(0.01f),
                $"{fbx} 烘焙的水平行程是 {horizontal:F3}，配置里是 " +
                $"{configured:F3}。两者必须一致：" +
                "不一致说明导入期的轴向还原不对，或者动画换了导出约定。");
            Assert.That(
                Mathf.Abs(delta.y), Is.LessThan(PlayerAnimationRootFixup.VerticalDriftTolerance),
                $"{fbx} 是纯水平位移动作，根节点不该有垂直净变化。");
        }

        [Test]
        public void EveryClipsRootVerticalChannelReturnsToWhereItStarted()
        {
            // 项目级不变量：游戏里没有跳跃，角色由 CharacterController 恒定贴地，
            // 所以片段播完之后根节点必须回到起始高度。净变化不为 0 只可能是导出残留
            // —— 而且因为抵消器保留垂直分量，残留会直接表现为角色沉进地里。
            // 实测 Attack01 的净变化曾经是 −3.167，由导入期压平。
            foreach (var (state, fbx) in P2AnimationSetup.StateToFbx)
            {
                var drift = RootTranslationDelta(fbx).y;
                Assert.That(
                    Mathf.Abs(drift), Is.LessThan(PlayerAnimationRootFixup.VerticalDriftTolerance),
                    $"{state}（{fbx}）的根节点垂直净变化是 {drift:F3}，" +
                    "角色会停在地下或空中。导入期应当把它压平。");
            }
        }

        [Test]
        public void TheChargeKeepsItsVerticalLeapAndThePrefabLetsItThrough()
        {
            // 用户报告"蓄力动画没有飞上天空，而是在原地旋转"。腾空确实在片段里：
            // 还原之后是升到 +5.227（角色身高 4.04）再落回 +0.062 的一条弧线，
            // 烘焙在根节点的垂直通道上。抵消器一旦连垂直一起锁，它就消失。
            var clip = P2AnimationSetup.LoadClip($"{P2AnimationSetup.AnimationRoot}/Attack10.fbx");
            Assert.That(clip, Is.Not.Null);

            using (var model = new SampledModel())
            {
                var peak = 0f;
                for (var i = 0; i <= 44; i++)
                {
                    model.Sample(clip, clip.length * i / 44f);
                    peak = Mathf.Max(peak, model.RootLocalPosition.y);
                }

                Assert.That(
                    peak, Is.GreaterThan(4f),
                    $"蓄力的腾空峰值只有 {peak:F3}，弧线被抹平了。");
            }

            Assert.That(
                Mathf.Abs(RootTranslationDelta("Attack10").y),
                Is.LessThan(PlayerAnimationRootFixup.VerticalDriftTolerance),
                "蓄力必须落回起始高度，否则角色会留在空中。");

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Settings/Player/Changli_Player.prefab");
            Assert.That(prefab, Is.Not.Null);

            var canceller = prefab.GetComponentInChildren<RootMotionCanceller>(true);
            Assert.That(canceller, Is.Not.Null);
            Assert.That(
                canceller.PreserveVertical, Is.True,
                "抵消器必须只锁水平：锁掉垂直分量，蓄力就从飞上天空变成原地旋转。");
        }

        [Test]
        public void TheOfficialModelRootRestMatchesTheConventionTheImportFixupAssumes()
        {
            // `PlayerAnimationRootFixup` 用一个常量表示"正确的 Root 静止姿态"：
            // 3ds Max 是 Z 轴朝上，导出 FBX 时每个文件的 Root 都带 PreRotation(-90,0,0)，
            // Unity 把它折进 localRotation。这条断言让那个常量不是凭空写的 ——
            // 模型哪天被重新导出、约定变了，这里会先失败。
            using (var model = new SampledModel())
            {
                Assert.That(
                    Quaternion.Angle(model.RestRotation, PlayerAnimationRootFixup.ExpectedRootRotation),
                    Is.LessThan(PlayerAnimationRootFixup.RotationToleranceDegrees),
                    "正式模型的 Root 静止姿态变了，导入期修正用的常量必须跟着重新确认。");
                Assert.That(model.RestScale.x, Is.EqualTo(1f).Within(0.001f));
            }
        }

        [Test]
        public void EveryAnimationKeepsTheSkeletonUprightAndFullSize()
        {
            // 2026-10-04 的事故：新导出在 Root 上多写了 +90 度的 X 旋转与 0.3937 的缩放，
            // 于是 15 个动作一播放角色就躺在地上、缩到 39%（用户报告"贴在地上，方向轴不对"）。
            //
            // 判据取"角色的上方向"而不是"Root 的旋转等于静止值"：
            // Root 的局部 +Z 就是角色头顶方向（静止姿态 Rx(-90) 把它映射成世界 +Y），
            // 这样 `Run_Turnback` 绕头顶轴转 180 度不会误报，而整体翻倒一定会被抓到。
            using (var model = new SampledModel())
            {
                foreach (var (state, fbx) in P2AnimationSetup.StateToFbx)
                {
                    var clip = P2AnimationSetup.LoadClip($"{P2AnimationSetup.AnimationRoot}/{fbx}.fbx");
                    Assert.That(clip, Is.Not.Null, $"找不到动画 {fbx}。");

                    for (var i = 0; i <= 8; i++)
                    {
                        model.Sample(clip, clip.length * i / 8f);

                        Assert.That(
                            Vector3.Angle(model.CharacterUp, Vector3.up), Is.LessThan(5f),
                            $"{state}（{fbx}）在 {i}/8 处让角色偏离了竖直方向。" +
                            "这正是新导出在 Root 上多写 90 度旋转时的症状。");
                        Assert.That(
                            model.Scale.x, Is.EqualTo(1f).Within(0.01f),
                            $"{state}（{fbx}）在 {i}/8 处把整具骨架缩放成了 {model.Scale.x:F4}。");
                    }
                }
            }
        }

        /// <summary>首末帧姿态差超过这个角度，循环处就会看出跳帧。</summary>
        private const float SeamLimitDegrees = 5f;

        /// <summary>
        /// 已知不闭环的循环动画，以及当前实测缝隙的上限。
        ///
        /// `AM_Stand1_Action03`（idle）：2026-10-04 的新动画烘焙的头发模拟不是循环的。
        /// 实测发梢在 1.0 秒处偏离首帧 55.8 度，到片段末尾只回落到 41.9 度，
        /// 身体（裙摆）也差 12.3 度；逐帧扫过全部结束点，最接近的也有 30.9 度。
        /// Unity 的 Loop Pose（`loopPose`）对 Generic + NoAvatar 的片段实测完全无效
        /// （开与不开都是 41.93 度），所以引擎里没有无损的修法 —— 这是导出端问题。
        /// 对照：旧 idle 在同样长度下只有 0.17 度，说明原来的导出流程是对的。
        ///
        /// 这里登记当前值只做两件事：防止它进一步变坏，以及在重新导出修好之后提醒把它删掉。
        /// 详见 `NARAKA_待确认问题.md` 的 Q-026。
        /// </summary>
        private static readonly System.Collections.Generic.Dictionary<string, float> KnownBrokenLoops =
            new System.Collections.Generic.Dictionary<string, float>
            {
                { "AM_Stand1_Action03", 45f }
            };

        [Test]
        public void LoopingAnimationsActuallyLoopSeamlessly()
        {
            // `loopTime` 打开后 Unity 把时间取模，pose(length) 的下一帧就是 pose(0)，
            // 两者差多少就等于每个循环硬跳多少。
            //
            // 2026-10-04 的事故：新动画把 FBX 时间模式从 30fps 改成 60fps，而 `.meta`
            // 里的裁剪范围是按帧号存的，于是三个循环动画都只剩前半段 —— 走路播半个步幅
            // 就接回起点（实测 42.8 度），idle 的发梢每圈跳一次（52.3 度）。
            // 这就是用户报告的"走路、idle 卡顿"。
            using (var model = new SampledModel())
            {
                foreach (var fbx in P2AnimationSetup.LoopingFbx)
                {
                    var clip = P2AnimationSetup.LoadClip($"{P2AnimationSetup.AnimationRoot}/{fbx}.fbx");
                    Assert.That(clip, Is.Not.Null, $"找不到动画 {fbx}。");

                    var first = model.Capture(clip, 0f);
                    var last = model.Capture(clip, clip.length);
                    var oneFrameEarlier = model.Capture(clip, Mathf.Max(0f, clip.length - 1f / 60f));

                    var seam = model.MaxBoneAngle(first, last);
                    var step = model.MaxBoneAngle(oneFrameEarlier, last);

                    if (KnownBrokenLoops.TryGetValue(fbx, out var ceiling))
                    {
                        Assert.That(
                            seam, Is.LessThan(ceiling),
                            $"{fbx} 的首末帧姿态差 {seam:F2} 度，比已登记的 {ceiling:F0} 度还大，" +
                            "说明循环比记录的时候更坏了。");
                        Assert.That(
                            seam, Is.GreaterThan(SeamLimitDegrees),
                            $"{fbx} 的循环已经闭合（{seam:F2} 度），" +
                            "请把它从 KnownBrokenLoops 里删掉，让门槛收回严格值。");
                        continue;
                    }

                    Assert.That(
                        seam, Is.LessThan(SeamLimitDegrees),
                        $"{fbx} 的首末帧姿态差 {seam:F2} 度，循环处会跳一下。" +
                        $"（片段内正常的单帧步进是 {step:F2} 度。" +
                        "最常见的原因是 `.meta` 的裁剪范围按帧号写死、而片段换了帧率。）");
                }
            }
        }

        [Test]
        public void IdleVariationStartsBeforeTheIdleClipWouldLoopWhileThatLoopIsBroken()
        {
            // 新 idle 片段的循环不闭环（见 KnownBrokenLoops 与 Q-026），
            // 每播满一轮就在循环处硬跳一次。2026-10-04 的处理是把待机动作的触发延迟
            // 压到片段长度以内，让 Idle 永远播不到那个边界 —— 跳变因此看不到。
            //
            // 交叉淡入期间 Idle 还在继续推进（权重在降，但姿态还在算），
            // 所以约束是「延迟 + 淡入 < 片段长度」，而不是「延迟 < 片段长度」。
            //
            // 这条约束只在循环坏着的时候成立：idle 重新导出闭合、从 KnownBrokenLoops
            // 里删掉之后，延迟想调回 5 秒也没问题，这条断言会自动让路。
            if (!KnownBrokenLoops.ContainsKey("AM_Stand1_Action03"))
            {
                Assert.Pass("idle 的循环已经闭合，待机动作延迟不再受片段长度约束。");
            }

            var delay = LoadTuning().idleVariationDelaySeconds;
            var idleLength = ClipLength("AM_Stand1_Action03");
            var fade = CrossFadeSeconds();

            Assert.That(
                delay + fade, Is.LessThan(idleLength),
                $"待机动作延迟 {delay:F2}s ＋ 交叉淡入 {fade:F2}s 必须小于 " +
                $"Idle 片段长度 {idleLength:F3}s，否则会先看到 Idle 的循环跳变。");
        }

        /// <summary>玩家 Prefab 上 `PlayerAnimatorProjector` 的常规交叉淡入时长。</summary>
        private static float CrossFadeSeconds()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Settings/Player/Changli_Player.prefab");
            Assert.That(prefab, Is.Not.Null);

            var projector = prefab.GetComponentInChildren<PlayerAnimatorProjector>(true);
            Assert.That(projector, Is.Not.Null, "玩家 Prefab 上找不到动画投影组件。");

            var field = new SerializedObject(projector).FindProperty("crossFadeSeconds");
            Assert.That(field, Is.Not.Null, "动画投影组件上找不到 crossFadeSeconds。");
            return field.floatValue;
        }

        [Test]
        public void LoopingClipRangesCoverTheWholeTake()
        {
            // 上一条测的是结果，这条测的是原因，坏的时候两条一起失败、好定位。
            // 范围必须等于文件自带的整段 Take：这样换帧率也不会把循环截断。
            foreach (var fbx in P2AnimationSetup.LoopingFbx)
            {
                var path = $"{P2AnimationSetup.AnimationRoot}/{fbx}.fbx";
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                Assert.That(importer, Is.Not.Null, $"找不到导入器 {path}。");

                var stored = importer.clipAnimations;
                var natural = importer.defaultClipAnimations;
                Assert.That(stored, Is.Not.Empty, $"{fbx} 需要显式的循环设置。");
                Assert.That(natural, Is.Not.Empty, $"{fbx} 没有可用的 Take。");

                Assert.That(stored[0].loopTime, Is.True, $"{fbx} 必须循环。");
                Assert.That(
                    stored[0].firstFrame, Is.EqualTo(natural[0].firstFrame),
                    $"{fbx} 的起始帧偏离了文件自带的 Take。");
                Assert.That(
                    stored[0].lastFrame, Is.EqualTo(natural[0].lastFrame),
                    $"{fbx} 的结束帧是 {stored[0].lastFrame:F0}，而文件整段是 " +
                    $"{natural[0].lastFrame:F0}。按帧号写死的范围换帧率就会截断循环。");
            }
        }

        /// <summary>
        /// 把动画采样到正式角色模型上，用来测量真实姿态。
        /// 测的是"曲线作用在模型上之后是什么样"，而不是曲线里存了什么数字 ——
        /// 2026-10-04 的躺平事故恰恰是曲线自洽、但与模型的静止姿态不一致。
        /// </summary>
        private sealed class SampledModel : System.IDisposable
        {
            private const string ModelPath = "Assets/Game/Art/Characters/Changli/Changli_TPose.fbx";

            private readonly GameObject _instance;
            private readonly Transform[] _bones;
            private readonly Transform _root;

            public SampledModel()
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
                Assert.That(model, Is.Not.Null, $"找不到正式角色模型 {ModelPath}。");

                _instance = Object.Instantiate(model);
                _instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                _bones = _instance.GetComponentsInChildren<Transform>(true);

                _root = null;
                foreach (var bone in _bones)
                {
                    if (bone.name == "Root")
                    {
                        _root = bone;
                        break;
                    }
                }

                Assert.That(_root, Is.Not.Null, "正式模型里找不到名为 Root 的骨架根节点。");
                RestRotation = _root.localRotation;
                RestScale = _root.localScale;
            }

            public Quaternion RestRotation { get; }

            public Vector3 RestScale { get; }

            /// <summary>当前采样姿态下 `Root` 的缩放。</summary>
            public Vector3 Scale => _root.localScale;

            /// <summary>当前采样姿态下角色头顶方向的世界向量（`Root` 的局部 +Z）。</summary>
            public Vector3 CharacterUp => _root.forward;

            /// <summary>当前采样姿态下 `Root` 的局部位移。</summary>
            public Vector3 RootLocalPosition => _root.localPosition;

            public void Sample(AnimationClip clip, float time) => clip.SampleAnimation(_instance, time);

            public Quaternion[] Capture(AnimationClip clip, float time)
            {
                Sample(clip, time);
                var pose = new Quaternion[_bones.Length];
                for (var i = 0; i < _bones.Length; i++)
                {
                    pose[i] = _bones[i].localRotation;
                }

                return pose;
            }

            public float MaxBoneAngle(Quaternion[] a, Quaternion[] b)
            {
                var max = 0f;
                for (var i = 0; i < a.Length; i++)
                {
                    max = Mathf.Max(max, Quaternion.Angle(a[i], b[i]));
                }

                return max;
            }

            public void Dispose() => Object.DestroyImmediate(_instance);
        }

        /// <summary>片段里根节点（`Root`）的整体位移增量，三个轴都看。</summary>
        private static Vector3 RootTranslationDelta(string fbx)
        {
            var clip = P2AnimationSetup.LoadClip($"{P2AnimationSetup.AnimationRoot}/{fbx}.fbx");
            Assert.That(clip, Is.Not.Null, $"找不到动画 {fbx}。");

            var delta = Vector3.zero;
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (binding.path != "Root" ||
                    !binding.propertyName.StartsWith("m_LocalPosition", System.StringComparison.Ordinal))
                {
                    continue;
                }

                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                var value = curve.Evaluate(clip.length) - curve.Evaluate(0f);
                switch (binding.propertyName)
                {
                    case "m_LocalPosition.x":
                        delta.x = value;
                        break;
                    case "m_LocalPosition.y":
                        delta.y = value;
                        break;
                    case "m_LocalPosition.z":
                        delta.z = value;
                        break;
                }
            }

            return delta;
        }
    }
}
