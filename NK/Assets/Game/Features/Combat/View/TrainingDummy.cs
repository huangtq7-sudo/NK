using Naraka.Features.Combat.Controller;
using Naraka.Features.Combat.Model;
using UnityEngine;

namespace Naraka.Features.Combat.View
{
    /// <summary>
    /// 灰盒训练假人。可以被三段攻击、蓄力与技能打到，有简单生命值和受击闪色。
    ///
    /// 它刻意不实现任何 AI、寻路、掉落、韧性、处决或服务端同步 ——
    /// 那些属于后续阶段，本阶段只验证命中链是否真实成立。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TrainingDummy : DamageReceiver
    {
        [SerializeField] private float maxHealth = 500f;

        [Tooltip("受击闪色持续时间（秒）。")]
        [SerializeField] private float flashSeconds = 0.12f;

        [SerializeField] private Color normalColor = new Color(0.72f, 0.72f, 0.76f);

        [SerializeField] private Color hitColor = new Color(0.95f, 0.35f, 0.25f);

        [SerializeField] private Color deadColor = new Color(0.25f, 0.25f, 0.28f);

        [SerializeField] private Renderer targetRenderer;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private DamageableModel _model;
        private MaterialPropertyBlock _block;
        private float _flashRemaining;

        public float Health => _model?.Health ?? maxHealth;

        public float MaxHealth => maxHealth;

        public override bool IsAlive => _model != null && !_model.IsDead;

        private void Awake()
        {
            _model = new DamageableModel(maxHealth);
            _block = new MaterialPropertyBlock();
            if (targetRenderer == null)
            {
                targetRenderer = GetComponentInChildren<Renderer>();
            }

            ApplyColor(normalColor);
        }

        public override DamageApplication Apply(float amount)
        {
            if (_model == null)
            {
                return default;
            }

            var lost = _model.ApplyDamage(amount);
            if (lost <= 0f)
            {
                return default;
            }

            _flashRemaining = flashSeconds;
            ApplyColor(hitColor);
            RaiseDamageDisplayed(lost);
            return new DamageApplication(lost, _model.IsDead);
        }

        /// <summary>重置假人。灰盒调试用，不代表正式怪物复活逻辑。</summary>
        public void ResetDummy()
        {
            _model?.Reset();
            _flashRemaining = 0f;
            ApplyColor(normalColor);
        }

        private void Update()
        {
            if (_flashRemaining <= 0f)
            {
                return;
            }

            _flashRemaining -= Time.deltaTime;
            if (_flashRemaining > 0f)
            {
                return;
            }

            ApplyColor(IsAlive ? normalColor : deadColor);
        }

        private void ApplyColor(Color color)
        {
            if (targetRenderer == null || _block == null)
            {
                return;
            }

            // MaterialPropertyBlock 不会为每个假人生成材质实例，切场景也不会泄漏材质。
            targetRenderer.GetPropertyBlock(_block);
            _block.SetColor(BaseColorId, color);
            _block.SetColor(ColorId, color);
            targetRenderer.SetPropertyBlock(_block);
        }
    }
}
