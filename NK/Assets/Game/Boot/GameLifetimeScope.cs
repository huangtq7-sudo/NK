using Naraka.Core.Application.Networking;
using Naraka.Infrastructure.Network;
using VContainer;
using VContainer.Unity;

namespace Naraka.Boot
{
    public sealed class GameLifetimeScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            builder.Register<MockNetworkFacade>(Lifetime.Singleton).As<INetworkFacade>();
        }
    }
}
