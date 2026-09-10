using UnityEngine.UIElements;
using UnityEngine.UIElements.Experimental;

namespace Naraka.Features.Lobby.View
{
    /// <summary>
    /// 弹窗的掉落回弹动画。外观面板与功能面板共用同一实现，
    /// 保证"从上到下并弹跳"的观感完全一致。
    /// </summary>
    internal static class LobbyPanelAnimation
    {
        private const float DropFromY = -1000f;
        private const int DropDurationMs = 420;

        public static void PlayDrop(VisualElement window)
        {
            if (window == null)
            {
                return;
            }

            window.style.translate = new Translate(0f, DropFromY);
            window.experimental.animation
                .Start(DropFromY, 0f, DropDurationMs,
                    (element, value) => element.style.translate = new Translate(0f, value))
                .Ease(Easing.OutBack);
        }
    }
}
