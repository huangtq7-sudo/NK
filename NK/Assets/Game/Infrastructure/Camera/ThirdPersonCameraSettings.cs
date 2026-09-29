using UnityEngine;

namespace Naraka.Infrastructure.Camera
{
    /// <summary>
    /// 第三人称镜头配置。距离、俯仰范围、灵敏度、阻尼与上下反转都在这里，
    /// 不散落成代码里的魔法数字。
    /// </summary>
    [CreateAssetMenu(
        fileName = "ThirdPersonCameraSettings",
        menuName = "NARAKA/Third Person Camera Settings")]
    public sealed class ThirdPersonCameraSettings : ScriptableObject
    {
        [Header("跟随")]
        [Tooltip("轴心相对角色根部的高度（米）。越高越俯视，角色在画面里也越靠下。")]
        [SerializeField] private float pivotHeight = 2.8f;

        [Tooltip("相机到轴心的距离（米）。越大角色越小，视野越广。")]
        [SerializeField] private float distance = 9f;

        [Header("旋转")]
        [Tooltip("俯仰角下限（度）。负值表示向上看。")]
        [SerializeField] private float minPitch = -25f;

        [Tooltip("俯仰角上限（度）。正值表示俯视。")]
        [SerializeField] private float maxPitch = 65f;

        [Tooltip("水平灵敏度，度/像素。")]
        [SerializeField] private float yawSensitivity = 0.18f;

        [Tooltip("垂直灵敏度，度/像素。")]
        [SerializeField] private float pitchSensitivity = 0.12f;

        [Tooltip("勾选后鼠标上移为抬头。")]
        [SerializeField] private bool invertPitch;

        [Header("平滑")]
        [Tooltip("轴心跟随角色位置的阻尼时间（秒）。")]
        [SerializeField] private float followDamping = 0.18f;

        [Tooltip("旋转阻尼时间（秒）。0 表示鼠标输入立刻生效。")]
        [SerializeField] private float rotationDamping = 0.05f;

        [Header("遮挡")]
        [Tooltip("相机碰撞检测的层。遇到障碍物时缩短距离，不改变轴心。")]
        [SerializeField] private LayerMask obstacleLayers = ~0;

        [Tooltip("贴墙时保留的最小距离（米）。")]
        [SerializeField] private float minObstacleDistance = 0.8f;

        [Tooltip("相机球形投射半径（米）。")]
        [SerializeField] private float cameraRadius = 0.25f;

        [Tooltip("被遮挡后恢复距离的阻尼时间（秒）。")]
        [SerializeField] private float obstacleDamping = 0.2f;

        [Header("初始朝向")]
        [SerializeField] private float initialYaw;

        [SerializeField] private float initialPitch = 15f;

        public float PivotHeight => pivotHeight;

        public float Distance => distance;

        public float MinPitch => minPitch;

        public float MaxPitch => maxPitch;

        public float YawSensitivity => yawSensitivity;

        public float PitchSensitivity => pitchSensitivity;

        public bool InvertPitch => invertPitch;

        public float FollowDamping => followDamping;

        public float RotationDamping => rotationDamping;

        public LayerMask ObstacleLayers => obstacleLayers;

        public float MinObstacleDistance => minObstacleDistance;

        public float CameraRadius => cameraRadius;

        public float ObstacleDamping => obstacleDamping;

        public float InitialYaw => initialYaw;

        public float InitialPitch => initialPitch;
    }
}
