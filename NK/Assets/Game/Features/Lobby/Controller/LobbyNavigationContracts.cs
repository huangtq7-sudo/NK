using System.Threading;
using Cysharp.Threading.Tasks;

namespace Naraka.Features.Lobby.Controller
{
    /// <summary>
    /// 场景加载能力的抽象。具体的Unity SceneManager实现位于Infrastructure。
    /// </summary>
    public interface ILobbySceneGateway
    {
        UniTask LoadMapAsync(string sceneName, CancellationToken cancellationToken);
    }
}
