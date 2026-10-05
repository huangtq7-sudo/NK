using System;
using Naraka.Features.Character.Model;
using Naraka.Features.CombatHud.Controller;
using Naraka.Features.Combat.Model;
using Naraka.Features.Monster.Model;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace Naraka.Features.CombatHud.View
{
    /// <summary>
    /// 战斗 HUD 的绑定脚本。
    ///
    /// 它**只做绑定**：把 <see cref="CombatHudPresentationState"/> 里的数字写到
    /// Inspector 上指定的 Slider / Image / Text 上。Canvas 层级、锚点、图片、字体
    /// 与所有视觉决定都由用户在 Unity 里手工完成，这个脚本不创建、不移动、
    /// 也不修改任何 UI 对象的布局。
    ///
    /// 每一个字段都是可选的：只绑一条血条也能正常工作，没绑的部分直接跳过。
    /// 这样用户可以分几次把 HUD 做完，而不是"少绑一个就报空引用"。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatHudView : MonoBehaviour
    {
        [Header("玩家 - 生命")]
        [SerializeField] private Slider playerHealthSlider;
        [SerializeField] private Image playerHealthFill;
        [SerializeField] private TMP_Text playerHealthLabel;

        [Header("玩家 - 护甲")]
        [SerializeField] private Slider playerArmorSlider;
        [SerializeField] private Image playerArmorFill;
        [SerializeField] private TMP_Text playerArmorLabel;

        [Header("玩家 - 体力")]
        [SerializeField] private Slider playerStaminaSlider;
        [SerializeField] private Image playerStaminaFill;
        [SerializeField] private TMP_Text playerStaminaLabel;

        [Header("技能冷却遮罩（Image 需设为 Filled）")]
        [SerializeField] private Image skillFCooldownMask;
        [SerializeField] private TMP_Text skillFCooldownLabel;
        [SerializeField] private Image skillVCooldownMask;
        [SerializeField] private TMP_Text skillVCooldownLabel;

        [Header("提示")]
        [Tooltip("显示“体力不足”“技能冷却中”这类拒绝原因。")]
        [SerializeField] private TMP_Text rejectionLabel;

        [Tooltip("玩家死亡时显示的根对象。")]
        [SerializeField] private GameObject deathRoot;

        [Tooltip("反击判定窗开启时显示的根对象。")]
        [SerializeField] private GameObject counterWindowRoot;

        [Header("目标")]
        [Tooltip("整块怪物血条的根对象。没有目标时整体隐藏。")]
        [SerializeField] private GameObject targetRoot;
        [SerializeField] private TMP_Text targetNameLabel;
        [SerializeField] private Slider targetHealthSlider;
        [SerializeField] private Image targetHealthFill;
        [SerializeField] private Slider targetArmorSlider;
        [SerializeField] private Image targetArmorFill;

        [Tooltip("怪物进入第二阶段时显示的根对象。")]
        [SerializeField] private GameObject targetEnragedRoot;

        [Tooltip("目标进入处决窗口时显示的根对象。")]
        [SerializeField] private GameObject targetExecutableRoot;

        [Header("技能预警")]
        [Tooltip("怪物技能预警的根对象。没有预警时隐藏。")]
        [SerializeField] private GameObject warningRoot;

        [Tooltip("预警图像。金色可反击、红色不可反击，颜色由脚本按标签写入。")]
        [SerializeField] private Image warningImage;

        [SerializeField] private Color goldWarningColor = new Color(1f, 0.82f, 0.25f);
        [SerializeField] private Color redWarningColor = new Color(0.95f, 0.2f, 0.18f);

        [Header("受击反馈")]
        [Tooltip("玩家受击时闪一下的根对象。")]
        [SerializeField] private GameObject playerHitFlashRoot;

        [Tooltip("闪烁持续时间（秒）。")]
        [SerializeField] private float hitFlashSeconds = 0.15f;

        private ICombatHudController _controller;
        private float _playerFlashRemaining;
        private float _monsterFlashRemaining;

        /// <summary>最近一次收到的状态。PlayMode 测试直接断言它。</summary>
        public CombatHudPresentationState State { get; private set; }

        /// <summary>收到过多少次怪物受击反馈。用来证明事件确实接上了。</summary>
        public int MonsterHitFeedbackCount { get; private set; }

        /// <summary>收到过多少次玩家受击反馈。</summary>
        public int PlayerHitFeedbackCount { get; private set; }

        [Inject]
        public void Construct(ICombatHudController controller) => _controller = controller;

        private void OnEnable()
        {
            if (_controller == null)
            {
                return;
            }

            _controller.PlayerDamaged += OnPlayerDamaged;
            _controller.MonsterDamaged += OnMonsterDamaged;
        }

        private void OnDisable()
        {
            if (_controller == null)
            {
                return;
            }

            _controller.PlayerDamaged -= OnPlayerDamaged;
            _controller.MonsterDamaged -= OnMonsterDamaged;
        }

        private void Update()
        {
            if (_controller == null)
            {
                return;
            }

            _controller.Refresh();
            Render(_controller.Current);
            TickFlash(Time.deltaTime);
        }

        /// <summary>把一份状态写到界面上。公开是为了让 PlayMode 测试直接驱动它。</summary>
        public void Render(in CombatHudPresentationState state)
        {
            State = state;

            SetBar(playerHealthSlider, playerHealthFill, state.PlayerHealthRatio);
            SetLabel(playerHealthLabel, state.PlayerHealth, state.PlayerMaxHealth);
            SetBar(playerArmorSlider, playerArmorFill, state.PlayerArmorRatio);
            SetLabel(playerArmorLabel, state.PlayerArmor, state.PlayerMaxArmor);
            SetBar(playerStaminaSlider, playerStaminaFill, state.PlayerStaminaRatio);
            SetLabel(playerStaminaLabel, state.PlayerStamina, state.PlayerMaxStamina);

            SetFill(skillFCooldownMask, state.SkillFCooldownRatio);
            SetSeconds(skillFCooldownLabel, state.SkillFCooldownRemaining);
            SetFill(skillVCooldownMask, state.SkillVCooldownRatio);
            SetSeconds(skillVCooldownLabel, state.SkillVCooldownRemaining);

            SetText(rejectionLabel, Describe(state.Rejection));
            SetActive(deathRoot, !state.PlayerIsAlive);
            SetActive(counterWindowRoot, state.PlayerCounterWindowOpen);

            var showTarget = state.HasTarget && state.TargetIsAlive;
            SetActive(targetRoot, showTarget);
            if (showTarget)
            {
                SetText(targetNameLabel, state.TargetName);
                SetBar(targetHealthSlider, targetHealthFill, state.TargetHealthRatio);
                SetBar(targetArmorSlider, targetArmorFill, state.TargetArmorRatio);
                SetActive(targetEnragedRoot, state.TargetPhase == MonsterPhase.Enraged);
                SetActive(targetExecutableRoot, state.TargetIsExecutable);
            }
            else
            {
                SetActive(targetEnragedRoot, false);
                SetActive(targetExecutableRoot, false);
            }

            var warning = state.TargetWarning;
            SetActive(warningRoot, showTarget && warning != AttackColorTag.None);
            if (warningImage != null && warning != AttackColorTag.None)
            {
                warningImage.color = warning == AttackColorTag.Gold ? goldWarningColor : redWarningColor;
            }
        }

        private void OnPlayerDamaged(CombatFeedback feedback)
        {
            PlayerHitFeedbackCount++;
            _playerFlashRemaining = hitFlashSeconds;
            SetActive(playerHitFlashRoot, true);
        }

        private void OnMonsterDamaged(CombatFeedback feedback)
        {
            MonsterHitFeedbackCount++;
            _monsterFlashRemaining = hitFlashSeconds;
        }

        private void TickFlash(float deltaSeconds)
        {
            if (_playerFlashRemaining > 0f)
            {
                _playerFlashRemaining -= deltaSeconds;
                if (_playerFlashRemaining <= 0f)
                {
                    SetActive(playerHitFlashRoot, false);
                }
            }

            if (_monsterFlashRemaining > 0f)
            {
                _monsterFlashRemaining -= deltaSeconds;
            }
        }

        private static void SetBar(Slider slider, Image fill, float ratio)
        {
            if (slider != null)
            {
                slider.SetValueWithoutNotify(ratio);
            }

            SetFill(fill, ratio);
        }

        private static void SetFill(Image image, float ratio)
        {
            if (image == null)
            {
                return;
            }

            image.fillAmount = ratio;
        }

        private static void SetLabel(TMP_Text label, float value, float max)
        {
            if (label == null)
            {
                return;
            }

            // 只在整数值变化时写字符串，避免每帧都拼一个新串。
            label.SetText("{0}/{1}", Mathf.CeilToInt(value), Mathf.CeilToInt(max));
        }

        private static void SetSeconds(TMP_Text label, float remaining)
        {
            if (label == null)
            {
                return;
            }

            if (remaining <= 0f)
            {
                label.SetText(string.Empty);
                return;
            }

            label.SetText("{0}", Mathf.CeilToInt(remaining));
        }

        private static void SetText(TMP_Text label, string value)
        {
            if (label == null)
            {
                return;
            }

            label.SetText(value ?? string.Empty);
        }

        private static void SetActive(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active)
            {
                target.SetActive(active);
            }
        }

        /// <summary>
        /// 拒绝原因的中文文案。它留在 View 里是对的：这是展示文字，
        /// 业务状态只给枚举，绝不给 UI 文本。
        /// </summary>
        private static string Describe(ActionRejection rejection) => rejection switch
        {
            ActionRejection.InsufficientStamina => "体力不足",
            ActionRejection.SkillFOnCooldown => "F 技能冷却中",
            ActionRejection.SkillVOnCooldown => "V 技能冷却中",
            ActionRejection.InputLocked => "当前无法操作",
            ActionRejection.Busy => "动作进行中",
            _ => string.Empty
        };
    }
}
