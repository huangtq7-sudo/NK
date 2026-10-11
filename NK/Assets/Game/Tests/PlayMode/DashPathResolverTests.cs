using System.Collections;
using Naraka.Features.Character.View;
using Naraka.Features.Combat.Controller;
using Naraka.Features.Combat.Model;
using Naraka.Features.Combat.View;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Naraka.P2.PlayMode.Tests
{
    /// <summary>
    /// V 冲刺的路径校验。
    ///
    /// 每条测试在一个空场景里自建几何体，因此结论不依赖正式地图恰好长什么样。
    /// 角色胶囊与正式玩家一致：高 1.8、半径 0.32、坡度上限 50°。
    ///
    /// 规则：沿朝向一步一步"走"过去 —— 每一步都要有地、坡不超限、高差不超过一步能上下的量、
    /// 胶囊扫掠不撞东西、落脚处放得下 —— 任何一条不满足就停在上一步。于是撞墙停在墙前、
    /// 薄墙也扫得到、悬崖边不掉下去、紧贴墙时原地不动；而怪物不挡路（用户选择穿过怪物）。
    /// </summary>
    public sealed class DashPathResolverTests
    {
        private const float Distance = 17.838f;

        private Scene _scene;
        private Scene _previous;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _previous = SceneManager.GetActiveScene();
            _scene = SceneManager.CreateScene("DashPathResolverTests");
            SceneManager.SetActiveScene(_scene);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_previous.IsValid())
            {
                SceneManager.SetActiveScene(_previous);
            }

            yield return SceneManager.UnloadSceneAsync(_scene);
        }

        [UnityTest]
        public IEnumerator OpenGroundLandsAtTheFullDistanceOnTheGround()
        {
            Ground(new Vector3(0f, -0.5f, 0f), new Vector3(200f, 1f, 200f));
            var body = Body(Vector3.zero);
            yield return null;

            var destination = Resolve(body);

            Assert.That(Horizontal(destination), Is.EqualTo(Distance).Within(0.01f));
            Assert.That(destination.y, Is.EqualTo(0f).Within(0.1f), "必须贴地，不悬空也不陷进去。");
            AssertFits(body, destination);
        }

        [UnityTest]
        public IEnumerator AWallStopsTheTeleportInFrontOfIt()
        {
            Ground(new Vector3(0f, -0.5f, 0f), new Vector3(200f, 1f, 200f));
            Ground(new Vector3(0f, 2.5f, 8.5f), new Vector3(20f, 5f, 1f));
            var body = Body(Vector3.zero);
            yield return null;

            var destination = Resolve(body);

            Assert.That(destination.z, Is.LessThan(8f - 0.32f), "不得穿墙。");
            Assert.That(destination.z, Is.GreaterThan(8f - 0.32f - 0.6f), "应停在离墙最近的合法点，而不是提前放弃。");
            AssertFits(body, destination);
        }

        [UnityTest]
        public IEnumerator AThinWallIsNotTunnelledThrough()
        {
            // 5 厘米厚，远小于 0.5 米的步长：只采样落点会漏掉它，必须在两步之间扫掠。
            Ground(new Vector3(0f, -0.5f, 0f), new Vector3(200f, 1f, 200f));
            Ground(new Vector3(0f, 2.5f, 8.025f), new Vector3(20f, 5f, 0.05f));
            var body = Body(Vector3.zero);
            yield return null;

            var destination = Resolve(body);

            Assert.That(destination.z, Is.LessThan(8f), "薄墙也不得穿过。");
        }

        [UnityTest]
        public IEnumerator ACliffEdgeIsNotWalkedOff()
        {
            // 地面只铺到 z = 10，再往前是 20 米深的落差。
            Ground(new Vector3(0f, -0.5f, 2.5f), new Vector3(20f, 1f, 15f));
            Ground(new Vector3(0f, -20.5f, 30f), new Vector3(20f, 1f, 40f));
            var body = Body(Vector3.zero);
            yield return null;

            var destination = Resolve(body);

            Assert.That(destination.z, Is.LessThanOrEqualTo(10f), "不得越过悬崖边。");
            Assert.That(destination.z, Is.GreaterThan(9f), "应退到悬崖边最远的合法点。");
            Assert.That(destination.y, Is.EqualTo(0f).Within(0.1f), "必须留在上面的地面上。");
        }

        [UnityTest]
        public IEnumerator NoValidPointLeavesThePlayerWhereTheyStand()
        {
            Ground(new Vector3(0f, -0.5f, 0f), new Vector3(200f, 1f, 200f));
            Ground(new Vector3(0f, 2.5f, 0.9f), new Vector3(20f, 5f, 1f));
            var body = Body(Vector3.zero);
            yield return null;

            var destination = Resolve(body);

            Assert.That(destination, Is.EqualTo(body.transform.position), "全程没有合法点时原地不动。");
        }

        [UnityTest]
        public IEnumerator AWalkableRampIsFollowed()
        {
            Ground(new Vector3(0f, -0.5f, 0f), new Vector3(200f, 1f, 200f));
            var ramp = Ground(new Vector3(0f, 0f, 12f), new Vector3(10f, 0.2f, 16f));
            ramp.transform.rotation = Quaternion.Euler(-20f, 0f, 0f);
            var body = Body(Vector3.zero);
            yield return null;

            var destination = Resolve(body);

            Assert.That(Horizontal(destination), Is.EqualTo(Distance).Within(0.01f), "可走的坡不应挡住瞬移。");
            Assert.That(destination.y, Is.GreaterThan(0.5f), "落点应在坡面上，而不是坡下面。");
            AssertFits(body, destination);
        }

        [UnityTest]
        public IEnumerator ASlopeSteeperThanTheLimitBlocks()
        {
            Ground(new Vector3(0f, -0.5f, 0f), new Vector3(200f, 1f, 200f));
            var slope = Ground(new Vector3(0f, 0f, 9f), new Vector3(10f, 0.2f, 12f));
            slope.transform.rotation = Quaternion.Euler(-70f, 0f, 0f);
            var body = Body(Vector3.zero);
            yield return null;

            var destination = Resolve(body);

            Assert.That(destination.z, Is.LessThan(9f), "超过 50° 的坡不得被翻越。");
            Assert.That(destination.y, Is.EqualTo(0f).Within(0.1f));
        }

        [UnityTest]
        public IEnumerator MonstersDoNotBlockTheTeleport()
        {
            Ground(new Vector3(0f, -0.5f, 0f), new Vector3(200f, 1f, 200f));
            var monster = new GameObject("Monster");
            SceneManager.MoveGameObjectToScene(monster, _scene);
            monster.transform.position = new Vector3(0f, 1f, 8f);
            var collider = monster.AddComponent<CapsuleCollider>();
            collider.height = 2f;
            collider.radius = 1f;
            monster.AddComponent<StubDamageTaker>();
            var body = Body(Vector3.zero);
            yield return null;

            var destination = Resolve(body);

            Assert.That(Horizontal(destination), Is.EqualTo(Distance).Within(0.01f), "用户选择穿过怪物。");
        }

        private Vector3 Resolve(CharacterController body)
        {
            Physics.SyncTransforms();
            return DashPathResolver.Resolve(
                body,
                Vector3.forward,
                Distance,
                DashPathResolver.ObstacleMaskFor(body.gameObject.layer));
        }

        private static float Horizontal(Vector3 destination) =>
            new Vector2(destination.x, destination.z).magnitude;

        private static void AssertFits(CharacterController body, Vector3 destination)
        {
            Assert.That(
                DashPathResolver.Fits(
                    body, destination, DashPathResolver.ObstacleMaskFor(body.gameObject.layer)),
                Is.True,
                "落点不得卡进任何东西。");
        }

        private GameObject Ground(Vector3 center, Vector3 size)
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            SceneManager.MoveGameObjectToScene(ground, _scene);
            ground.transform.position = center;
            ground.transform.localScale = size;
            return ground;
        }

        private CharacterController Body(Vector3 feet)
        {
            var player = new GameObject("Player");
            SceneManager.MoveGameObjectToScene(player, _scene);
            var layer = LayerMask.NameToLayer("Player");
            player.layer = layer >= 0 ? layer : 0;
            player.transform.position = feet;
            var controller = player.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = 0.32f;
            controller.center = new Vector3(0f, 0.9f, 0f);
            controller.slopeLimit = 50f;
            controller.stepOffset = 0.3f;
            return controller;
        }
    }

    /// <summary>代表怪物的最小可受击对象：解析器据此认出"怪物不挡路"。</summary>
    internal sealed class StubDamageTaker : MonoBehaviour, IDamageTaker
    {
        public int TargetId => GetInstanceID();

        public Faction Faction => Faction.Enemy;

        public bool IsAlive => true;

        public DamageApplication TakeDamage(in HitRequest request) => default;
    }
}
