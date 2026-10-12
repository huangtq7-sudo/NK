using Naraka.Features.Quest.Controller;
using UnityEngine;
using VContainer.Unity;

namespace Naraka.Boot
{
    /// <summary>
    /// 地图场景里的任务交互帧驱动。它只做两件事：进入地图时同步一次任务日志，
    /// 每帧把真实帧间隔交给交互控制器（读取交互键、推进逐字表现）。
    /// 使用不受 timeScale 影响的帧间隔：对话不暂停世界，逐字也不应因为世界时间被缩放而变慢。
    /// </summary>
    public sealed class QuestWorldEntryPoint : IStartable, ITickable
    {
        private readonly NpcInteractionController _interaction;

        public QuestWorldEntryPoint(NpcInteractionController interaction)
        {
            _interaction = interaction;
        }

        public void Start() => _interaction.Start();

        public void Tick() => _interaction.Tick(Time.unscaledDeltaTime);
    }
}
