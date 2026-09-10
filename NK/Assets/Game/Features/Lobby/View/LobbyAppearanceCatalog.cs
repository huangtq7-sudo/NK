using System;
using System.Collections.Generic;
using UnityEngine;

namespace Naraka.Features.Lobby.View
{
    /// <summary>
    /// 头像与头像框的表现资源目录。内容由菜单 NARAKA/Setup/Rescan Appearance Catalog
    /// 按文件名前缀扫描生成。
    ///
    /// 业务层只使用配置中的稳定 ID；View 通过配置里的 IconKey（贴图文件名）在这里查表拿到贴图，
    /// 因此 Controller、Model 与服务端都不接触 <see cref="Texture2D"/>。
    /// </summary>
    public sealed class LobbyAppearanceCatalog : ScriptableObject
    {
        [SerializeField] private Texture2D[] avatars = Array.Empty<Texture2D>();
        [SerializeField] private Texture2D[] frames = Array.Empty<Texture2D>();

        private Dictionary<string, Texture2D> _byKey;

        public IReadOnlyList<Texture2D> Avatars => avatars;

        public IReadOnlyList<Texture2D> Frames => frames;

        /// <summary>
        /// 按配置中的 IconKey 取贴图。IconKey 就是贴图资源名，因此配置换一张图只需要改 CSV。
        /// 找不到时返回 null，调用方保留占位而不是崩溃。
        /// </summary>
        public Texture2D GetByKey(string iconKey)
        {
            if (string.IsNullOrEmpty(iconKey))
            {
                return null;
            }

            EnsureIndex();
            return _byKey.TryGetValue(iconKey, out var texture) ? texture : null;
        }

        public Texture2D GetAvatar(int index) => Get(avatars, index);

        public Texture2D GetFrame(int index) => Get(frames, index);

        /// <summary>目录内容在编辑器里重新扫描后需要重建索引。</summary>
        public void InvalidateIndex() => _byKey = null;

        private void EnsureIndex()
        {
            if (_byKey != null)
            {
                return;
            }

            _byKey = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
            Index(avatars);
            Index(frames);
        }

        private void Index(Texture2D[] textures)
        {
            if (textures == null)
            {
                return;
            }

            foreach (var texture in textures)
            {
                if (texture != null)
                {
                    _byKey[texture.name] = texture;
                }
            }
        }

        private static Texture2D Get(Texture2D[] source, int index) =>
            source != null && index >= 0 && index < source.Length ? source[index] : null;
    }
}
