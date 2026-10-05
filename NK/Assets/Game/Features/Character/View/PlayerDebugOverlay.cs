#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Naraka.Features.Character.Model;
using Naraka.Infrastructure.Input;
using UnityEngine;

namespace Naraka.Features.Character.View
{
    /// <summary>
    /// 开发期调试显示与调试输入。
    ///
    /// 整个文件被 <c>UNITY_EDITOR || DEVELOPMENT_BUILD</c> 包起来，
    /// 正式发布构建里既不存在这个组件，也不会启用调试动作表。
    /// 它不是战斗 HUD：正式 HUD 的视觉设计不在本阶段范围内。
    /// </summary>
    public sealed class PlayerDebugOverlay : MonoBehaviour
    {
        [SerializeField] private PlayerCharacterView player;
        [SerializeField] private NarakaPlayerInputProvider input;
        [SerializeField] private float debugDamage = 120f;
        [SerializeField] private bool showOverlay = true;

        private GUIStyle _style;

        private void Awake()
        {
            if (player == null)
            {
                player = GetComponent<PlayerCharacterView>();
            }
        }

        private void Update()
        {
            if (player == null || input == null)
            {
                return;
            }

            if (input.DebugHitPressed)
            {
                player.ApplyDebugRawDamage(debugDamage);
            }

            if (input.DebugDeathPressed)
            {
                // 原始伤害要先过防御再扣护甲，所以"刚好等于当前生命"是打不死的。
                // 调试用的一击必杀直接给一个足够大的值，不去反推公式。
                player.ApplyDebugRawDamage(
                    (player.State.Health + player.State.Armor) * 100f);
            }

            if (input.DebugRevivePressed)
            {
                player.Respawn();
            }
        }

        private void OnGUI()
        {
            if (!showOverlay || player == null)
            {
                return;
            }

            _style ??= new GUIStyle(GUI.skin.label) { fontSize = 14, richText = false };

            var state = player.State;
            var rect = new Rect(12f, 12f, 460f, 210f);
            GUI.Box(rect, "NARAKA P2 Debug");
            GUILayout.BeginArea(new Rect(rect.x + 10f, rect.y + 24f, rect.width - 20f, rect.height - 34f));
            GUILayout.Label(
                $"生命 {state.Health:0}/{state.MaxHealth:0}    护甲 {state.Armor:0}/{state.MaxArmor:0}",
                _style);
            GUILayout.Label($"体力 {state.Stamina:0.0}/{state.MaxStamina:0}", _style);
            GUILayout.Label(
                $"F 冷却 {state.SkillFCooldownRemaining:0.0}s    V 冷却 {state.SkillVCooldownRemaining:0.0}s",
                _style);
            GUILayout.Label(
                $"Locomotion {state.Locomotion}    Action {state.Action}    Reaction {state.Reaction}",
                _style);
            GUILayout.Label($"连招段 {state.ComboStep}    Flags {state.Flags}", _style);
            GUILayout.Label(
                state.Rejection == ActionRejection.None ? "拒绝原因 无" : $"拒绝原因 {state.Rejection}",
                _style);
            GUILayout.Label("F9 受击    F10 死亡    F11 重生", _style);
            GUILayout.EndArea();
        }
    }
}
#endif
