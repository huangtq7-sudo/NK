using UnityEngine;
using UnityEngine.UI;

namespace Naraka.Features.World.View
{
    /// <summary>
    /// 进入任务界面时的出场屏幕效果：一层全屏的半透明色幕，按时长逐渐变成完全透明。
    ///
    /// 纯代码在运行时生成，不新增也不修改任何 UI 资产；挂在当前场景里，场景卸载时一起销毁，
    /// 播完之后自己销毁。不接收射线，不挡任何点击。
    /// </summary>
    public sealed class EntryScreenFade : MonoBehaviour
    {
        /// <summary>与玩家状态机、出场镜头一致的单帧上限：卡顿时三者保持同步。</summary>
        private const float MaxStep = 0.1f;

        private Image _veil;
        private Color _color;
        private float _startAlpha;
        private float _seconds;
        private float _elapsed;

        /// <summary>当前色幕的不透明度。PlayMode 测试据此核对渐变。</summary>
        public float Alpha => _veil != null ? _veil.color.a : 0f;

        public Color Color => _color;

        /// <summary>
        /// 立刻以 <paramref name="startAlpha"/> 盖住画面，然后用 <paramref name="seconds"/> 秒淡到透明。
        /// </summary>
        public static EntryScreenFade Play(Color color, float startAlpha, float seconds)
        {
            var root = new GameObject("EntryScreenFade");
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue;

            var veil = new GameObject("Veil", typeof(RectTransform));
            veil.transform.SetParent(root.transform, false);
            var rect = (RectTransform)veil.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var image = veil.AddComponent<Image>();
            image.raycastTarget = false;

            var fade = root.AddComponent<EntryScreenFade>();
            fade._veil = image;
            fade._color = color;
            fade._startAlpha = Mathf.Clamp01(startAlpha);
            fade._seconds = Mathf.Max(0.01f, seconds);
            fade.Apply(0f);
            return fade;
        }

        private void Update()
        {
            _elapsed += Mathf.Min(Time.deltaTime, MaxStep);
            var t = Mathf.Clamp01(_elapsed / _seconds);
            Apply(t);
            if (t >= 1f)
            {
                Destroy(gameObject);
            }
        }

        /// <summary>smoothstep：开头与结尾都是缓的，看起来是"逐渐显示"而不是线性擦除。</summary>
        private void Apply(float t)
        {
            var eased = t * t * (3f - (2f * t));
            var color = _color;
            color.a = _startAlpha * (1f - eased);
            _veil.color = color;
        }
    }
}
