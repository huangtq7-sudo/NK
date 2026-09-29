using System;

namespace Naraka.Features.Character.Model
{
    /// <summary>
    /// 一帧玩家输入意图。View 把 Input System 的读数、摄像机朝向与角色当前朝向
    /// 翻译成这个纯数据结构，因此状态机不知道键位、设备、摄像机对象或 Transform 的存在，
    /// EditMode 里可以直接构造它。
    ///
    /// 移动是**相机相对**的：WASD 四个方向都会移动，方向由摄像机朝向决定，
    /// 角色转过去之后一直播放前进动画。没有后退，也没有原地转身。
    /// </summary>
    public readonly struct PlayerInputFrame
    {
        public PlayerInputFrame(
            float moveX,
            float moveY,
            float cameraYaw,
            float facingYaw,
            bool sprintHeld,
            bool attackHeld,
            bool skillFPressed,
            bool skillVPressed,
            bool cameraMoved)
        {
            MoveX = moveX;
            MoveY = moveY;
            CameraYaw = cameraYaw;
            FacingYaw = facingYaw;
            SprintHeld = sprintHeld;
            AttackHeld = attackHeld;
            SkillFPressed = skillFPressed;
            SkillVPressed = skillVPressed;
            CameraMoved = cameraMoved;
        }

        /// <summary>沿摄像机右方向的输入分量：D 为 +1，A 为 -1。</summary>
        public float MoveX { get; }

        /// <summary>沿摄像机前方向的输入分量：W 为 +1，S 为 -1。</summary>
        public float MoveY { get; }

        /// <summary>摄像机当前水平朝向（度）。移动方向以它为基准。</summary>
        public float CameraYaw { get; }

        /// <summary>角色当前水平朝向（度）。状态机据此算出本帧该转多少。</summary>
        public float FacingYaw { get; }

        public bool SprintHeld { get; }

        /// <summary>
        /// 鼠标左键按住状态。普通攻击在释放时提交，蓄力在按住达到阈值时提交，
        /// 因此状态机只需要"现在是否按住"，不需要边沿事件。
        /// </summary>
        public bool AttackHeld { get; }

        public bool SkillFPressed { get; }

        public bool SkillVPressed { get; }

        /// <summary>
        /// 本帧是否转动了摄像机。状态机刻意不使用这个字段：
        /// 单纯转动摄像机不算角色操作，不重置待机计时。保留它是为了让这条保证可被测试断言。
        /// </summary>
        public bool CameraMoved { get; }

        /// <summary>摇杆量，钳制在 0–1。键盘斜向输入的模长会超过 1，必须先归一化。</summary>
        public float MoveMagnitude
        {
            get
            {
                var squared = (MoveX * MoveX) + (MoveY * MoveY);
                if (squared <= 0f)
                {
                    return 0f;
                }

                var magnitude = (float)Math.Sqrt(squared);
                return magnitude > 1f ? 1f : magnitude;
            }
        }

        /// <summary>
        /// 玩家想去的世界朝向（度）。摄像机朝向加上输入在摄像机平面内的角度。
        /// 没有移动输入时返回角色当前朝向，因此调用方不需要额外判空。
        /// </summary>
        public float DesiredWorldYaw
        {
            get
            {
                if (MoveX == 0f && MoveY == 0f)
                {
                    return FacingYaw;
                }

                // Unity 的 yaw 以 +Z 为 0 度、顺时针为正，因此这里是 Atan2(x, y)。
                var local = (float)(Math.Atan2(MoveX, MoveY) * (180.0 / Math.PI));
                return Normalize(CameraYaw + local);
            }
        }

        public static PlayerInputFrame Idle => default;

        public PlayerInputFrame WithMove(float moveX, float moveY) => new PlayerInputFrame(
            moveX, moveY, CameraYaw, FacingYaw,
            SprintHeld, AttackHeld, SkillFPressed, SkillVPressed, CameraMoved);

        public PlayerInputFrame WithCameraYaw(float cameraYaw) => new PlayerInputFrame(
            MoveX, MoveY, cameraYaw, FacingYaw,
            SprintHeld, AttackHeld, SkillFPressed, SkillVPressed, CameraMoved);

        public PlayerInputFrame WithFacingYaw(float facingYaw) => new PlayerInputFrame(
            MoveX, MoveY, CameraYaw, facingYaw,
            SprintHeld, AttackHeld, SkillFPressed, SkillVPressed, CameraMoved);

        public PlayerInputFrame WithSprint(bool held) => new PlayerInputFrame(
            MoveX, MoveY, CameraYaw, FacingYaw,
            held, AttackHeld, SkillFPressed, SkillVPressed, CameraMoved);

        public PlayerInputFrame WithAttack(bool held) => new PlayerInputFrame(
            MoveX, MoveY, CameraYaw, FacingYaw,
            SprintHeld, held, SkillFPressed, SkillVPressed, CameraMoved);

        public PlayerInputFrame WithSkillF(bool pressed) => new PlayerInputFrame(
            MoveX, MoveY, CameraYaw, FacingYaw,
            SprintHeld, AttackHeld, pressed, SkillVPressed, CameraMoved);

        public PlayerInputFrame WithSkillV(bool pressed) => new PlayerInputFrame(
            MoveX, MoveY, CameraYaw, FacingYaw,
            SprintHeld, AttackHeld, SkillFPressed, pressed, CameraMoved);

        public PlayerInputFrame WithCameraMoved(bool moved) => new PlayerInputFrame(
            MoveX, MoveY, CameraYaw, FacingYaw,
            SprintHeld, AttackHeld, SkillFPressed, SkillVPressed, moved);

        /// <summary>把角度归一化到 (-180, 180]。</summary>
        public static float Normalize(float degrees)
        {
            degrees %= 360f;
            if (degrees > 180f)
            {
                degrees -= 360f;
            }
            else if (degrees <= -180f)
            {
                degrees += 360f;
            }

            return degrees;
        }

        /// <summary>从 <paramref name="from"/> 转到 <paramref name="to"/> 的最短带符号角度。</summary>
        public static float DeltaAngle(float from, float to) => Normalize(to - from);
    }
}
