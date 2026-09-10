using System;
using System.Collections.Generic;
using UnityEngine;

namespace Naraka.Features.Loadout.View
{
    /// <summary>
    /// 英雄立绘、技能图标与兵器图标的表现资源目录。
    ///
    /// 内容由 Editor 工具按目录扫描写入，View 通过配置中的 IconKey（贴图资源名）在这里查表。
    /// 业务层只使用稳定配置 ID，永远不接触 <see cref="Texture2D"/>。
    /// </summary>
    public sealed class LoadoutIconCatalog : ScriptableObject
    {
        [SerializeField] private Texture2D[] textures = Array.Empty<Texture2D>();

        private Dictionary<string, Texture2D> _byKey;

        public IReadOnlyList<Texture2D> Textures => textures;

        /// <summary>找不到对应贴图时返回 null，调用方保留占位而不是崩溃。</summary>
        public Texture2D GetByKey(string iconKey)
        {
            if (string.IsNullOrEmpty(iconKey))
            {
                return null;
            }

            if (_byKey == null)
            {
                _byKey = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
                foreach (var texture in textures)
                {
                    if (texture != null)
                    {
                        _byKey[texture.name] = texture;
                    }
                }
            }

            return _byKey.TryGetValue(iconKey, out var found) ? found : null;
        }

        public void InvalidateIndex() => _byKey = null;
    }
}
