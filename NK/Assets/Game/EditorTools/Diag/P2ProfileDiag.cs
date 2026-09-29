using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Naraka.EditorTools.Diag
{
    public static class P2ProfileDiag
    {
        public static void Run()
        {
            var sb = new StringBuilder();
            sb.AppendLine("PROFDIAG ==== 根位移随时间分布 ====");
            foreach (var fbx in new[] { "Move_F", "Attack04_1", "Attack10", "Burst02", "Burst01",
                                        "Attack01", "AM_Summon", "Attack02", "AM_Skill01" })
            {
                var clip = P2AnimationSetup.LoadClip($"{P2AnimationSetup.AnimationRoot}/{fbx}.fbx");
                if (clip == null) { sb.AppendLine($"PROFDIAG {fbx}: <no clip>"); continue; }

                var bindings = AnimationUtility.GetCurveBindings(clip);
                var zb = bindings.FirstOrDefault(
                    b => b.path == "Root" && b.propertyName == "m_LocalPosition.z");
                if (string.IsNullOrEmpty(zb.propertyName))
                {
                    sb.AppendLine($"PROFDIAG {fbx,-12} len={clip.length:0.000} 无根位移曲线");
                    continue;
                }

                var curve = AnimationUtility.GetEditorCurve(clip, zb);
                var start = curve.Evaluate(0f);
                var total = curve.Evaluate(clip.length) - start;
                if (Mathf.Abs(total) < 0.001f)
                {
                    sb.AppendLine($"PROFDIAG {fbx,-12} len={clip.length:0.000} 净位移≈0");
                    continue;
                }

                // 达到总位移 50% / 90% / 99% 的时间
                float t50 = -1f, t90 = -1f, t99 = -1f;
                const int steps = 400;
                for (var i = 0; i <= steps; i++)
                {
                    var t = clip.length * i / steps;
                    var ratio = (curve.Evaluate(t) - start) / total;
                    if (t50 < 0f && ratio >= 0.5f) t50 = t;
                    if (t90 < 0f && ratio >= 0.9f) t90 = t;
                    if (t99 < 0f && ratio >= 0.99f) t99 = t;
                }

                sb.AppendLine(
                    $"PROFDIAG {fbx,-12} len={clip.length:0.000} total={total:0.00} " +
                    $"t50={t50:0.000} t90={t90:0.000} t99={t99:0.000} " +
                    $"(t90占比 {(t90 / clip.length):0.0%})");
            }

            Debug.Log(sb.ToString());
        }
    }
}
