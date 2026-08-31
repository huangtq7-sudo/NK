using MessagePipe;
using Naraka.Core.Application.Bootstrap;
using Naraka.Core.Application.Messaging;
using Naraka.Core.Application.Networking;
using Naraka.Features.Account.Controller;
using Naraka.Features.Account.Model;
using Naraka.Features.Account.View;
using Naraka.Features.Bootstrap.Controller;
using Naraka.Features.Bootstrap.Model;
using Naraka.Features.Bootstrap.View;
using Naraka.Features.Lobby.Controller;
using Naraka.Features.Lobby.Model;
using Naraka.Features.Lobby.View;
using Naraka.Infrastructure.Config;
using Naraka.Infrastructure.Messaging;
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
        [SerializeField] private string bootstrapBaseUrl = "http://127.0.0.1:5222";
        [SerializeField] private string configVersion = "p0-config-1";
        [SerializeField] private string protocolVersion = "LegacyNetworkV1";

        protected override void Configure(IContainerBuilder builder)
        {
            builder.RegisterMessagePipe();
            builder.Register<MessagePipeDomainEventBus>(Lifetime.Singleton).As<IDomainEventBus>();

            var network = new LegacyNetworkAdapter(serverAddress, serverPort);
            builder.RegisterInstance<INetworkFacade, IAccountGateway>(network);
            builder.RegisterInstance<IConfigVersionGateway>(
                new UnityConfigVersionGateway(bootstrapBaseUrl));
            builder.RegisterInstance(new ConfigVersionModel(
                UnityEngine.Application.version,
                configVersion,
                protocolVersion));

            builder.Register<ConfigVersionController>(Lifetime.Singleton)
                .AsSelf()
                .As<IStartupReadiness>();
            builder.Register<AccountSessionModel>(Lifetime.Singleton);
            builder.Register<AccountController>(Lifetime.Singleton);
            builder.Register<LobbyModel>(Lifetime.Singleton);
            builder.Register<LobbyController>(Lifetime.Singleton);
            builder.RegisterComponentInHierarchy<ConfigVersionView>();
            builder.RegisterComponentInHierarchy<AccountView>();
            builder.RegisterComponentInHierarchy<LobbyView>();
            builder.RegisterEntryPoint<P0FlowCoordinator>();
        }
    }
}
