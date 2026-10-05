using Naraka.Features.Combat.Controller;
using Naraka.Features.Combat.Model;
using UnityEngine;
using VContainer;

namespace Naraka.Features.Combat.View
{
    /// <summary>
    /// **开发用反击训练靶**。
    ///
    /// 它按固定节奏放出一个金色可反击技能，好让"Space 反击成功 → 目标进入处决窗口"
    /// 这条路径可以在编辑器里人工走一遍。
    ///
    /// 它**不是**暮影妖狼，也不代表任何正式怪物能力：暮影妖狼在设计上只有
    /// 普通攻击和红色不可反击吐息，两者都不能被反击。给它编一个金色技能
    /// 只为了测试反击，会把一条不存在的设计写进工程。
    /// 因此这个对象独立存在、名字里带 Training，并且默认不参与正式流程。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CounterTrainingTarget : DamageReceiver
    {
        [Tooltip("生命值。它只是靶子，被打死就停止出手。")]
        [SerializeField] private float maxHealth = 400f;

        [SerializeField] private float defense;

        [Tooltip("两次金色技能之间的间隔（秒）。")]
        [SerializeField] private float attackIntervalSeconds = 4f;

        [Tooltip("预警时长（秒）。预警必须先于伤害窗口。")]
        [SerializeField] private float warningSeconds = 0.9f;

        [Tooltip("命中窗时长（秒）。玩家要在这段时间里完成反击。")]
        [SerializeField] private float hitSeconds = 0.3f;

        [Tooltip("扣防御之前的伤害。")]
        [SerializeField] private float rawDamage = 120f;

        [SerializeField] private MeleeHitbox hitbox;

        [SerializeField] private Renderer bodyRenderer;

        [SerializeField] private Color idleColor = new Color(0.45f, 0.45f, 0.5f);

        [SerializeField] private Color warningColor = new Color(1f, 0.82f, 0.25f);

        [SerializeField] private Color counteredColor = new Color(0.3f, 0.85f, 0.95f);

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private DamageableModel _model;
        private MaterialPropertyBlock _block;
        private IHitResolver _resolver;
        private float _cycleElapsed;
        private int _attackId;
        private float _counteredFlash;

        /// <summary>被反击过多少次。人工验收与 PlayMode 测试都看它。</summary>
        public int CounteredCount { get; private set; }

        public override bool IsAlive => _model != null && !_model.IsDead;

        /// <summary>绑定命中结算。由 VContainer 注入，测试也可以直接调用。</summary>
        [Inject]
        public void Construct(IHitResolver resolver)
        {
            _resolver = resolver;
            hitbox?.Bind(resolver);
        }

        private void Awake()
        {
            _model = new DamageableModel(maxHealth, 0f, defense);
            _block = new MaterialPropertyBlock();
            if (hitbox == null)
            {
                hitbox = GetComponentInChildren<MeleeHitbox>();
            }

            if (bodyRenderer == null)
            {
                bodyRenderer = GetComponentInChildren<Renderer>();
            }

            if (hitbox != null)
            {
                hitbox.Countered += OnCountered;
                if (_resolver != null)
                {
                    hitbox.Bind(_resolver);
                }
            }

            ApplyColor(idleColor);
        }

        private void OnDestroy()
        {
            if (hitbox != null)
            {
                hitbox.Countered -= OnCountered;
                hitbox.CloseWindow();
            }
        }

        private void Update()
        {
            if (!IsAlive || hitbox == null)
            {
                hitbox?.CloseWindow();
                return;
            }

            var deltaSeconds = Time.deltaTime;
            _cycleElapsed += deltaSeconds;
            if (_counteredFlash > 0f)
            {
                _counteredFlash -= deltaSeconds;
            }

            var cycle = attackIntervalSeconds + warningSeconds + hitSeconds;
            if (_cycleElapsed >= cycle)
            {
                _cycleElapsed = 0f;
                _attackId++;
            }

            var inWarning = _cycleElapsed >= attackIntervalSeconds &&
                            _cycleElapsed < attackIntervalSeconds + warningSeconds;
            var inHit = _cycleElapsed >= attackIntervalSeconds + warningSeconds;

            hitbox.SetWindow(
                inHit ? _attackId + 1 : 0,
                rawDamage,
                0f,
                AttackColorTag.Gold,
                true);

            if (_counteredFlash > 0f)
            {
                ApplyColor(counteredColor);
            }
            else
            {
                ApplyColor(inWarning || inHit ? warningColor : idleColor);
            }
        }

        private void OnCountered(int counteredBy)
        {
            CounteredCount++;
            _counteredFlash = 0.6f;
            _cycleElapsed = 0f;
            hitbox?.CloseWindow();
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

            RaiseDamageDisplayed(result.Total);
            return new DamageApplication(result.ArmorLost, result.HealthLost, result.Died);
        }

        private void ApplyColor(Color color)
        {
            if (bodyRenderer == null || _block == null)
            {
                return;
            }

            bodyRenderer.GetPropertyBlock(_block);
            _block.SetColor(BaseColorId, color);
            _block.SetColor(ColorId, color);
            bodyRenderer.SetPropertyBlock(_block);
        }
    }
}
