using System.Threading;
using Cysharp.Threading.Tasks;
using Naraka.Features.Bootstrap.Model;

namespace Naraka.Features.Bootstrap.Controller
{
    public interface IConfigVersionGateway
    {
        UniTask<ConfigVersionManifest> GetRequiredVersionAsync(CancellationToken cancellationToken);
    }
}
