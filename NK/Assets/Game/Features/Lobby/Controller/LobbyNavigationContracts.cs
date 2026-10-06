using System.Threading;
using Cysharp.Threading.Tasks;

namespace Naraka.Features.Lobby.Controller
{
    /// <summary>
    /// 场景加载能力的抽象。具体的Unity SceneManager实现位于Infrastructure。
    /// </summary>
    public interface ILobbySceneGateway
    {
        /// <param name="mapId">
        /// 稳定业务地图 ID（见 Naraka.Core.Application.Scenes.WorldMapIds），不是 Unity 场景名。
        /// 大厅只负责转发它从不解释的这个标识符，解析成场景名是世界流转的职责。
        /// </param>
        UniTask LoadMapAsync(string mapId, CancellationToken cancellationToken);
    }
}
