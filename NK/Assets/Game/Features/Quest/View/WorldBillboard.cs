using UnityEngine;

namespace Naraka.Features.Quest.View
{
    /// <summary>
    /// NPC 头顶标记始终朝向主相机。纯表现，只改自身旋转；相机缺失时保持原朝向。
    /// </summary>
    public sealed class WorldBillboard : MonoBehaviour
    {
        private Camera _camera;

        private void LateUpdate()
        {
            if (_camera == null || !_camera.isActiveAndEnabled)
            {
                _camera = Camera.main;
                if (_camera == null)
                {
                    return;
                }
            }

            transform.rotation = _camera.transform.rotation;
        }
    }
}
