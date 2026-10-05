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

            var wolves = Object.FindObjectsOfType<GrayboxWolfView>();
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

            var wolf = Object.FindObjectOfType<GrayboxWolfView>();
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
            var wolf = Object.FindObjectOfType<GrayboxWolfView>();
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
            var wolf = Object.FindObjectOfType<GrayboxWolfView>();
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
            var wolf = Object.FindObjectOfType<GrayboxWolfView>();
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
            var wolf = Object.FindObjectOfType<GrayboxWolfView>();
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
            var wolf = Object.FindObjectOfType<GrayboxWolfView>();
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

            var target = Object.FindObjectOfType<CounterTrainingTarget>();

            Assert.That(target, Is.Not.Null, "反击训练靶必须存在，否则反击成功路径无法人工验证。");
            Assert.That(
                target.name,
                Does.Contain("Training"),
                "开发测试对象必须在名字上标明自己是测试对象。");
        }

        [UnityTest]
        public IEnumerator ReEnteringMap02DoesNotAccumulateDuplicates()
        {
            yield return EnterMap02();
            yield return EnterMap02();

            Assert.That(Object.FindObjectsOfType<AppRootLifetimeScope>().Length, Is.EqualTo(1));
            Assert.That(Object.FindObjectsOfType<WorldSceneLifetimeScope>().Length, Is.EqualTo(1));
            Assert.That(Object.FindObjectsOfType<PlayerCharacterView>().Length, Is.EqualTo(1));
            Assert.That(Object.FindObjectsOfType<GrayboxWolfView>().Length, Is.EqualTo(1));
            Assert.That(Object.FindObjectsOfType<Camera>().Length, Is.EqualTo(1));
        }

        private static void Hit(
            IHitResolver resolver,
            PlayerCharacterView player,
            GrayboxWolfView wolf,
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
                Is.EqualTo(WorldMapIds.Map02CombatGraybox));

            yield return WaitUntil(
                () => Object.FindObjectOfType<PlayerCharacterView>() != null &&
                      Object.FindObjectOfType<GrayboxWolfView>() != null,
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
