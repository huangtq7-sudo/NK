using System.Collections.Generic;
using Naraka.Features.Combat.View;
using UnityEngine;

namespace Naraka.Features.Character.View
{
    /// <summary>
    /// V 冲刺的路径校验。Model 只给出"沿朝向想走多远"，这里决定实际能走到哪、沿什么路走。
    ///
    /// 做法是沿朝向一小步一小步"走"过去，每一步都必须同时满足：
    ///
    /// - 正下方在一步能上下的高差内有地（没有就是悬崖或空洞）；
    /// - 地面坡度不超过角色的 <see cref="CharacterController.slopeLimit"/>；
    /// - 胶囊前缘下方也有地，不会把角色留在悬崖边上悬空一半；
    /// - 从上一步到这一步的胶囊扫掠不撞东西 —— 扫掠而不是只看落点，
    ///   所以比步长还薄的墙也穿不过去；
    /// - 落脚处放得下整个胶囊。
    ///
    /// 任何一条不满足就停在上一步；第一步就不满足则原地不动。
    /// 冲刺开始时校验一次，得到一串贴地的路径点；此后每帧按进度沿它插值，
    /// 因此冲得再快也看得见轨迹，同时不会穿墙、陷地或冲下悬崖。
    /// 怪物与玩家自己不挡路（凡是挂着 <see cref="IDamageTaker"/> 的碰撞体都忽略），
    /// 触发器也不挡路；其余碰撞层与角色平时走路时完全一致，取自物理碰撞矩阵。
    ///
    /// 这里不用 NavMesh：任务地图没有烘焙 NavMesh，只有战斗地图有，统一用物理才两张图一致。
    /// </summary>
    public static class DashPathResolver
    {
        /// <summary>
        /// 步长。只决定"停在离墙或悬崖多近"的精度；穿墙由两步之间的扫掠防住，与步长无关。
        /// 17.838 米一次大约 72 步，每步几次物理查询，只在冲刺开始时算一次，V 的冷却是 40 秒，开销可以忽略。
        /// </summary>
        private const float StepMeters = 0.25f;

        /// <summary>落点离地的余量，避免一开始就嵌进地面；随后一帧的重力会把它贴实。</summary>
        private const float Clearance = 0.02f;

        private const float MinimumLift = 0.2f;

        private static readonly RaycastHit[] Hits = new RaycastHit[32];
        private static readonly Collider[] Overlaps = new Collider[32];

        /// <summary>角色所在层在物理碰撞矩阵里会碰撞的全部层。</summary>
        public static int ObstacleMaskFor(int layer)
        {
            var mask = 0;
            for (var other = 0; other < 32; other++)
            {
                if (!Physics.GetIgnoreLayerCollision(layer, other))
                {
                    mask |= 1 << other;
                }
            }

            return mask;
        }

        /// <summary>
        /// 返回实际终点（角色根节点位置）。没有任何合法点时返回当前位置。
        /// </summary>
        public static Vector3 Resolve(
            CharacterController body, Vector3 direction, float distance, int obstacleMask)
        {
            var path = new List<Vector3>();
            return ResolvePath(body, direction, distance, obstacleMask, path);
        }

        /// <summary>
        /// 校验整条路径，把起点与每一个合法的贴地路径点依次写入 <paramref name="path"/>，返回终点。
        /// </summary>
        public static Vector3 ResolvePath(
            CharacterController body, Vector3 direction, float distance, int obstacleMask, List<Vector3> path)
        {
            path.Clear();
            var origin = body.transform.position;
            path.Add(origin);
            var flat = Vector3.ProjectOnPlane(direction, Vector3.up);
            if (distance <= 0f || flat.sqrMagnitude < 1e-6f)
            {
                return origin;
            }

            flat.Normalize();
            var shape = Shape.Of(body);
            var current = origin;
            var travelled = 0f;
            while (travelled < distance - 1e-4f)
            {
                var advance = Mathf.Min(StepMeters, distance - travelled);
                var next = current + (flat * advance);
                var feet = current.y + shape.FeetOffset;

                if (!TryFindGround(body, next, feet, shape.MaxRise, obstacleMask, out var ground) ||
                    Vector3.Angle(ground.normal, Vector3.up) > body.slopeLimit ||
                    !TryFindGround(body, next + (flat * shape.Radius), feet, shape.MaxRise, obstacleMask, out _))
                {
                    break;
                }

                var candidate = new Vector3(next.x, ground.point.y - shape.FeetOffset + Clearance, next.z);
                if (SweepBlocked(body, shape, current, candidate, obstacleMask) ||
                    !Fits(body, shape, candidate, obstacleMask))
                {
                    break;
                }

                current = candidate;
                travelled += advance;
                path.Add(current);
            }

            return current;
        }

        /// <summary>
        /// 沿路径走过 <paramref name="progress"/>（0 → 1）比例的长度时所在的位置。
        /// 按路径的实际长度插值，所以上下坡时不会忽快忽慢。
        /// </summary>
        public static Vector3 PointAlong(List<Vector3> path, float progress)
        {
            if (path.Count == 0)
            {
                return Vector3.zero;
            }

            if (path.Count == 1 || progress <= 0f)
            {
                return path[0];
            }

            if (progress >= 1f)
            {
                return path[path.Count - 1];
            }

            var total = 0f;
            for (var i = 1; i < path.Count; i++)
            {
                total += Vector3.Distance(path[i - 1], path[i]);
            }

            var remaining = total * progress;
            for (var i = 1; i < path.Count; i++)
            {
                var segment = Vector3.Distance(path[i - 1], path[i]);
                if (remaining <= segment && segment > 0f)
                {
                    return Vector3.Lerp(path[i - 1], path[i], remaining / segment);
                }

                remaining -= segment;
            }

            return path[path.Count - 1];
        }

        /// <summary>角色根节点放在 <paramref name="position"/> 时，胶囊是否不与任何东西重叠。</summary>
        public static bool Fits(CharacterController body, Vector3 position, int obstacleMask) =>
            Fits(body, Shape.Of(body), position, obstacleMask);

        private static bool Fits(CharacterController body, Shape shape, Vector3 position, int obstacleMask)
        {
            shape.Points(position, out var bottom, out var top);
            var count = Physics.OverlapCapsuleNonAlloc(
                bottom, top, shape.Radius, Overlaps, obstacleMask, QueryTriggerInteraction.Ignore);
            for (var i = 0; i < count; i++)
            {
                if (!IsPassable(body, Overlaps[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool SweepBlocked(
            CharacterController body, Shape shape, Vector3 from, Vector3 to, int obstacleMask)
        {
            var delta = to - from;
            var length = delta.magnitude;
            if (length < 1e-5f)
            {
                return false;
            }

            shape.Points(from, out var bottom, out var top);
            var count = Physics.CapsuleCastNonAlloc(
                bottom, top, shape.Radius, delta / length, Hits, length,
                obstacleMask, QueryTriggerInteraction.Ignore);
            for (var i = 0; i < count; i++)
            {
                // 距离为 0 是起点就重叠的东西（例如背后贴着的墙）。往远离它的方向走不该被它挡住；
                // 真往它里面走，落点的 Fits 检查会拦下来。
                if (Hits[i].distance > 0f && !IsPassable(body, Hits[i].collider))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 在 <paramref name="at"/> 正下方、当前脚底上下各一步高差的范围内找地。
        /// 射线起点压在"最多能爬上去的高度"附近，因此不会把头顶的树冠或屋檐当成地面。
        /// </summary>
        private static bool TryFindGround(
            CharacterController body, Vector3 at, float feet, float maxRise, int obstacleMask, out RaycastHit ground)
        {
            var start = new Vector3(at.x, feet + maxRise + 0.05f, at.z);
            var count = Physics.RaycastNonAlloc(
                start, Vector3.down, Hits, (2f * maxRise) + 0.1f, obstacleMask, QueryTriggerInteraction.Ignore);

            ground = default;
            var nearest = float.MaxValue;
            for (var i = 0; i < count; i++)
            {
                if (Hits[i].distance < nearest && !IsPassable(body, Hits[i].collider))
                {
                    nearest = Hits[i].distance;
                    ground = Hits[i];
                }
            }

            return nearest < float.MaxValue;
        }

        private static bool IsPassable(CharacterController body, Collider collider) =>
            collider == null ||
            collider == body ||
            collider.isTrigger ||
            collider.GetComponentInParent<IDamageTaker>() != null;

        /// <summary>角色胶囊的世界尺寸。取自 CharacterController 的世界包围盒，因此自动带上缩放。</summary>
        private readonly struct Shape
        {
            private Shape(float radius, float halfHeight, Vector3 centerOffset, float lift, float maxRise)
            {
                Radius = radius;
                HalfHeight = halfHeight;
                CenterOffset = centerOffset;
                Lift = lift;
                MaxRise = maxRise;
            }

            public float Radius { get; }

            public float HalfHeight { get; }

            /// <summary>胶囊中心相对根节点的世界偏移。</summary>
            public Vector3 CenterOffset { get; }

            /// <summary>
            /// 扫掠与放置检查时把胶囊底部抬高的量（不高于台阶高度）：一步能跨过的小起伏不算障碍，
            /// 坡面也不会被误判为撞墙。50° 坡上半径 0.32 的胶囊至少要抬 0.18，台阶高度 0.3 足够。
            /// </summary>
            public float Lift { get; }

            /// <summary>相邻两步之间允许的最大高差：台阶高度与坡度上限二者取大。</summary>
            public float MaxRise { get; }

            /// <summary>脚底相对根节点的高度偏移。</summary>
            public float FeetOffset => CenterOffset.y - HalfHeight;

            public static Shape Of(CharacterController body)
            {
                var bounds = body.bounds;
                var scale = body.height > 0f ? (bounds.extents.y * 2f) / body.height : 1f;
                var lift = Mathf.Max(body.stepOffset * scale, MinimumLift);
                var maxRise = Mathf.Max(
                    body.stepOffset * scale,
                    StepMeters * Mathf.Tan(body.slopeLimit * Mathf.Deg2Rad));
                return new Shape(
                    Mathf.Max(bounds.extents.x, bounds.extents.z),
                    bounds.extents.y,
                    bounds.center - body.transform.position,
                    lift,
                    maxRise);
            }

            /// <summary>根节点在 <paramref name="position"/> 时，抬高后的胶囊两端球心。</summary>
            public void Points(Vector3 position, out Vector3 bottom, out Vector3 top)
            {
                var center = position + CenterOffset;
                top = center + (Vector3.up * (HalfHeight - Radius));
                bottom = center + (Vector3.up * (-HalfHeight + Radius + Lift));
                if (bottom.y > top.y)
                {
                    bottom = top;
                }
            }
        }
    }
}
