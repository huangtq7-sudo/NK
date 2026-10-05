using Naraka.Features.Combat.Controller;
using Naraka.Features.Combat.Model;
using UnityEngine;

namespace Naraka.Features.Combat.View
{
    /// <summary>
    /// 灰盒训练假人。可以被三段攻击、蓄力与技能打到，有生命、护甲、防御与受击闪色。
    ///
    /// 它刻意不实现任何 AI、寻路、掉落、韧性、处决或服务端同步 ——
    /// 那些属于怪物模块，本类只验证命中链与伤害公式是否真实成立。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TrainingDummy : DamageReceiver
    {
        [SerializeField] private float maxHealth = 500f;

        [Tooltip("护甲耐久。伤害先扣护甲，溢出部分才扣生命。")]
        [SerializeField] private float maxArmor;

        [Tooltip("防御。最终伤害 = 原始伤害 × 100 / (100 + 防御)。")]
        [SerializeField] private float defense;

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

        public float Armor => _model?.Armor ?? maxArmor;

        public float Defense => defense;

        public override bool IsAlive => _model != null && !_model.IsDead;

        private void Awake()
        {
            _model = new DamageableModel(maxHealth, maxArmor, defense);
            _block = new MaterialPropertyBlock();
            if (targetRenderer == null)
            {
                targetRenderer = GetComponentInChildren<Renderer>();
            }

            ApplyColor(normalColor);
        }

        public override DamageApplication TakeDamage(in HitRequest request)
        {
            if (_model == null)
            {
                return default;
            }

            var result = _model.ApplyRawDamage(request.RawDamage);
            if (result.Total <= 0f)
            {
                return default;
            }

            _flashRemaining = flashSeconds;
            ApplyColor(hitColor);
            RaiseDamageDisplayed(result.Total);
            return new DamageApplication(result.ArmorLost, result.HealthLost, result.Died);
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
