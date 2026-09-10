using System;
using UnityEngine;
using Naraka.Features.Lobby.Controller;

namespace Naraka.Features.Lobby.View
{
    /// <summary>
    /// 十个大厅功能面板的背景图目录，按 LobbyFeature 枚举顺序存放。
    /// 内容由菜单 NARAKA/Setup/Apply P0 Project Settings 按 ui_bg_ 前缀与后缀匹配生成。
    /// </summary>
    public sealed class LobbyFeaturePanelCatalog : ScriptableObject
    {
        [SerializeField] private Texture2D[] backgrounds = Array.Empty<Texture2D>();

        public int Count => backgrounds == null ? 0 : backgrounds.Length;

        public Texture2D GetBackground(LobbyFeature feature)
        {
            var index = (int)feature;
            return backgrounds != null && index >= 0 && index < backgrounds.Length
                ? backgrounds[index]
                : null;
        }
    }
}
