namespace Naraka.Features.Character.Controller
{
    /// <summary>
    /// 摄像机的水平朝向。移动是相机相对的，因此状态机需要知道"现在镜头朝哪"，
    /// 但它不需要知道 Cinemachine、Transform 或任何摄像机实现的存在。
    ///
    /// 具体实现位于 Infrastructure（<c>ThirdPersonCameraRig</c>）。
    /// 没有绑定实现时 View 会退回使用角色自身朝向，此时移动变成角色相对，
    /// 仍然可以操作，不会因为缺少摄像机就完全动不了。
    /// </summary>
    public interface ICameraOrientation
    {
        /// <summary>摄像机当前水平朝向（度）。</summary>
        float Yaw { get; }
    }
}
