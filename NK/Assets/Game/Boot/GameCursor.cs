using UnityEngine;

namespace Naraka.Boot
{
    /// <summary>
    /// 启动后把系统指针替换为游戏指针。硬件指针没有跟随延迟；
    /// 禁用时还原默认指针，避免退出Play Mode后编辑器仍保留自定义指针。
    /// </summary>
    public sealed class GameCursor : MonoBehaviour
    {
        [SerializeField] private Texture2D cursorTexture;

        /// <summary>指针热点，按MouseCursor.png旋转后的箭头尖端解析计算所得。</summary>
        [SerializeField] private Vector2 hotspot = new Vector2(25f, 18f);

        private void OnEnable()
        {
            if (cursorTexture == null)
            {
                Debug.LogError(
                    "GameCursor 未绑定指针贴图；请运行菜单 NARAKA/Setup/Apply P0 Project Settings。",
                    this);
                return;
            }

            Cursor.SetCursor(cursorTexture, hotspot, CursorMode.Auto);
        }

        private void OnDisable()
        {
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        }
    }
}
