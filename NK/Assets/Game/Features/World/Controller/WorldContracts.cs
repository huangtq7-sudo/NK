using Naraka.Core.Application.MVC;

namespace Naraka.Features.World.Controller
{
    /// <summary>地图标识。使用稳定字符串 ID，绝不使用场景列表下标。</summary>
    public static class WorldMapIds
    {
        public const string Map01Task = "Map01_Task";
        public const string Map02CombatGraybox = "Map02_CombatGraybox";
    }

    /// <summary>本次进入地图的来源，决定出场动画与出生点。</summary>
    public enum WorldArrival
    {
        None = 0,

        /// <summary>从大厅进入地图一：播放 Burst02。</summary>
        LobbyToMap01 = 1,

        /// <summary>从地图一进入地图二：播放 Burst01。</summary>
        Map01ToMap02 = 2,

        /// <summary>死亡后返回地图一重生点。</summary>
        DeathToMap01 = 3
    }

    /// <summary>
    /// 世界流转的只读状态。场景入口 View 用它决定生成位置与出场动画，
    /// 因此"从哪来"这件事只有一份真相。
    /// </summary>
    public readonly struct WorldPresentationState : IPresentationState
    {
        public WorldPresentationState(string currentMapId, WorldArrival arrival, bool isTransitioning)
        {
            CurrentMapId = currentMapId ?? string.Empty;
            Arrival = arrival;
            IsTransitioning = isTransitioning;
        }

        public string CurrentMapId { get; }

        public WorldArrival Arrival { get; }

        /// <summary>正在切换场景。为真时任何新的切换请求都必须被拒绝。</summary>
        public bool IsTransitioning { get; }

        public static WorldPresentationState Initial =>
            new WorldPresentationState(string.Empty, WorldArrival.None, false);
    }
}
