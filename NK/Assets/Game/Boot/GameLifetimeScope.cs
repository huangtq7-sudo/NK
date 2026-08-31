using Naraka.Core.Application.Networking;
using Naraka.Features.Account.Controller;
using Naraka.Features.Account.Model;
using Naraka.Features.Account.View;
using Naraka.Features.Lobby.Controller;
using Naraka.Features.Lobby.Model;
using Naraka.Features.Lobby.View;
using Naraka.Infrastructure.Network;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Naraka.Boot
{
    public sealed class GameLifetimeScope : LifetimeScope
    {
        [SerializeField] private string serverAddress = "127.0.0.1";
        [SerializeField] private int serverPort = 8011;

        protected override void Configure(IContainerBuilder builder)
        {
            var network = new LegacyNetworkAdapter(serverAddress, serverPort);
            builder.RegisterInstance<INetworkFacade, IAccountGateway>(network);

            builder.Register<AccountSessionModel>(Lifetime.Singleton);
            builder.Register<AccountController>(Lifetime.Singleton);
            builder.Register<LobbyModel>(Lifetime.Singleton);
            builder.Register<LobbyController>(Lifetime.Singleton);
            builder.RegisterComponentInHierarchy<AccountView>();
            builder.RegisterComponentInHierarchy<LobbyView>();
            builder.RegisterEntryPoint<P0FlowCoordinator>();
        }
    }
}
