using System.Collections;
using System.Linq;
using Cysharp.Threading.Tasks;
using Naraka.Boot;
using Naraka.Core.Application.Scenes;
using Naraka.Features.Character.Model;
using Naraka.Features.Character.View;
using Naraka.Features.Dialogue.Controller;
using Naraka.Features.Dialogue.View;
using Naraka.Features.Quest.Controller;
using Naraka.Features.Quest.View;
using Naraka.Features.World.Controller;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using VContainer;

namespace Naraka.P2.PlayMode.Tests
{
    /// <summary>
    /// P4.1 正式 Map01 装配验收：真实场景、真实触发器、真实 Input System 虚拟设备。
    /// 测试环境没有服务端任务能力，因此 NPC 只播日常对白；这正好覆盖"旧 Host 不发任务协议"的分支，
    /// 并验证交互范围、E 键、底部对话框、输入锁与释放的场景装配。
    /// </summary>
    public sealed class P41NpcDialoguePlayModeTests
    {
        private const float SceneLoadTimeoutSeconds = 30f;

        private InputTestFixture _input;
        private Keyboard _keyboard;
        private Mouse _mouse;

        [SetUp]
        public void SetUp()
        {
            DestroyPersistentRoot();
            _input = new InputTestFixture();
            _input.Setup();
            _keyboard = InputSystem.AddDevice<Keyboard>();
            _mouse = InputSystem.AddDevice<Mouse>();
        }

        [TearDown]
        public void TearDown()
        {
            DestroyPersistentRoot();
            _input?.TearDown();
            _input = null;
        }

        [UnityTest]
        public IEnumerator Map01HostsBothNpcsAndTheQuestHud()
        {
            yield return EnterMap01();

            var volumes = Object.FindObjectsOfType<NpcInteractionVolume>();
            Assert.That(volumes.Select(volume => volume.NpcId).OrderBy(id => id),
                Is.EqualTo(new[] { "npc_shuangchi", "npc_yunsheng" }));
            foreach (var volume in volumes)
            {
                var sphere = volume.GetComponent<SphereCollider>();
                Assert.That(sphere.isTrigger, Is.True);
                var animator = volume.GetComponentInChildren<Animator>();
                Assert.That(animator.avatar != null && animator.avatar.isHuman, Is.True, volume.NpcId);
                Assert.That(animator.runtimeAnimatorController.animationClips.Length, Is.EqualTo(1),
                    "NPC 只播放 Idle。");
            }

            Assert.That(Object.FindObjectOfType<DialogueBoxView>(), Is.Not.Null);
            Assert.That(Object.FindObjectOfType<InteractPromptView>(), Is.Not.Null);
            Assert.That(Object.FindObjectOfType<QuestTrackerView>(), Is.Not.Null);
            Assert.That(Object.FindObjectOfType<QuestRewardPanelView>(), Is.Not.Null);
            yield return null;
            Assert.That(Object.FindObjectsOfType<EventSystem>().Length, Is.EqualTo(1),
                "领取面板需要且只需要一个 EventSystem（运行时补建，不保存在正式场景里）。");
        }

        [UnityTest]
        public IEnumerator PressingEInsideTheRangeOpensTheBottomDialogueAndLocksThenReturnsInput()
        {
            yield return EnterMap01();
            var player = Object.FindObjectOfType<PlayerCharacterView>();
            yield return WaitUntil(() => player.State.Action == ActionState.None, 12f, "spawn");

            var shen = Object.FindObjectsOfType<NpcInteractionVolume>().Single(volume => volume.NpcId == "npc_shuangchi");
            var interaction = WorldResolve<INpcInteractionController>();
            var dialogue = WorldResolve<IDialogueController>();

            Teleport(player, shen.transform.position + (shen.transform.forward * 2f));
            yield return WaitUntil(() => interaction.Current.PromptVisible, 3f, "prompt");
            Assert.That(interaction.Current.FocusedNpcName, Is.EqualTo("沈霜迟"));

            yield return Tap(_keyboard[Key.E]);
            yield return WaitUntil(() => dialogue.IsOpen, 2f, "dialogue-open");

            var box = Object.FindObjectOfType<DialogueBoxView>();
            var speaker = box.GetComponentsInChildren<Text>(true).Single(text => text.name == "SpeakerName");
            yield return WaitUntil(() => speaker.text == "沈霜迟", 2f, "speaker");
            Assert.That(dialogue.Current.Text, Is.EqualTo("此门之后妖物横行，没准备就别靠近。"));
            yield return WaitUntil(() => player.State.IsInputLocked, 2f, "input-locked");
            Assert.That(Time.timeScale, Is.EqualTo(1f), "对话不暂停世界。");

            // 逐字中点击：揭示整句但不进入下一句。
            yield return Tap(_mouse.leftButton);
            Assert.That(dialogue.Current.LineIndex, Is.EqualTo(0));
            Assert.That(dialogue.Current.IsLineRevealed, Is.True);

            // 继续点击直到读完（日常对白两行）。
            for (var i = 0; i < 6 && dialogue.IsOpen; i++)
            {
                yield return Tap(_mouse.leftButton);
            }

            Assert.That(dialogue.IsOpen, Is.False);
            yield return WaitUntil(() => !player.State.IsInputLocked, 2f, "input-released");
            yield return WaitUntil(() => interaction.Current.PromptVisible, 2f, "prompt-again");

            Teleport(player, shen.transform.position + (shen.transform.forward * 12f));
            yield return WaitUntil(() => !interaction.Current.PromptVisible, 3f, "prompt-hidden");
        }

        private IEnumerator Tap(UnityEngine.InputSystem.Controls.ButtonControl button)
        {
            _input.Press(button);
            yield return null;
            yield return null;
            _input.Release(button);
            yield return null;
            yield return null;
        }

        private static void Teleport(PlayerCharacterView player, Vector3 position)
        {
            var body = player.GetComponent<CharacterController>();
            body.enabled = false;
            player.transform.position = position + (Vector3.up * 0.1f);
            body.enabled = true;
            Physics.SyncTransforms();
        }

        private static T WorldResolve<T>()
        {
            var scope = Object.FindObjectOfType<WorldSceneLifetimeScope>();
            Assert.That(scope, Is.Not.Null, "Map01 缺少 WorldSceneLifetimeScope。");
            return scope.Container.Resolve<T>();
        }

        private static void DestroyPersistentRoot()
        {
            foreach (var root in Object.FindObjectsOfType<AppRootLifetimeScope>())
            {
                Object.DestroyImmediate(root.gameObject);
            }
        }

        private static IEnumerator EnterMap01()
        {
            SceneManager.LoadScene("SampleScene", LoadSceneMode.Single);
            yield return null;
            yield return null;
            var root = AppRootLifetimeScope.Instance;
            Assert.That(root, Is.Not.Null);
            yield return null;

            var world = root.Container.Resolve<IWorldFlowController>();
            var task = world.EnterMap01Async(default).Preserve();
            var deadline = Time.realtimeSinceStartup + SceneLoadTimeoutSeconds;
            while (!task.Status.IsCompleted() && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(task.Status.IsCompleted(), Is.True, "进入地图一没有在超时前完成。");
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(WorldSceneNames.Map01Task));
            yield return WaitUntil(() => Object.FindObjectOfType<PlayerCharacterView>() != null, 6f, "player");
        }

        private static IEnumerator WaitUntil(System.Func<bool> condition, float timeoutSeconds, string label)
        {
            var deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (!condition() && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(condition(), Is.True, $"等待条件在 {timeoutSeconds} 秒内没有满足（{label}）。");
        }
    }
}
