using System.Linq;
using System.Collections;
using Cysharp.Threading.Tasks;
using Naraka.Boot;
using Naraka.Features.Character.Model;
using Naraka.Features.Character.View;
using Naraka.Features.Combat.Controller;
using Naraka.Features.Combat.Model;
using Naraka.Features.Combat.View;
using Naraka.Features.CombatHud.Controller;
using Naraka.Features.Monster.Model;
using Naraka.Features.Monster.View;
using Naraka.Features.World.Controller;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VContainer;
using Naraka.Core.Application.Scenes;

namespace Naraka.P2.PlayMode.Tests
{
    /// <summary>
    /// P2.2 战斗闭环的 PlayMode 验收：真实场景、真实怪物、真实命中链。
    ///
    /// 这些测试不碰输入设备：怪物 AI 与伤害结算都不依赖玩家按键，
    /// 直接驱动状态与命中层反而更稳定，也更容易定位失败原因。
    /// </summary>
    public sealed class P22CombatPlayModeTests
    {
        private const float SceneLoadTimeoutSeconds = 30f;

        [SetUp]
        public void SetUp() => DestroyPersistentRoot();

        [TearDown]
        public void TearDown() => DestroyPersistentRoot();

        private static void DestroyPersistentRoot()
        {
            foreach (var root in Object.FindObjectsOfType<AppRootLifetimeScope>())
            {
                Object.DestroyImmediate(root.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator Map02SpawnsExactlyOneGrayboxWolf()
        {
            yield return EnterMap02();

            var wolves = Object.FindObjectsOfType<DuskshadowWolfView>();
            Assert.That(wolves.Length, Is.EqualTo(1), "地图二只应该生成一只测试狼。");

            var spawner = Object.FindObjectOfType<MonsterSpawner>();
            Assert.That(spawner, Is.Not.Null, "地图二必须有怪物生成点。");
            Assert.That(spawner.SpawnCount, Is.EqualTo(1));
            Assert.That(spawner.AliveCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator TheWolfReadsItsNumbersFromTheGeneratedConfig()
        {
            yield return EnterMap02();

            var wolf = Object.FindObjectOfType<DuskshadowWolfView>();
            var tuning = wolf.Controller.Tuning;

            Assert.That(tuning.MonsterId, Is.EqualTo("monster_wolf_duskshadow"));
            Assert.That(tuning.DisplayName, Is.EqualTo("暮影妖狼"));
            Assert.That(tuning.SkillCount, Is.EqualTo(1), "配置里只有一个红色吐息。");
            Assert.That(tuning.Skill(0).ColorTag, Is.EqualTo(AttackColorTag.Red));
            Assert.That(tuning.Skill(0).Counterable, Is.False);
        }

        [UnityTest]
        public IEnumerator TheWolfPatrolsThenPerceivesChasesAndAttacks()
        {
            yield return EnterMap02();
            var wolf = Object.FindObjectOfType<DuskshadowWolfView>();
            var player = Object.FindObjectOfType<PlayerCharacterView>();
            yield return WaitUntil(() => player.State.Action == ActionState.None, 12f, "spawn-burst");

            // 先把玩家挪到休眠距离之外，确认它确实在巡逻。
            player.TeleportTo(
                wolf.transform.position + new Vector3(0f, 0f, 200f), Quaternion.identity);
            yield return WaitUntil(
                () => wolf.Controller.Current.Intent == MonsterIntent.Dormant, 6f, "dormant");

            // 走进感知圈：应当依次出现感知与追击。
            player.TeleportTo(
                wolf.transform.position + new Vector3(0f, 0f, 10f), Quaternion.identity);
            yield return WaitUntil(
                () => wolf.Controller.Current.Intent == MonsterIntent.Chase ||
                      wolf.Controller.Current.Intent == MonsterIntent.Perceive,
                6f, "perceive-or-chase");

            // 贴身：应当打出普通攻击。
            player.TeleportTo(
                wolf.transform.position + new Vector3(0f, 0f, 2f), Quaternion.identity);
            yield return WaitUntil(
                () => wolf.Controller.Current.Action == MonsterActionState.Attack, 8f, "attack");

            Assert.That(wolf.Controller.Current.Action, Is.EqualTo(MonsterActionState.Attack));
        }

        [UnityTest]
        public IEnumerator TheWolfDisengagesWhenThePlayerLeaves()
        {
            yield return EnterMap02();
            var wolf = Object.FindObjectOfType<DuskshadowWolfView>();
            var player = Object.FindObjectOfType<PlayerCharacterView>();
            yield return WaitUntil(() => player.State.Action == ActionState.None, 12f, "spawn-burst");

            player.TeleportTo(
                wolf.transform.position + new Vector3(0f, 0f, 6f), Quaternion.identity);
            yield return WaitUntil(
                () => wolf.Controller.Current.Intent == MonsterIntent.Chase ||
                      wolf.Controller.Current.Intent == MonsterIntent.NormalAttack ||
                      wolf.Controller.Current.Action == MonsterActionState.Attack,
                8f, "engage");

            player.TeleportTo(
                wolf.transform.position + new Vector3(0f, 0f, 200f), Quaternion.identity);
            yield return WaitUntil(
                () => wolf.Controller.Current.Intent == MonsterIntent.Recover ||
                      wolf.Controller.Current.Intent == MonsterIntent.Dormant ||
                      wolf.Controller.Current.Intent == MonsterIntent.Patrol,
                10f, "disengage");

            Assert.That(
                wolf.Controller.Current.Intent,
                Is.Not.EqualTo(MonsterIntent.Chase),
                "玩家离开之后必须脱战或休眠。");
        }

        [UnityTest]
        public IEnumerator ThePlayerCanDamageAndKillTheWolf()
        {
            yield return EnterMap02();
            var wolf = Object.FindObjectOfType<DuskshadowWolfView>();
            var player = Object.FindObjectOfType<PlayerCharacterView>();
            var resolver = Resolve<IHitResolver>();

            var before = wolf.Controller.Current.Health;
            var combo = player.Tuning.FinalAttack * player.Tuning.Combo.Step1.SkillMultiplier;
            Hit(resolver, player, wolf, combo, sequence: 1);
            yield return null;

            Assert.That(wolf.Controller.Current.Health + wolf.Controller.Current.Armor,
                Is.LessThan(before + wolf.Controller.Tuning.MaxArmor),
                "玩家攻击必须真的打掉怪物的护甲或生命。");

            for (var i = 2; i < 20 && !wolf.Controller.IsDead; i++)
            {
                Hit(resolver, player, wolf, combo * 3f, sequence: i);
                yield return null;
            }

            Assert.That(wolf.Controller.IsDead, Is.True, "玩家必须能杀死暮影妖狼。");
        }

        [UnityTest]
        public IEnumerator TheWolfCanRemoveThePlayersArmorAndHealth()
        {
            yield return EnterMap02();
            var player = Object.FindObjectOfType<PlayerCharacterView>();
            yield return WaitUntil(() => player.State.Action == ActionState.None, 12f, "spawn-burst");
            // 重生保护期间直接免伤，必须先等它过去。
            yield return WaitUntil(() => !player.State.HasSpawnProtection, 8f, "spawn-protection");

            var armorBefore = player.State.Armor;
            var request = new HitRequest(
                new HitId(9001, 1), Faction.Enemy, player.TargetId, Faction.Player, 200f);
            player.TakeDamage(in request);
            yield return null;

            Assert.That(player.State.Armor, Is.LessThan(armorBefore), "怪物攻击必须扣玩家护甲。");

            var healthBefore = player.State.Health;
            var heavy = new HitRequest(
                new HitId(9001, 2), Faction.Enemy, player.TargetId, Faction.Player, 5000f);
            player.TakeDamage(in heavy);
            yield return null;

            Assert.That(player.State.Health, Is.LessThan(healthBefore), "护甲不足时必须扣生命。");
        }

        [UnityTest]
        public IEnumerator TheRedBreathOnlyAppearsBelowHalfHealthAndWarnsFirst()
        {
            yield return EnterMap02();
            var wolf = Object.FindObjectOfType<DuskshadowWolfView>();
            var player = Object.FindObjectOfType<PlayerCharacterView>();
            yield return WaitUntil(() => player.State.Action == ActionState.None, 12f, "spawn-burst");

            player.TeleportTo(
                wolf.transform.position + new Vector3(0f, 0f, 6f), Quaternion.identity);
            yield return WaitUntil(
                () => wolf.Controller.Current.Intent != MonsterIntent.Patrol, 8f, "engage");

            // 满血阶段不得出现吐息。
            var elapsed = 0f;
            while (elapsed < 2f)
            {
                Assert.That(
                    wolf.Controller.Current.Action,
                    Is.Not.EqualTo(MonsterActionState.Skill),
                    "生命高于一半时不得释放吐息。");
                elapsed += Time.deltaTime;
                yield return null;
            }

            // 打到半血以下。
            var tuning = wolf.Controller.Tuning;
            var raw = (tuning.MaxArmor + (tuning.MaxHealth * 0.55f)) * (100f + tuning.Defense) / 100f;
            wolf.Controller.ApplyRawDamage(raw);
            Assert.That(wolf.Controller.Current.Phase, Is.EqualTo(MonsterPhase.Enraged));

            // 预警必须先于伤害窗口。
            yield return WaitUntil(
                () => wolf.Controller.Current.IsWarningActive, 10f, "warning");
            Assert.That(
                wolf.Controller.Current.WarningColorTag,
                Is.EqualTo(AttackColorTag.Red),
                "吐息的预警是红色的。");

            var warningSeen = false;
            var hitSeen = false;
            var deadline = Time.realtimeSinceStartup + 6f;
            while (Time.realtimeSinceStartup < deadline && !hitSeen)
            {
                var state = wolf.Controller.Current;
                if (state.IsWarningActive)
                {
                    warningSeen = true;
                }

                if (state.Action == MonsterActionState.Skill && !state.IsWarningActive && warningSeen)
                {
                    hitSeen = true;
                }

                yield return null;
            }

            Assert.That(warningSeen, Is.True, "红色预警必须出现。");
            Assert.That(hitSeen, Is.True, "预警之后才进入伤害窗口。");
        }

        [UnityTest]
        public IEnumerator TheHudStateTracksPlayerAndTarget()
        {
            yield return EnterMap02();
            var wolf = Object.FindObjectOfType<DuskshadowWolfView>();
            var player = Object.FindObjectOfType<PlayerCharacterView>();
            var hud = Resolve<ICombatHudController>();

            hud.Refresh();
            Assert.That(
                hud.Current.PlayerMaxHealth,
                Is.EqualTo(player.State.MaxHealth),
                "HUD 的玩家生命上限必须来自玩家状态。");
            Assert.That(hud.Current.PlayerMaxStamina, Is.EqualTo(20f), "体力上限为 20。");

            var feedback = 0;
            hud.MonsterDamaged += _ => feedback++;

            var resolver = Resolve<IHitResolver>();
            Hit(resolver, player, wolf, 400f, sequence: 77);
            yield return null;
            hud.Refresh();

            Assert.That(feedback, Is.GreaterThan(0), "怪物受击反馈事件必须接上。");
            Assert.That(hud.Current.HasTarget, Is.True, "挨打之后 HUD 必须显示这只怪。");
            Assert.That(hud.Current.TargetName, Is.EqualTo("暮影妖狼"));
            Assert.That(
                hud.Current.TargetHealthRatio + hud.Current.TargetArmorRatio,
                Is.LessThan(2f),
                "目标血条必须反映实际损失。");
        }

        [UnityTest]
        public IEnumerator TheCounterTrainingTargetIsPresentAndClearlyMarked()
        {
            yield return EnterMap02();

            // 包含停用对象：P2.3 把训练靶收进了默认停用的 DevOnly 节点下，
            // 因此 FindObjectOfType 看不到它。要断言的两件事没变：它必须存在，
            // 而且必须在名字上标明自己是测试对象。
            var target = Object.FindObjectsOfType<CounterTrainingTarget>(true)
                .FirstOrDefault();

            Assert.That(target, Is.Not.Null, "反击训练靶必须存在，否则反击成功路径无法人工验证。");
            Assert.That(
                target.name,
                Does.Contain("Training"),
                "开发测试对象必须在名字上标明自己是测试对象。");

            // 新增的保证：它默认不影响正式流程。停用的对象不渲染、不跑 Update，
            // 也不会被战斗登记表找到，因此玩家在正式环境里不会撞上三个灰色假人。
            Assert.That(
                target.gameObject.activeInHierarchy, Is.False,
                "训练靶是开发用对象，必须默认停用。");
        }

        [UnityTest]
        public IEnumerator ReEnteringMap02DoesNotAccumulateDuplicates()
        {
            yield return EnterMap02();
            yield return EnterMap02();

            Assert.That(Object.FindObjectsOfType<AppRootLifetimeScope>().Length, Is.EqualTo(1));
            Assert.That(Object.FindObjectsOfType<WorldSceneLifetimeScope>().Length, Is.EqualTo(1));
            Assert.That(Object.FindObjectsOfType<PlayerCharacterView>().Length, Is.EqualTo(1));
            Assert.That(Object.FindObjectsOfType<DuskshadowWolfView>().Length, Is.EqualTo(1));
            Assert.That(Object.FindObjectsOfType<Camera>().Length, Is.EqualTo(1));
        }

        // ------------------------------------------------------------ 正式模型与动画投影
        //
        // 2026-10-05 正式暮影妖狼接入。下面这组断言要回答的是同一个问题：
        // 换了美术之后，**业务状态仍然是唯一真相，Animator 只是跟着走**。
        // 因此每一条都同时看两边：HFSM 的当前动作，以及投影到 Animator 的动画。

        [UnityTest]
        public IEnumerator TheSpawnedWolfUsesTheOfficialModelAndHasNoCubeBody()
        {
            yield return EnterMap02();
            var wolf = Object.FindObjectOfType<DuskshadowWolfView>();

            var skinned = wolf.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Assert.That(
                skinned.Length, Is.GreaterThan(0),
                "场上的狼必须是带蒙皮网格的正式模型，而不是方块替身。");

            foreach (var filter in wolf.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null || !filter.gameObject.activeInHierarchy)
                {
                    continue;
                }

                Assert.That(
                    filter.sharedMesh.name, Is.Not.EqualTo("Cube"),
                    $"场上的狼还带着灰盒方块本体（{filter.gameObject.name}）。");
            }

            Assert.That(
                wolf.BodyRendererCount, Is.GreaterThan(0),
                "颜色反馈必须绑到身体 Renderer 上。");

            var animator = wolf.GetComponentInChildren<Animator>(true);
            Assert.That(animator, Is.Not.Null, "正式模型必须带 Animator。");
            Assert.That(
                animator.applyRootMotion, Is.False,
                "Root Motion 必须关闭，否则动画会和 NavMeshAgent 一起推坐标。");
        }

        [UnityTest]
        public IEnumerator PatrollingPlaysTheWalkAnimation()
        {
            yield return EnterMap02();
            var wolf = Object.FindObjectOfType<DuskshadowWolfView>();
            var player = Object.FindObjectOfType<PlayerCharacterView>();
            yield return WaitUntil(() => player.State.Action == ActionState.None, 12f, "spawn-burst");

            // 把玩家挪远一点但不到休眠距离，让狼安心巡逻。
            player.TeleportTo(
                wolf.transform.position + new Vector3(0f, 0f, 30f), Quaternion.identity);

            yield return WaitUntil(
                () => wolf.Controller.Current.Intent == MonsterIntent.Patrol &&
                      wolf.Controller.Current.Action == MonsterActionState.Move,
                10f, "patrol-move");

            Assert.That(
                wolf.CurrentAnimation, Is.EqualTo(MonsterAnimation.Walk),
                "巡逻移动时应当播放 Walk。");
            yield return WaitForAnimatorState(wolf, MonsterAnimatorProjector.StateNames.Walk);
        }

        [UnityTest]
        public IEnumerator ChasingPlaysTheRunAnimation()
        {
            yield return EnterMap02();
            var wolf = Object.FindObjectOfType<DuskshadowWolfView>();
            var player = Object.FindObjectOfType<PlayerCharacterView>();
            yield return WaitUntil(() => player.State.Action == ActionState.None, 12f, "spawn-burst");

            // 进感知圈但不进攻击距离：这样它会一直追而不是立刻出手。
            player.TeleportTo(
                wolf.transform.position + new Vector3(0f, 0f, 10f), Quaternion.identity);

            yield return WaitUntil(
                () => wolf.Controller.Current.Intent == MonsterIntent.Chase, 10f, "chase");

            Assert.That(
                wolf.CurrentAnimation, Is.EqualTo(MonsterAnimation.Run),
                "追击时应当播放 Run。");
            yield return WaitForAnimatorState(wolf, MonsterAnimatorProjector.StateNames.Run);
        }

        [UnityTest]
        public IEnumerator AttackingPlaysTheBiteAnimation()
        {
            yield return EnterMap02();
            var wolf = Object.FindObjectOfType<DuskshadowWolfView>();
            var player = Object.FindObjectOfType<PlayerCharacterView>();
            yield return WaitUntil(() => player.State.Action == ActionState.None, 12f, "spawn-burst");

            player.TeleportTo(
                wolf.transform.position + new Vector3(0f, 0f, 2f), Quaternion.identity);
            yield return WaitUntil(
                () => wolf.Controller.Current.Action == MonsterActionState.Attack, 10f, "attack");

            Assert.That(
                wolf.CurrentAnimation, Is.EqualTo(MonsterAnimation.Attack),
                "普通攻击时应当播放咬击（Attack State 绑的是 @Bite Attack）。");
            yield return WaitForAnimatorState(wolf, MonsterAnimatorProjector.StateNames.Attack);
        }

        [UnityTest]
        public IEnumerator TheRedBreathPlaysTheBreathAnimationAfterItsWarning()
        {
            yield return EnterMap02();
            var wolf = Object.FindObjectOfType<DuskshadowWolfView>();
            var player = Object.FindObjectOfType<PlayerCharacterView>();
            yield return WaitUntil(() => player.State.Action == ActionState.None, 12f, "spawn-burst");

            player.TeleportTo(
                wolf.transform.position + new Vector3(0f, 0f, 6f), Quaternion.identity);
            yield return WaitUntil(
                () => wolf.Controller.Current.Intent != MonsterIntent.Patrol, 10f, "engage");

            // 打到半血以下解锁阶段技能。数值来自配置，不是写死的。
            var tuning = wolf.Controller.Tuning;
            var raw = (tuning.MaxArmor + (tuning.MaxHealth * 0.55f)) * (100f + tuning.Defense) / 100f;
            wolf.Controller.ApplyRawDamage(raw);
            Assert.That(wolf.Controller.Current.Phase, Is.EqualTo(MonsterPhase.Enraged));

            // 预警必须先出现，然后才是吐息的动画与伤害窗口。
            yield return WaitUntil(() => wolf.Controller.Current.IsWarningActive, 12f, "warning");
            Assert.That(
                wolf.Controller.Current.WarningColorTag, Is.EqualTo(AttackColorTag.Red),
                "吐息的预警是红色的。");
            Assert.That(
                wolf.CurrentAnimation, Is.EqualTo(MonsterAnimation.Skill),
                "预警期间就已经在播吐息动画（前摇属于技能动作本身）。");
            yield return WaitForAnimatorState(wolf, MonsterAnimatorProjector.StateNames.Skill);

            var warningEnded = false;
            var deadline = Time.realtimeSinceStartup + 6f;
            while (Time.realtimeSinceStartup < deadline && !warningEnded)
            {
                var state = wolf.Controller.Current;
                if (state.Action == MonsterActionState.Skill && !state.IsWarningActive)
                {
                    warningEnded = true;
                    Assert.That(
                        wolf.CurrentAnimation, Is.EqualTo(MonsterAnimation.Skill),
                        "进入伤害窗口后仍然是同一段吐息动画。");
                }

                yield return null;
            }

            Assert.That(warningEnded, Is.True, "预警之后必须进入伤害窗口。");
        }

        [UnityTest]
        public IEnumerator TakingDamagePlaysTheTakeDamageAnimation()
        {
            yield return EnterMap02();
            var wolf = Object.FindObjectOfType<DuskshadowWolfView>();
            var player = Object.FindObjectOfType<PlayerCharacterView>();
            var resolver = Resolve<IHitResolver>();
            yield return WaitUntil(() => player.State.Action == ActionState.None, 12f, "spawn-burst");

            // 巡逻中的狼没有霸体，因此会进硬直。
            Hit(resolver, player, wolf, 120f, sequence: 4101);
            yield return null;

            Assert.That(
                wolf.Controller.Current.Action, Is.EqualTo(MonsterActionState.HitStun),
                "没有霸体时受击必须进硬直。");
            Assert.That(
                wolf.CurrentAnimation, Is.EqualTo(MonsterAnimation.HitStun),
                "受击时应当播放 Take Damage。");
            yield return WaitForAnimatorState(wolf, MonsterAnimatorProjector.StateNames.HitStun);
        }

        [UnityTest]
        public IEnumerator DyingPlaysTheDieAnimation()
        {
            yield return EnterMap02();
            var wolf = Object.FindObjectOfType<DuskshadowWolfView>();
            var player = Object.FindObjectOfType<PlayerCharacterView>();
            var resolver = Resolve<IHitResolver>();
            yield return WaitUntil(() => player.State.Action == ActionState.None, 12f, "spawn-burst");

            var tuning = wolf.Controller.Tuning;
            var lethal = (tuning.MaxArmor + tuning.MaxHealth) * (100f + tuning.Defense) / 100f * 2f;
            Hit(resolver, player, wolf, lethal, sequence: 4102);
            yield return null;

            Assert.That(wolf.Controller.IsDead, Is.True);
            Assert.That(
                wolf.Controller.Current.Action, Is.EqualTo(MonsterActionState.Death),
                "死亡是动作层的终态。");
            Assert.That(
                wolf.CurrentAnimation, Is.EqualTo(MonsterAnimation.Death),
                "死亡时应当播放 Die。");
            yield return WaitForAnimatorState(wolf, MonsterAnimatorProjector.StateNames.Death);
        }

        [UnityTest]
        public IEnumerator TheAnimationProjectionDoesNotChangeTheHfsmOutput()
        {
            yield return EnterMap02();
            var wolf = Object.FindObjectOfType<DuskshadowWolfView>();
            var player = Object.FindObjectOfType<PlayerCharacterView>();
            yield return WaitUntil(() => player.State.Action == ActionState.None, 12f, "spawn-burst");

            // 休眠是一个稳定态：动作不会每帧变，因此可以干净地比较前后。
            player.TeleportTo(
                wolf.transform.position + new Vector3(0f, 0f, 200f), Quaternion.identity);
            yield return WaitUntil(
                () => wolf.Controller.Current.Intent == MonsterIntent.Dormant, 10f, "dormant");

            var projector = wolf.GetComponentInChildren<MonsterAnimatorProjector>(true);
            Assert.That(projector, Is.Not.Null);

            var before = wolf.Controller.Current;

            // 强行投一个与业务状态无关的动画。如果 Animator 能回写业务状态，
            // 这一下就会把 HFSM 带歪 —— 投影是单向的，所以它不会。
            projector.Apply(MonsterAnimation.Death, restart: true);
            Assert.That(projector.CurrentAnimation, Is.EqualTo(MonsterAnimation.Death));

            var after = wolf.Controller.Current;
            Assert.That(after.Action, Is.EqualTo(before.Action), "投影不得改变当前动作。");
            Assert.That(after.Intent, Is.EqualTo(before.Intent), "投影不得改变行为树意图。");
            Assert.That(after.IsAlive, Is.True, "投了死亡动画不等于真的死了。");

            // 下一帧投影会按业务状态自己纠回来，不需要任何人去复位。
            yield return null;
            Assert.That(
                wolf.CurrentAnimation, Is.EqualTo(MonsterAnimation.Idle),
                "下一帧应当回到业务状态要求的动画。");
        }

        [UnityTest]
        public IEnumerator TheWolfStopsShortInsteadOfPushingThePlayer()
        {
            // 2026-10-05 用户报告："怪物攻击角色后、且处于攻击范围内，
            // 怪物会处于追击状态推着角色移动，而不是在原地攻击角色。"
            //
            // 根因不在碰撞体：普攻冷却是 2 秒，冷却期间行为树的 NormalAttack 分支不成立
            // （它同时要求"冷却结束"与"在攻击距离内"），于是落到 Chase；
            // 而 Chase 的目标是玩家的坐标本身，原来的到达判定 0.6 比两边胶囊半径之和
            // （≈0.65）还小，所以永远判定不出"到达"，狼一直往玩家身上顶。
            yield return EnterMap02();
            var wolf = Object.FindObjectOfType<DuskshadowWolfView>();
            var player = Object.FindObjectOfType<PlayerCharacterView>();
            yield return WaitUntil(() => player.State.Action == ActionState.None, 12f, "spawn-burst");
            yield return WaitUntil(() => !player.State.HasSpawnProtection, 8f, "spawn-protection");

            var tuning = wolf.Controller.Tuning;
            Assert.That(
                wolf.ChaseStopDistance, Is.LessThan(tuning.AttackRange),
                "停止距离必须落在攻击距离之内，否则冷却一结束狼还得再往前挪一步才能出手。");
            Assert.That(
                wolf.ChaseStopDistance, Is.GreaterThan(1.5f),
                "停止距离必须远离接触，否则推人的现象还在。");

            // 放在攻击距离之内，让它立刻出手；接下来要完整跨过一次普攻冷却。
            player.TeleportTo(
                wolf.transform.position + new Vector3(0f, 0f, tuning.AttackRange - 0.4f),
                Quaternion.identity);
            yield return WaitUntil(
                () => wolf.Controller.Current.Action == MonsterActionState.Attack, 10f, "first-attack");

            var playerStart = player.transform.position;
            var closest = float.MaxValue;
            var sawChase = false;

            // 冷却 2 秒、普攻动作 1.3 秒，因此观察 5 秒足以跨过
            // "攻击 → 冷却期追击 → 再攻击"一整圈。
            var deadline = Time.realtimeSinceStartup + 5f;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (wolf.Controller.Current.Intent == MonsterIntent.Chase)
                {
                    sawChase = true;
                }

                closest = Mathf.Min(
                    closest, Flat(wolf.transform.position, player.transform.position));
                yield return null;
            }

            Assert.That(
                sawChase, Is.True,
                "普攻冷却期间行为树确实会选追击 —— 这正是原来推人的那一段，" +
                "如果这里没看到，说明这个测试没有覆盖到目标场景。");
            Assert.That(
                Flat(player.transform.position, playerStart), Is.LessThan(0.5f),
                "怪物不得推着玩家移动。");
            Assert.That(
                closest, Is.GreaterThan(1.2f),
                $"怪物最近贴到了 {closest:F2}，仍然在往玩家身上顶。");
        }

        /// <summary>水平距离。竖直方向由重力负责，不参与"有没有被推开"的判断。</summary>
        private static float Flat(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        /// <summary>
        /// 等到 Animator 真的进入这个 State。
        ///
        /// 要等而不是立即断言：<c>CrossFadeInFixedTime</c> 只是把切换排进队列，
        /// 要到下一次 Animator 求值才生效。契约是"投影会到达 Animator"，
        /// 不是"同一帧就到达" —— 立即断言会在刚切换的那一帧必然失败
        /// （而已经播了很多帧的状态又会侥幸通过，于是测试结果取决于时机）。
        ///
        /// 交叉淡入期间"当前 State"还是旧的、"下一个 State"才是新的，因此两边都要看。
        /// </summary>
        private static IEnumerator WaitForAnimatorState(
            DuskshadowWolfView wolf, string stateName, float timeoutSeconds = 2f)
        {
            var animator = wolf.GetComponentInChildren<Animator>(true);
            Assert.That(animator, Is.Not.Null);

            var hash = Animator.StringToHash(stateName);
            var deadline = Time.realtimeSinceStartup + timeoutSeconds;
            var matched = false;
            while (!matched && Time.realtimeSinceStartup < deadline)
            {
                matched = animator.GetCurrentAnimatorStateInfo(0).shortNameHash == hash ||
                          (animator.IsInTransition(0) &&
                           animator.GetNextAnimatorStateInfo(0).shortNameHash == hash);
                if (matched)
                {
                    break;
                }

                yield return null;
            }

            Assert.That(
                matched, Is.True,
                $"Animator 在 {timeoutSeconds} 秒内没有进入 State「{stateName}」" +
                "（投影没有到达 Animator）。");
        }

        private static void Hit(
            IHitResolver resolver,
            PlayerCharacterView player,
            DuskshadowWolfView wolf,
            float rawDamage,
            int sequence)
        {
            var request = new HitRequest(
                new HitId(player.TargetId, sequence),
                Faction.Player,
                wolf.TargetId,
                Faction.Enemy,
                rawDamage);
            resolver.Resolve(in request, wolf.TakeDamage);
            resolver.ReleaseAttack(request.HitId);
        }

        private static T Resolve<T>()
        {
            var root = AppRootLifetimeScope.Instance;
            Assert.That(root, Is.Not.Null, "持久化根不存在。");
            return root.Container.Resolve<T>();
        }

        private IEnumerator EnterMap02()
        {
            SceneManager.LoadScene("SampleScene", LoadSceneMode.Single);
            yield return null;
            yield return null;
            Assert.That(AppRootLifetimeScope.Instance, Is.Not.Null, "Bootstrap 场景缺少持久化 App Root。");
            yield return null;

            var world = Resolve<IWorldFlowController>();
            var task = world.EnterMap02Async(default).Preserve();
            var deadline = Time.realtimeSinceStartup + SceneLoadTimeoutSeconds;
            while (!task.Status.IsCompleted() && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(task.Status.IsCompleted(), Is.True, "进入地图二没有在超时前完成。");
            Assert.That(
                SceneManager.GetActiveScene().name,
                Is.EqualTo(WorldSceneNames.Map02Combat));

            yield return WaitUntil(
                () => Object.FindObjectOfType<PlayerCharacterView>() != null &&
                      Object.FindObjectOfType<DuskshadowWolfView>() != null,
                10f, "map02-actors");
        }

        private static IEnumerator WaitUntil(
            System.Func<bool> condition, float timeoutSeconds, string label = null)
        {
            var deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (!condition() && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(
                condition(), Is.True,
                $"等待条件在 {timeoutSeconds} 秒内没有满足（{label ?? "未命名"}）。");
        }
    }
}
