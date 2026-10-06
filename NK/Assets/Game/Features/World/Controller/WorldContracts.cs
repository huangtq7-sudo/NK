using Naraka.Core.Application.MVC;

namespace Naraka.Features.World.Controller
{
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
    ///
    /// <see cref="CurrentMapId"/> 是 <c>Naraka.Core.Application.Scenes.WorldMapIds</c>
    /// 里的**稳定业务 ID**（例如 <c>Map01</c>），**不是** Unity 场景名。
    /// 这个区分是硬要求：这个字段会流向未来的远征记录、服务端协议与数据库，
    /// 而场景文件名属于资源实现，随时可能因为换美术素材而改变。
    /// MapId 到场景名的翻译只发生在场景加载边界，见 <c>IWorldSceneCatalog</c>。
    /// </summary>
    public readonly struct WorldPresentationState : IPresentationState
    {
        public WorldPresentationState(string currentMapId, WorldArrival arrival, bool isTransitioning)
        {
            CurrentMapId = currentMapId ?? string.Empty;
            Arrival = arrival;
            IsTransitioning = isTransitioning;
        }

        /// <summary>稳定业务地图 ID，绝不是 Unity 场景名，也绝不是场景列表下标。</summary>
        public string CurrentMapId { get; }

        public WorldArrival Arrival { get; }

        /// <summary>正在切换场景。为真时任何新的切换请求都必须被拒绝。</summary>
        public bool IsTransitioning { get; }

        public static WorldPresentationState Initial =>
            new WorldPresentationState(string.Empty, WorldArrival.None, false);
    }
}
