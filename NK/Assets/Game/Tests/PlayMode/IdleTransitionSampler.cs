using System;
using System.Collections.Generic;
using Naraka.Features.Character.Model;
using Naraka.Features.Character.View;
using UnityEngine;

namespace Naraka.P2.PlayMode.Tests
{
    internal struct SampleFrame
    {
        public float Time;
        public float DeltaTime;
        public int GcGen0;
        public PlayerAnimation Animation;
        public string State;
        public string Next;
        public bool InTransition;
        public float StateTime;
        public float TransitionTime;
        public float AnimatorSpeed;
        public float HairSpeed;
        public float BodySpeed;
        public float RootSpeed;
        public float RootHeight;
        public float RootYaw;
    }

    /// <summary>
    /// 逐帧采样器。执行顺序排在最后：Animator 求值与根位移抵消（LateUpdate，顺序 200）都已完成，
    /// 读到的就是这一帧最终呈现的姿态。不用 WaitForEndOfFrame —— 批处理的 -nographics 下它不会返回。
    /// </summary>
    [DefaultExecutionOrder(10000)]
    internal sealed class IdleTransitionSampler : MonoBehaviour
    {
        private static readonly Dictionary<int, string> StateNames = BuildStateNames();

        private readonly List<SampleFrame> _frames = new List<SampleFrame>();
        private PlayerAnimatorProjector _projector;
        private Animator _animator;
        private Transform _root;
        private Transform[] _bones;
        private int[] _groups;
        private Quaternion[] _previous;
        private bool _recording;
        private bool _hasPrevious;
        private PlayerAnimation _startAnimation;

        public PlayerAnimation CurrentAnimation => _projector != null ? _projector.CurrentAnimation : PlayerAnimation.None;

        /// <summary>本段是否已经离开起始动画（避免把"还在 Idle、动作没开始"当成已经回到 Idle）。</summary>
        public bool HasLeftStartAnimation { get; private set; }

        public void Begin()
        {
            _frames.Clear();
            _recording = true;
            _hasPrevious = false;
            _startAnimation = CurrentAnimation;
            HasLeftStartAnimation = _startAnimation != PlayerAnimation.Idle;
        }

        public List<SampleFrame> End()
        {
            _recording = false;
            return new List<SampleFrame>(_frames);
        }

        private void Awake()
        {
            _projector = GetComponentInChildren<PlayerAnimatorProjector>();
            _animator = _projector != null ? _projector.GetComponent<Animator>() : null;
            var canceller = GetComponentInChildren<RootMotionCanceller>();
            _root = canceller != null ? canceller.RootBone : null;

            _bones = _animator != null
                ? _animator.GetComponentsInChildren<Transform>(true)
                : Array.Empty<Transform>();
            _groups = new int[_bones.Length];
            for (var i = 0; i < _bones.Length; i++)
            {
                var n = _bones[i].name;
                _groups[i] = _bones[i] == _root
                    ? 2
                    : n.StartsWith("Bone_Hair", StringComparison.Ordinal) ||
                      n.StartsWith("Bone_Piao", StringComparison.Ordinal) ||
                      n.StartsWith("Bangs", StringComparison.Ordinal)
                        ? 0
                        : 1;
            }

            _previous = new Quaternion[_bones.Length];
        }

        private void LateUpdate()
        {
            if (!_recording || _animator == null)
            {
                return;
            }

            if (CurrentAnimation != _startAnimation)
            {
                HasLeftStartAnimation = true;
            }

            var dt = UnityEngine.Time.deltaTime;
            var peaks = new float[3];
            for (var i = 0; i < _bones.Length; i++)
            {
                var rotation = _bones[i].localRotation;
                if (_hasPrevious && dt > 0f)
                {
                    var speed = Quaternion.Angle(_previous[i], rotation) / dt;
                    if (speed > peaks[_groups[i]])
                    {
                        peaks[_groups[i]] = speed;
                    }
                }

                _previous[i] = rotation;
            }

            _hasPrevious = true;
            var current = _animator.GetCurrentAnimatorStateInfo(0);
            var inTransition = _animator.IsInTransition(0);
            _frames.Add(new SampleFrame
            {
                Time = UnityEngine.Time.realtimeSinceStartup,
                DeltaTime = UnityEngine.Time.unscaledDeltaTime,
                GcGen0 = GC.CollectionCount(0),
                Animation = CurrentAnimation,
                State = Name(current.shortNameHash),
                Next = inTransition ? Name(_animator.GetNextAnimatorStateInfo(0).shortNameHash) : "",
                InTransition = inTransition,
                StateTime = current.normalizedTime,
                TransitionTime = inTransition ? _animator.GetAnimatorTransitionInfo(0).normalizedTime : 0f,
                AnimatorSpeed = _animator.speed,
                HairSpeed = peaks[0],
                BodySpeed = peaks[1],
                RootSpeed = peaks[2],
                RootHeight = _root != null ? _root.localPosition.y : 0f,
                RootYaw = _root != null ? _root.localEulerAngles.y : 0f
            });
        }

        private static string Name(int hash) => StateNames.TryGetValue(hash, out var name) ? name : hash.ToString();

        private static Dictionary<int, string> BuildStateNames()
        {
            var map = new Dictionary<int, string>();
            foreach (var field in typeof(PlayerAnimatorProjector.StateNames).GetFields())
            {
                if (field.IsLiteral && field.GetRawConstantValue() is string name)
                {
                    map[Animator.StringToHash(name)] = name;
                }
            }

            return map;
        }
    }
}
