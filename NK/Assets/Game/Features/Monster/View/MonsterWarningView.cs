using Naraka.Features.Combat.Model;
using UnityEngine;

namespace Naraka.Features.Monster.View
{
    /// <summary>
    /// 技能预警的灰盒表现。
    ///
    /// 它只做一件事：按颜色标签把预警显示出来或收起来。
    /// "现在是不是预警段"是状态机的时间轴说了算，这里绝不自己计时 ——
    /// 否则预警与伤害窗口会各走各的表，玩家看到的红光和实际挨打的时刻对不上。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MonsterWarningView : MonoBehaviour
    {
        [SerializeField] private Renderer warningRenderer;

        [Tooltip("金色可反击技能的预警颜色。")]
        [SerializeField] private Color goldColor = new Color(1f, 0.82f, 0.25f, 0.55f);

        [Tooltip("红色不可反击技能的预警颜色。")]
        [SerializeField] private Color redColor = new Color(0.95f, 0.2f, 0.18f, 0.55f);

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private MaterialPropertyBlock _block;
        private bool _visible;
        private AttackColorTag _colorTag = AttackColorTag.None;

        /// <summary>当前是否正在显示预警。PlayMode 测试直接断言它。</summary>
        public bool IsVisible => _visible;

        public AttackColorTag ColorTag => _colorTag;

        private void Awake()
        {
            _block = new MaterialPropertyBlock();
            if (warningRenderer == null)
            {
                warningRenderer = GetComponent<Renderer>();
            }

            Hide();
        }

        /// <summary>由怪物 View 每帧投影。参数完全来自状态机输出。</summary>
        public void SetWarning(bool active, AttackColorTag colorTag)
        {
            if (!active || colorTag == AttackColorTag.None)
            {
                Hide();
                return;
            }

            _visible = true;
            _colorTag = colorTag;
            if (warningRenderer == null)
            {
                return;
            }

            warningRenderer.enabled = true;
            var color = colorTag == AttackColorTag.Gold ? goldColor : redColor;
            warningRenderer.GetPropertyBlock(_block);
            _block.SetColor(BaseColorId, color);
            _block.SetColor(ColorId, color);
            warningRenderer.SetPropertyBlock(_block);
        }

        private void Hide()
        {
            _visible = false;
            _colorTag = AttackColorTag.None;
            if (warningRenderer != null)
            {
                warningRenderer.enabled = false;
            }
        }
    }
}
