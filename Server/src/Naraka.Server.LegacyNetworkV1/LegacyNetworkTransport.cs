using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Naraka.Server.Application.Accounts;
using Naraka.Server.Application.Lobby;
using Naraka.Server.Application.Networking;
using Naraka.Server.Application.Forge;
using Naraka.Server.Application.Gacha;
using Naraka.Server.Application.Inventory;
using Naraka.Server.Application.Progression;
using Naraka.Server.Application.Shop;
using Naraka.Server.Application.Social;
using Naraka.Server.Application.Sessions;
using Naraka.Server.LegacyNetworkV1.Messages;
using Naraka.Server.LegacyNetworkV1.Authentication;
using Naraka.Server.LegacyNetworkV1.Protocol;

namespace Naraka.Server.LegacyNetworkV1;

/// <summary>
/// Runtime boundary for the frozen AES/handshake/protobuf/framing/heartbeat/Socket.Select transport.
/// Business authentication is delegated to Application services; this type only adapts the V1 wire.
/// </summary>
public sealed class LegacyNetworkTransport(
    AccountService accounts,
    LoginSessionService loginSessions,
    LegacySessionAccountResolver sessionAccounts,
    LobbyAccountService lobbyAccounts,
    AccountProfileService accountProfiles,
    InventoryService inventories,
    ShopService shops,
    ForgeService forges,
    GachaService gachas,
    SignInService signIns,
    AchievementService achievements,
    RedDotService redDots,
    SocialService socials,
    ILegacyTransportDiagnostics? diagnostics = null) : ILegacyNetworkTransport
{
    public const string PublicHandshakeKey = "abc123";
    public const int LegacyClientHeartbeatIntervalSeconds = 300;
    public static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromSeconds(360);

    private const int SelectTimeoutMicroseconds = 1_000_000;

    /// <summary>RequestId 上限。超长请求号不做截断，直接判为非法请求。</summary>
    private const int MaximumRequestIdLength = 64;

    private readonly ILegacyTransportDiagnostics _diagnostics =
        diagnostics ?? NullLegacyTransportDiagnostics.Instance;

    private int _isRunning;
    private int _isIntegrated;
    private int _listeningPort;
    private int _connectedClientCount;

    public bool IsIntegrated => Volatile.Read(ref _isIntegrated) == 1;

    public int ListeningPort => Volatile.Read(ref _listeningPort);

    public int ConnectedClientCount => Volatile.Read(ref _connectedClientCount);

    public string CompatibilityContract => IsIntegrated
        ? $"LegacyNetworkV1 listening on port {ListeningPort}; frozen V1 wire and authenticated sessions active"
        : "LegacyNetworkV1 socket listener is not active";

    public Task RunAsync(IPAddress listenAddress, int listenPort, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(listenAddress);
        if (listenPort is < 0 or > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(listenPort));
        }

        if (Interlocked.CompareExchange(ref _isRunning, 1, 0) != 0)
        {
            throw new InvalidOperationException("The legacy network transport is already running.");
        }

        return Task.Run(() => RunLoop(listenAddress, listenPort, cancellationToken), CancellationToken.None);
    }

    private void RunLoop(IPAddress listenAddress, int listenPort, CancellationToken cancellationToken)
    {
        Socket? listener = null;
        var clients = new List<LegacyClientConnection>();

        try
        {
            listener = new Socket(listenAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            listener.ExclusiveAddressUse = true;
            listener.Bind(new IPEndPoint(listenAddress, listenPort));
            listener.Listen(100);

            Volatile.Write(ref _listeningPort, ((IPEndPoint)listener.LocalEndPoint!).Port);
            Volatile.Write(ref _isIntegrated, 1);

            while (!cancellationToken.IsCancellationRequested)
            {
                if (!TryWaitForReadable(listener, clients, out var readable))
                {
                    continue;
                }

                if (readable.Contains(listener))
                {
                    AcceptClient(listener, clients);
                    Volatile.Write(ref _connectedClientCount, clients.Count);
                }

                foreach (var client in clients.ToArray())
                {
                    if (readable.Contains(client.Socket) && !ReceiveAndDispatch(client, cancellationToken))
                    {
                        CloseClient(client, clients);
                    }
                }

                var now = DateTime.UtcNow;
                foreach (var client in clients.Where(client => now - client.LastHeartbeatUtc > HeartbeatTimeout).ToArray())
                {
                    _diagnostics.ConnectionDropped(client.ConnectionId, "heartbeat timeout", null);
                    CloseClient(client, clients);
                }
            }
        }
        finally
        {
            foreach (var client in clients.ToArray())
            {
                CloseClient(client, clients);
            }

            CloseSocket(listener);
            Volatile.Write(ref _connectedClientCount, 0);
            Volatile.Write(ref _listeningPort, 0);
            Volatile.Write(ref _isIntegrated, 0);
            Volatile.Write(ref _isRunning, 0);
        }
    }

    /// <summary>
    /// Waits for readable sockets. A broken client handle makes Select fail for the whole set, so that
    /// fault closes the current connections and keeps the listener; with no client left the fault can
    /// only come from the listener itself and must surface.
    /// </summary>
    private bool TryWaitForReadable(
        Socket listener,
        List<LegacyClientConnection> clients,
        out List<Socket> readable)
    {
        readable = new List<Socket>(clients.Count + 1) { listener };
        readable.AddRange(clients.Select(client => client.Socket));

        try
        {
            Socket.Select(readable, null, null, SelectTimeoutMicroseconds);
            return true;
        }
        catch (Exception exception) when (exception is SocketException or ObjectDisposedException)
        {
            if (clients.Count == 0)
            {
                throw;
            }

            foreach (var client in clients.ToArray())
            {
                _diagnostics.ConnectionDropped(client.ConnectionId, "socket wait failed", exception);
                CloseClient(client, clients);
            }

            return false;
        }
    }

    private void AcceptClient(Socket listener, ICollection<LegacyClientConnection> clients)
    {
        Socket socket;
        try
        {
            socket = listener.Accept();
        }
        catch (SocketException exception)
        {
            // A client that vanishes between select and accept must not close the listening port.
            _diagnostics.AcceptFailed(exception);
            return;
        }

        socket.NoDelay = true;
        clients.Add(new LegacyClientConnection(socket));
    }

    /// <summary>
    /// Handles one readable connection. Every connection scoped fault - an unregistered protocol name,
    /// a decryption failure, a malformed frame or a database outage inside a handler - closes only that
    /// connection. Letting one escape would end <see cref="RunLoop"/>, close the listening port and
    /// stop the host, so a single unsupported message from one client made the server unreachable for
    /// everyone until it was restarted by hand.
    /// </summary>
    private bool ReceiveAndDispatch(LegacyClientConnection client, CancellationToken cancellationToken)
    {
        try
        {
            var received = client.Receive();
            if (received == 0)
            {
                return false;
            }

            while (client.TryTakeFrame(out var frame))
            {
                Dispatch(client, frame, cancellationToken);
            }

            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _diagnostics.ConnectionDropped(client.ConnectionId, "frame handling failed", exception);
            return false;
        }
    }

    private void Dispatch(LegacyClientConnection client, LegacyFrame frame, CancellationToken cancellationToken)
    {
        var key = frame.ProtocolName == "MsgSecret"
            ? PublicHandshakeKey
            : client.SessionKey ?? throw new InvalidDataException("Handshake required before business messages.");

        var plaintext = LegacyAesCodec.Decrypt(frame.EncryptedBody, key);
        var message = LegacyProtobufCodec.DeserializeIncoming(frame.ProtocolName, plaintext);
        ValidateEmbeddedProtocol(frame.ProtocolName, message.ProtocolType);

        switch (message)
        {
            case LegacyMsgSecret:
                loginSessions.Disconnect(client.ConnectionId);
                client.SessionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
                Send(client, new LegacyMsgSecret { Secret = client.SessionKey }, PublicHandshakeKey);
                break;

            case LegacyMsgPing:
                client.LastHeartbeatUtc = DateTime.UtcNow;
                Send(client, new LegacyMsgPing(), key);
                break;

            case LegacyMsgRegister register:
                HandleRegister(client, register, key, cancellationToken);
                break;

            case LegacyMsgLogin login:
                HandleLogin(client, login, key, cancellationToken);
                break;

            case LegacyMsgLobbyAccountSummaryRequest summaryRequest:
                HandleLobbyAccountSummary(client, summaryRequest, key, cancellationToken);
                break;

            case LegacyMsgLobbyProfileRequest profileRequest:
                HandleLobbyProfile(client, profileRequest, key, cancellationToken);
                break;

            case LegacyMsgLobbySetAppearanceRequest appearanceRequest:
                HandleLobbySetAppearance(client, appearanceRequest, key, cancellationToken);
                break;

            case LegacyMsgLobbySetLoadoutRequest loadoutRequest:
                HandleLobbySetLoadout(client, loadoutRequest, key, cancellationToken);
                break;

            case LegacyMsgLobbyInventoryRequest inventoryRequest:
                HandleLobbyInventory(client, inventoryRequest, key, cancellationToken);
                break;

            case LegacyMsgLobbyInventoryMutateRequest mutateRequest:
                HandleLobbyInventoryMutate(client, mutateRequest, key, cancellationToken);
                break;

            case LegacyMsgLobbyShopRequest shopRequest:
                HandleLobbyShop(client, shopRequest, key, cancellationToken);
                break;

            case LegacyMsgLobbyShopPurchaseRequest purchaseRequest:
                HandleLobbyShopPurchase(client, purchaseRequest, key, cancellationToken);
                break;

            case LegacyMsgLobbyForgeRequest forgeRequest:
                HandleLobbyForge(client, forgeRequest, key, cancellationToken);
                break;

            case LegacyMsgLobbyForgeUpgradeRequest upgradeRequest:
                HandleLobbyForgeUpgrade(client, upgradeRequest, key, cancellationToken);
                break;

            case LegacyMsgLobbyGachaRequest gachaRequest:
                HandleLobbyGacha(client, gachaRequest, key, cancellationToken);
                break;

            case LegacyMsgLobbyGachaPullRequest pullRequest:
                HandleLobbyGachaPull(client, pullRequest, key, cancellationToken);
                break;

            case LegacyMsgLobbyGachaAcknowledgeRequest acknowledgeRequest:
                HandleLobbyGachaAcknowledge(client, acknowledgeRequest, key, cancellationToken);
                break;

            case LegacyMsgLobbySignInRequest signInRequest:
                HandleLobbySignIn(client, signInRequest, key, cancellationToken);
                break;

            case LegacyMsgLobbySignInClaimRequest signInClaimRequest:
                HandleLobbySignInClaim(client, signInClaimRequest, key, cancellationToken);
                break;

            case LegacyMsgLobbyAchievementRequest achievementRequest:
                HandleLobbyAchievement(client, achievementRequest, key, cancellationToken);
                break;

            case LegacyMsgLobbyAchievementClaimRequest achievementClaimRequest:
                HandleLobbyAchievementClaim(client, achievementClaimRequest, key, cancellationToken);
                break;

            case LegacyMsgLobbyRedDotRequest redDotRequest:
                HandleLobbyRedDot(client, redDotRequest, key, cancellationToken);
                break;

            case LegacyMsgLobbyRedDotSeenRequest redDotSeenRequest:
                HandleLobbyRedDotSeen(client, redDotSeenRequest, key, cancellationToken);
                break;

            case LegacyMsgLobbySocialRequest socialRequest:
                HandleLobbySocial(client, socialRequest, key, cancellationToken);
                break;

            case LegacyMsgLobbySocialSearchRequest socialSearchRequest:
                HandleLobbySocialSearch(client, socialSearchRequest, key, cancellationToken);
                break;

            case LegacyMsgLobbySocialActionRequest socialActionRequest:
                HandleLobbySocialAction(client, socialActionRequest, key, cancellationToken);
                break;

            case LegacyMsgLobbyChatRequest chatRequest:
                HandleLobbyChat(client, chatRequest, key, cancellationToken);
                break;

            default:
                throw new InvalidDataException($"Unsupported legacy message type: {message.GetType().Name}.");
        }
    }

    private void HandleRegister(
        LegacyClientConnection client,
        LegacyMsgRegister request,
        string key,
        CancellationToken cancellationToken)
    {
        var result = accounts.RegisterAsync(
                request.Account ?? string.Empty,
                request.Password ?? string.Empty,
                cancellationToken)
            .GetAwaiter()
            .GetResult();

        var legacyResult = result.Status switch
        {
            AccountRegistrationStatus.Success => LegacyRegisterResult.Success,
            AccountRegistrationStatus.AlreadyExists => LegacyRegisterResult.AlreadyExist,
            AccountRegistrationStatus.InvalidUsername or AccountRegistrationStatus.WeakPassword => LegacyRegisterResult.Failed,
            _ => LegacyRegisterResult.Failed
        };

        Send(client, new LegacyMsgRegister { Result = legacyResult }, key);
    }

    private void HandleLogin(
        LegacyClientConnection client,
        LegacyMsgLogin request,
        string key,
        CancellationToken cancellationToken)
    {
        var result = loginSessions.LoginAsync(
                client.ConnectionId,
                request.Account ?? string.Empty,
                request.Password ?? string.Empty,
                cancellationToken)
            .GetAwaiter()
            .GetResult();

        var legacyResult = result.Status switch
        {
            AccountAuthenticationStatus.Success => LegacyLoginResult.Success,
            AccountAuthenticationStatus.UserNotFound => LegacyLoginResult.UserNotExist,
            AccountAuthenticationStatus.WrongPassword => LegacyLoginResult.WrongPwd,
            AccountAuthenticationStatus.Forbidden => LegacyLoginResult.Failed,
            _ => LegacyLoginResult.Failed
        };

        var accountId = result.Session?.AccountId ?? 0;
        if (accountId > int.MaxValue)
        {
            loginSessions.Disconnect(client.ConnectionId);
            legacyResult = LegacyLoginResult.Failed;
            accountId = 0;
        }

        if (legacyResult == LegacyLoginResult.Success && accountId > 0)
        {
            EnsureProvisioned(client, accountId, cancellationToken);
        }

        Send(client, new LegacyMsgLogin
        {
            Result = legacyResult,
            AccountId = checked((int)accountId)
        }, key);
    }

    /// <summary>
    /// Resolves the account from the authenticated connection session. The request carries no account
    /// id at all, so a client cannot select whose summary it reads (ADR-0007).
    /// </summary>
    private void HandleLobbyAccountSummary(
        LegacyClientConnection client,
        LegacyMsgLobbyAccountSummaryRequest request,
        string key,
        CancellationToken cancellationToken)
    {
        var response = new LegacyMsgLobbyAccountSummaryResponse { RequestId = request.RequestId };

        if (string.IsNullOrWhiteSpace(request.RequestId))
        {
            response.Status = LegacyLobbyAccountSummaryStatus.InvalidRequest;
            Send(client, response, key);
            return;
        }

        long accountId;
        try
        {
            accountId = sessionAccounts.Resolve(client.ConnectionId);
        }
        catch (UnauthorizedAccessException)
        {
            response.Status = LegacyLobbyAccountSummaryStatus.Unauthenticated;
            Send(client, response, key);
            return;
        }

        var result = lobbyAccounts.GetSummaryAsync(accountId, cancellationToken).GetAwaiter().GetResult();
        response.Status = result.Status switch
        {
            LobbyAccountSummaryStatus.Success => LegacyLobbyAccountSummaryStatus.Success,
            LobbyAccountSummaryStatus.Unauthenticated => LegacyLobbyAccountSummaryStatus.Unauthenticated,
            LobbyAccountSummaryStatus.InvalidRequest => LegacyLobbyAccountSummaryStatus.InvalidRequest,
            LobbyAccountSummaryStatus.NotFound => LegacyLobbyAccountSummaryStatus.NotFound,
            LobbyAccountSummaryStatus.DatabaseUnavailable => LegacyLobbyAccountSummaryStatus.DatabaseUnavailable,
            _ => LegacyLobbyAccountSummaryStatus.InternalError
        };

        if (result.Status == LobbyAccountSummaryStatus.Success && result.Summary is not null)
        {
            response.AccountLevel = result.Summary.AccountLevel;
            response.Copper = result.Summary.Copper;
            response.Silk = result.Summary.Silk;
            response.Gold = result.Summary.Gold;
        }

        Send(client, response, key);
    }

    /// <summary>
    /// 登录成功后一次性开通账号：写入资料行并发放初始赠送。整个过程是幂等的，
    /// 已开通的账号只做一次读取，既有余额绝不会被覆盖。
    ///
    /// 开通失败不会阻断登录：登录本身已经成功，此时把玩家挡在门外只会让一次瞬时数据库故障
    /// 变成无法登录。开通会在下一次登录时自动重试，故障本身通过诊断接口上报给运维。
    /// </summary>
    private void EnsureProvisioned(LegacyClientConnection client, long accountId, CancellationToken cancellationToken)
    {
        try
        {
            var result = accountProfiles.EnsureProvisionedAsync(accountId, cancellationToken)
                .GetAwaiter()
                .GetResult();
            if (result.Status != LobbyOperationStatus.Success)
            {
                _diagnostics.BusinessFaulted(client.ConnectionId, "account provisioning", null);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _diagnostics.BusinessFaulted(client.ConnectionId, "account provisioning", exception);
        }
    }

    private void HandleLobbyProfile(
        LegacyClientConnection client,
        LegacyMsgLobbyProfileRequest request,
        string key,
        CancellationToken cancellationToken)
    {
        var response = new LegacyMsgLobbyProfileResponse { RequestId = request.RequestId };
        if (!TryResolveAccount(client, request.RequestId, out var accountId, out var failure))
        {
            response.Status = failure;
            Send(client, response, key);
            return;
        }

        var result = accountProfiles.GetProfileAsync(accountId, cancellationToken).GetAwaiter().GetResult();
        response.Status = ToWireStatus(result.Status);
        if (result.Status == LobbyOperationStatus.Success && result.View is not null)
        {
            ApplyProfile(response, result.View);
        }

        Send(client, response, key);
    }

    private void HandleLobbySetAppearance(
        LegacyClientConnection client,
        LegacyMsgLobbySetAppearanceRequest request,
        string key,
        CancellationToken cancellationToken)
    {
        var response = new LegacyMsgLobbySetAppearanceResponse { RequestId = request.RequestId };
        if (!TryResolveAccount(client, request.RequestId, out var accountId, out var failure))
        {
            response.Status = failure;
            Send(client, response, key);
            return;
        }

        var result = accountProfiles
            .SetAppearanceAsync(accountId, request.AvatarId, request.AvatarFrameId, cancellationToken)
            .GetAwaiter()
            .GetResult();

        response.Status = ToWireStatus(result.Status);
        if (result.Status == LobbyOperationStatus.Success && result.View is not null)
        {
            response.AvatarId = result.View.AvatarId;
            response.AvatarFrameId = result.View.AvatarFrameId;
        }

        Send(client, response, key);
    }

    /// <summary>
    /// 修改出战英雄、兵器与宠物。三个 ID 都由服务端对照配置校验，
    /// 未解锁的宠物返回 NotAvailable 而不是 InvalidRequest，客户端据此给出正确提示。
    /// </summary>
    private void HandleLobbySetLoadout(
        LegacyClientConnection client,
        LegacyMsgLobbySetLoadoutRequest request,
        string key,
        CancellationToken cancellationToken)
    {
        var response = new LegacyMsgLobbySetLoadoutResponse { RequestId = request.RequestId };
        if (!TryResolveAccount(client, request.RequestId, out var accountId, out var failure))
        {
            response.Status = failure;
            Send(client, response, key);
            return;
        }

        var result = accountProfiles
            .SetLoadoutAsync(accountId, request.HeroId, request.WeaponId, request.PetId, cancellationToken)
            .GetAwaiter()
            .GetResult();

        response.Status = ToWireStatus(result.Status);
        if (result.Status == LobbyOperationStatus.Success && result.View is not null)
        {
            response.HeroId = result.View.SelectedHeroId;
            response.WeaponId = result.View.SelectedWeaponId;
            response.PetId = result.View.SelectedPetId;
        }

        Send(client, response, key);
    }

    private void HandleLobbyInventory(
        LegacyClientConnection client,
        LegacyMsgLobbyInventoryRequest request,
        string key,
        CancellationToken cancellationToken)
    {
        var response = new LegacyMsgLobbyInventoryResponse { RequestId = request.RequestId };
        if (!TryResolveAccount(client, request.RequestId, out var accountId, out var failure))
        {
            response.Status = failure;
            Send(client, response, key);
            return;
        }

        var result = inventories.GetAsync(accountId, cancellationToken).GetAwaiter().GetResult();
        response.Status = ToWireStatus(result.Status);
        if (result.Snapshot is not null)
        {
            ApplySnapshot(response.Slots, response.Equipment, result.Snapshot);
            response.Capacity = result.Snapshot.Capacity;
            response.Tier = result.Snapshot.Tier;
        }

        Send(client, response, key);
    }

    /// <summary>
    /// 一次仓库写操作。数量、价格、容量与堆叠上限全部由服务端按配置重新判定，
    /// 请求里的任何数字都只是意图，不是结论。
    /// </summary>
    private void HandleLobbyInventoryMutate(
        LegacyClientConnection client,
        LegacyMsgLobbyInventoryMutateRequest request,
        string key,
        CancellationToken cancellationToken)
    {
        var response = new LegacyMsgLobbyInventoryMutateResponse { RequestId = request.RequestId };
        if (!TryResolveAccount(client, request.RequestId, out var accountId, out var failure))
        {
            response.Status = failure;
            Send(client, response, key);
            return;
        }

        var result = request.Operation switch
        {
            LegacyInventoryOperation.Discard => inventories
                .DiscardAsync(accountId, request.ItemId, request.Quantity, cancellationToken)
                .GetAwaiter().GetResult(),
            LegacyInventoryOperation.Sell => inventories
                .SellAsync(accountId, request.ItemId, request.Quantity, request.RequestId, cancellationToken)
                .GetAwaiter().GetResult(),
            LegacyInventoryOperation.Reorder => inventories
                .ReorderAsync(accountId, request.ItemOrder, cancellationToken)
                .GetAwaiter().GetResult(),
            LegacyInventoryOperation.AutoSort => inventories
                .AutoSortAsync(accountId, cancellationToken)
                .GetAwaiter().GetResult(),
            LegacyInventoryOperation.Equip => inventories
                .EquipAsync(accountId, request.SlotKind, request.SlotIndex, request.ItemId, cancellationToken)
                .GetAwaiter().GetResult(),
            LegacyInventoryOperation.Unequip => inventories
                .EquipAsync(accountId, request.SlotKind, request.SlotIndex, null, cancellationToken)
                .GetAwaiter().GetResult(),
            LegacyInventoryOperation.Expand => inventories
                .ExpandAsync(accountId, request.RequestId, cancellationToken)
                .GetAwaiter().GetResult(),
            _ => InventoryResult.Failed(LobbyOperationStatus.InvalidRequest)
        };

        response.Status = ToWireStatus(result.Status);
        if (result.Snapshot is not null)
        {
            ApplySnapshot(response.Slots, response.Equipment, result.Snapshot);
            response.Capacity = result.Snapshot.Capacity;
            response.Tier = result.Snapshot.Tier;
        }

        if (result.Balances is not null)
        {
            response.HasBalances = true;
            response.Copper = result.Balances.Get("Copper");
            response.Silk = result.Balances.Get("Silk");
            response.Gold = result.Balances.Get("Gold");
        }

        Send(client, response, key);
    }

    private void HandleLobbyShop(
        LegacyClientConnection client,
        LegacyMsgLobbyShopRequest request,
        string key,
        CancellationToken cancellationToken)
    {
        var response = new LegacyMsgLobbyShopResponse { RequestId = request.RequestId };
        if (!TryResolveAccount(client, request.RequestId, out var accountId, out var failure))
        {
            response.Status = failure;
            Send(client, response, key);
            return;
        }

        var result = shops.GetAsync(accountId, cancellationToken).GetAwaiter().GetResult();
        response.Status = ToWireStatus(result.Status);
        if (result.View is not null)
        {
            ApplyShopView(response.Purchases, result.View, out var copper, out var silk, out var gold);
            response.Copper = copper;
            response.Silk = silk;
            response.Gold = gold;
        }

        Send(client, response, key);
    }

    /// <summary>
    /// 购买。价格、限购、堆叠上限与容量都由服务端从配置重新计算，
    /// 请求里只有商品 ID 与数量；同一个 RequestId 重复提交只会扣一次费。
    /// </summary>
    private void HandleLobbyShopPurchase(
        LegacyClientConnection client,
        LegacyMsgLobbyShopPurchaseRequest request,
        string key,
        CancellationToken cancellationToken)
    {
        var response = new LegacyMsgLobbyShopPurchaseResponse { RequestId = request.RequestId };
        if (!TryResolveAccount(client, request.RequestId, out var accountId, out var failure))
        {
            response.Status = failure;
            Send(client, response, key);
            return;
        }

        var result = shops
            .PurchaseAsync(accountId, request.ProductId, request.Quantity, request.RequestId, cancellationToken)
            .GetAwaiter()
            .GetResult();

        response.Status = ToWireStatus(result.Status);
        if (result.View is not null)
        {
            ApplyShopView(response.Purchases, result.View, out var copper, out var silk, out var gold);
            response.Copper = copper;
            response.Silk = silk;
            response.Gold = gold;
        }

        Send(client, response, key);
    }

    private void HandleLobbyForge(
        LegacyClientConnection client,
        LegacyMsgLobbyForgeRequest request,
        string key,
        CancellationToken cancellationToken)
    {
        var response = new LegacyMsgLobbyForgeResponse { RequestId = request.RequestId };
        if (!TryResolveAccount(client, request.RequestId, out var accountId, out var failure))
        {
            response.Status = failure;
            Send(client, response, key);
            return;
        }

        var result = forges.GetAsync(accountId, cancellationToken).GetAwaiter().GetResult();
        response.Status = ToWireStatus(result.Status);
        if (result.View is not null)
        {
            ApplyForgeView(response.Weapons, response.Materials, result.View);
            response.Copper = result.View.Balances.Get("Copper");
            response.Silk = result.View.Balances.Get("Silk");
            response.Gold = result.View.Balances.Get("Gold");
        }

        Send(client, response, key);
    }

    /// <summary>
    /// 强化一级。材料足够时必定成功——服务端没有任何随机判定，
    /// 失败只可能来自材料不足、货币不足、已达最高等级或界面等级过期。
    /// </summary>
    private void HandleLobbyForgeUpgrade(
        LegacyClientConnection client,
        LegacyMsgLobbyForgeUpgradeRequest request,
        string key,
        CancellationToken cancellationToken)
    {
        var response = new LegacyMsgLobbyForgeUpgradeResponse { RequestId = request.RequestId };
        if (!TryResolveAccount(client, request.RequestId, out var accountId, out var failure))
        {
            response.Status = failure;
            Send(client, response, key);
            return;
        }

        var result = forges
            .UpgradeAsync(accountId, request.WeaponId, request.ExpectedLevel, request.RequestId, cancellationToken)
            .GetAwaiter()
            .GetResult();

        response.Status = ToWireStatus(result.Status);
        if (result.View is not null)
        {
            ApplyForgeView(response.Weapons, response.Materials, result.View);
            response.Copper = result.View.Balances.Get("Copper");
            response.Silk = result.View.Balances.Get("Silk");
            response.Gold = result.View.Balances.Get("Gold");
        }

        Send(client, response, key);
    }

    private void HandleLobbyGacha(
        LegacyClientConnection client,
        LegacyMsgLobbyGachaRequest request,
        string key,
        CancellationToken cancellationToken)
    {
        var response = new LegacyMsgLobbyGachaResponse { RequestId = request.RequestId };
        if (!TryResolveAccount(client, request.RequestId, out var accountId, out var failure))
        {
            response.Status = failure;
            Send(client, response, key);
            return;
        }

        var result = gachas.GetAsync(accountId, request.PoolId, cancellationToken).GetAwaiter().GetResult();
        response.Status = ToWireStatus(result.Status);
        if (result.View is not null)
        {
            response.PoolId = result.View.State.PoolId;
            response.PityCounter = result.View.State.PityCounter;
            response.TotalPulls = result.View.State.TotalPulls;
            foreach (var order in result.View.UnshownOrders)
            {
                response.UnshownOrders.Add(ToWireOrder(order));
            }

            response.Copper = result.View.Balances.Get("Copper");
            response.Silk = result.View.Balances.Get("Silk");
            response.Gold = result.View.Balances.Get("Gold");
        }

        Send(client, response, key);
    }

    /// <summary>
    /// 抽奖。服务端先原子完成扣费、抽取、发奖与结果固化，再把结果回给客户端播放动画；
    /// 客户端不生成任何随机结果，也无法影响任何一次抽取。
    /// </summary>
    private void HandleLobbyGachaPull(
        LegacyClientConnection client,
        LegacyMsgLobbyGachaPullRequest request,
        string key,
        CancellationToken cancellationToken)
    {
        var response = new LegacyMsgLobbyGachaPullResponse { RequestId = request.RequestId };
        if (!TryResolveAccount(client, request.RequestId, out var accountId, out var failure))
        {
            response.Status = failure;
            Send(client, response, key);
            return;
        }

        var result = gachas
            .PullAsync(accountId, request.PoolId, request.PullCount, request.RequestId, cancellationToken)
            .GetAwaiter()
            .GetResult();

        response.Status = ToWireStatus(result.Status);
        if (result.Order is not null)
        {
            response.Order = ToWireOrder(result.Order);
        }

        if (result.View is not null)
        {
            response.PityCounter = result.View.State.PityCounter;
            response.TotalPulls = result.View.State.TotalPulls;
            response.Copper = result.View.Balances.Get("Copper");
            response.Silk = result.View.Balances.Get("Silk");
            response.Gold = result.View.Balances.Get("Gold");
        }

        Send(client, response, key);
    }

    private void HandleLobbyGachaAcknowledge(
        LegacyClientConnection client,
        LegacyMsgLobbyGachaAcknowledgeRequest request,
        string key,
        CancellationToken cancellationToken)
    {
        var response = new LegacyMsgLobbyGachaAcknowledgeResponse { RequestId = request.RequestId };
        if (!TryResolveAccount(client, request.RequestId, out var accountId, out var failure))
        {
            response.Status = failure;
            Send(client, response, key);
            return;
        }

        var result = gachas
            .AcknowledgeAsync(accountId, request.PoolId, request.OrderId, cancellationToken)
            .GetAwaiter()
            .GetResult();

        response.Status = ToWireStatus(result.Status);
        if (result.View is not null)
        {
            response.PityCounter = result.View.State.PityCounter;
            foreach (var order in result.View.UnshownOrders)
            {
                response.UnshownOrders.Add(ToWireOrder(order));
            }
        }

        Send(client, response, key);
    }

    private void HandleLobbySignIn(
        LegacyClientConnection client,
        LegacyMsgLobbySignInRequest request,
        string key,
        CancellationToken cancellationToken)
    {
        var response = new LegacyMsgLobbySignInResponse { RequestId = request.RequestId };
        if (!TryResolveAccount(client, request.RequestId, out var accountId, out var failure))
        {
            response.Status = failure;
            Send(client, response, key);
            return;
        }

        var result = signIns.GetAsync(accountId, cancellationToken).GetAwaiter().GetResult();
        response.Status = ToWireStatus(result.Status);
        if (result.View is not null)
        {
            ApplySignInView(response.Claims, response.ClaimedMilestones, result.View,
                out var cycleStart, out var serverDay, out var consecutive, out var cards);
            response.CycleStartDay = cycleStart;
            response.ServerDay = serverDay;
            response.ConsecutiveDays = consecutive;
            response.MakeupCardCount = cards;
        }

        Send(client, response, key);
    }

    private void HandleLobbySignInClaim(
        LegacyClientConnection client,
        LegacyMsgLobbySignInClaimRequest request,
        string key,
        CancellationToken cancellationToken)
    {
        var response = new LegacyMsgLobbySignInClaimResponse { RequestId = request.RequestId };
        if (!TryResolveAccount(client, request.RequestId, out var accountId, out var failure))
        {
            response.Status = failure;
            Send(client, response, key);
            return;
        }

        var result = request.Kind switch
        {
            LegacySignInClaimKind.Today => signIns
                .ClaimTodayAsync(accountId, cancellationToken).GetAwaiter().GetResult(),
            LegacySignInClaimKind.MakeUp => signIns
                .MakeUpAsync(accountId, request.Target, cancellationToken).GetAwaiter().GetResult(),
            LegacySignInClaimKind.Milestone => signIns
                .ClaimMilestoneAsync(accountId, request.Target, cancellationToken).GetAwaiter().GetResult(),
            _ => SignInResult.Failed(LobbyOperationStatus.InvalidRequest)
        };

        response.Status = ToWireStatus(result.Status);
        if (result.View is not null)
        {
            ApplySignInView(response.Claims, response.ClaimedMilestones, result.View,
                out var cycleStart, out var serverDay, out var consecutive, out var cards);
            response.CycleStartDay = cycleStart;
            response.ServerDay = serverDay;
            response.ConsecutiveDays = consecutive;
            response.MakeupCardCount = cards;
        }

        Send(client, response, key);
    }

    private void HandleLobbyAchievement(
        LegacyClientConnection client,
        LegacyMsgLobbyAchievementRequest request,
        string key,
        CancellationToken cancellationToken)
    {
        var response = new LegacyMsgLobbyAchievementResponse { RequestId = request.RequestId };
        if (!TryResolveAccount(client, request.RequestId, out var accountId, out var failure))
        {
            response.Status = failure;
            Send(client, response, key);
            return;
        }

        var result = achievements.GetAsync(accountId, cancellationToken).GetAwaiter().GetResult();
        response.Status = ToWireStatus(result.Status);
        if (result.View is not null)
        {
            ApplyAchievementView(
                response.Progress, response.ClaimedAchievements, response.ClaimedAccountLevels, result.View);
            response.AchievementXp = result.View.State.AchievementXp;
            response.AchievementLevel = result.View.State.AchievementLevel;
            response.AccountXp = result.View.AccountXp;
            response.AccountLevel = result.View.AccountLevel;
        }

        Send(client, response, key);
    }

    private void HandleLobbyAchievementClaim(
        LegacyClientConnection client,
        LegacyMsgLobbyAchievementClaimRequest request,
        string key,
        CancellationToken cancellationToken)
    {
        var response = new LegacyMsgLobbyAchievementClaimResponse { RequestId = request.RequestId };
        if (!TryResolveAccount(client, request.RequestId, out var accountId, out var failure))
        {
            response.Status = failure;
            Send(client, response, key);
            return;
        }

        var result = request.Kind switch
        {
            LegacyAchievementClaimKind.Achievement => achievements
                .ClaimAchievementAsync(accountId, request.AchievementId, cancellationToken)
                .GetAwaiter().GetResult(),
            LegacyAchievementClaimKind.AccountLevel => achievements
                .ClaimAccountLevelRewardAsync(accountId, request.AccountLevel, cancellationToken)
                .GetAwaiter().GetResult(),
            _ => AchievementResult.Failed(LobbyOperationStatus.InvalidRequest)
        };

        response.Status = ToWireStatus(result.Status);
        if (result.View is not null)
        {
            ApplyAchievementView(
                response.Progress, response.ClaimedAchievements, response.ClaimedAccountLevels, result.View);
            response.AchievementXp = result.View.State.AchievementXp;
            response.AchievementLevel = result.View.State.AchievementLevel;
            response.AccountXp = result.View.AccountXp;
            response.AccountLevel = result.View.AccountLevel;
        }

        Send(client, response, key);
    }

    private static void ApplySignInView(
        List<LegacySignInClaim> claims,
        List<string> milestones,
        SignInView view,
        out long cycleStartDay,
        out long serverDay,
        out int consecutiveDays,
        out long makeupCards)
    {
        foreach (var claim in view.State.Claims)
        {
            claims.Add(new LegacySignInClaim { DayIndex = claim.DayIndex, IsMakeup = claim.IsMakeup });
        }

        milestones.AddRange(view.ClaimedMilestones);
        cycleStartDay = view.State.CycleStartDay;
        serverDay = view.ServerDay;
        consecutiveDays = view.State.ConsecutiveDays;
        makeupCards = view.MakeupCardCount;
    }

    private void HandleLobbyRedDot(
        LegacyClientConnection client,
        LegacyMsgLobbyRedDotRequest request,
        string key,
        CancellationToken cancellationToken)
    {
        var response = new LegacyMsgLobbyRedDotResponse { RequestId = request.RequestId };
        if (!TryResolveAccount(client, request.RequestId, out var accountId, out var failure))
        {
            response.Status = failure;
            Send(client, response, key);
            return;
        }

        var result = redDots.GetAsync(accountId, cancellationToken).GetAwaiter().GetResult();
        response.Status = ToWireStatus(result.Status);
        ApplyRedDotNodes(response.Nodes, result);
        Send(client, response, key);
    }

    private void HandleLobbyRedDotSeen(
        LegacyClientConnection client,
        LegacyMsgLobbyRedDotSeenRequest request,
        string key,
        CancellationToken cancellationToken)
    {
        var response = new LegacyMsgLobbyRedDotSeenResponse { RequestId = request.RequestId };
        if (!TryResolveAccount(client, request.RequestId, out var accountId, out var failure))
        {
            response.Status = failure;
            Send(client, response, key);
            return;
        }

        var result = redDots
            .MarkSeenAsync(accountId, request.Path, request.SeenVersion, cancellationToken)
            .GetAwaiter().GetResult();
        response.Status = ToWireStatus(result.Status);
        ApplyRedDotNodes(response.Nodes, result);
        Send(client, response, key);
    }

    private void HandleLobbySocial(
        LegacyClientConnection client,
        LegacyMsgLobbySocialRequest request,
        string key,
        CancellationToken cancellationToken)
    {
        var response = new LegacyMsgLobbySocialResponse { RequestId = request.RequestId };
        if (!TryResolveAccount(client, request.RequestId, out var accountId, out var failure))
        {
            response.Status = failure;
            Send(client, response, key);
            return;
        }

        var result = socials.GetAsync(accountId, cancellationToken).GetAwaiter().GetResult();
        response.Status = ToWireStatus(result.Status);
        ApplySocialView(
            response.Friends, response.IncomingRequests, response.Blocked,
            response.Conversations, result);
        Send(client, response, key);
    }

    private void HandleLobbySocialSearch(
        LegacyClientConnection client,
        LegacyMsgLobbySocialSearchRequest request,
        string key,
        CancellationToken cancellationToken)
    {
        var response = new LegacyMsgLobbySocialSearchResponse { RequestId = request.RequestId };
        if (!TryResolveAccount(client, request.RequestId, out var accountId, out var failure))
        {
            response.Status = failure;
            Send(client, response, key);
            return;
        }

        var result = socials
            .SearchAsync(accountId, request.DisplayName, cancellationToken)
            .GetAwaiter().GetResult();
        response.Status = ToWireStatus(result.Status);
        response.Found = result.SearchResult is null ? null : ToWirePlayer(result.SearchResult, false);
        ApplySocialView(
            response.Friends, response.IncomingRequests, response.Blocked,
            response.Conversations, result);
        Send(client, response, key);
    }

    private void HandleLobbySocialAction(
        LegacyClientConnection client,
        LegacyMsgLobbySocialActionRequest request,
        string key,
        CancellationToken cancellationToken)
    {
        var response = new LegacyMsgLobbySocialActionResponse { RequestId = request.RequestId };
        if (!TryResolveAccount(client, request.RequestId, out var accountId, out var failure))
        {
            response.Status = failure;
            Send(client, response, key);
            return;
        }

        // 发起方始终是已认证会话里的账号：客户端只能指定对方。
        var result = request.Action switch
        {
            LegacySocialAction.SendRequest => socials
                .SendRequestAsync(accountId, request.TargetAccountId, cancellationToken)
                .GetAwaiter().GetResult(),
            LegacySocialAction.AcceptRequest => socials
                .AcceptRequestAsync(accountId, request.TargetAccountId, cancellationToken)
                .GetAwaiter().GetResult(),
            LegacySocialAction.RejectRequest => socials
                .RejectRequestAsync(accountId, request.TargetAccountId, cancellationToken)
                .GetAwaiter().GetResult(),
            LegacySocialAction.RemoveFriend => socials
                .RemoveFriendAsync(accountId, request.TargetAccountId, cancellationToken)
                .GetAwaiter().GetResult(),
            LegacySocialAction.Block => socials
                .BlockAsync(accountId, request.TargetAccountId, cancellationToken)
                .GetAwaiter().GetResult(),
            LegacySocialAction.Unblock => socials
                .UnblockAsync(accountId, request.TargetAccountId, cancellationToken)
                .GetAwaiter().GetResult(),
            _ => SocialResult.Failed(LobbyOperationStatus.InvalidRequest)
        };

        response.Status = ToWireStatus(result.Status);
        ApplySocialView(
            response.Friends, response.IncomingRequests, response.Blocked,
            response.Conversations, result);
        Send(client, response, key);
    }

    private void HandleLobbyChat(
        LegacyClientConnection client,
        LegacyMsgLobbyChatRequest request,
        string key,
        CancellationToken cancellationToken)
    {
        var response = new LegacyMsgLobbyChatResponse { RequestId = request.RequestId };
        if (!TryResolveAccount(client, request.RequestId, out var accountId, out var failure))
        {
            response.Status = failure;
            Send(client, response, key);
            return;
        }

        var result = request.Action switch
        {
            LegacyChatAction.OpenConversation => socials
                .OpenConversationAsync(accountId, request.PeerAccountId, cancellationToken)
                .GetAwaiter().GetResult(),
            LegacyChatAction.SendMessage => socials
                .SendMessageAsync(accountId, request.PeerAccountId, request.Body, cancellationToken)
                .GetAwaiter().GetResult(),
            LegacyChatAction.MarkRead => socials
                .MarkReadAsync(
                    accountId, request.ConversationId, request.LastReadMessageId, cancellationToken)
                .GetAwaiter().GetResult(),
            _ => SocialResult.Failed(LobbyOperationStatus.InvalidRequest)
        };

        response.Status = ToWireStatus(result.Status);
        foreach (var message in result.Messages)
        {
            response.ConversationId = message.ConversationId;
            response.Messages.Add(new LegacyChatMessage
            {
                MessageId = message.MessageId,
                ConversationId = message.ConversationId,
                SenderAccountId = message.SenderAccountId,
                Body = message.Body,
                SentUnixSeconds = new DateTimeOffset(message.SentUtc, TimeSpan.Zero)
                    .ToUnixTimeSeconds()
            });
        }

        ApplySocialView(
            response.Friends, response.IncomingRequests, response.Blocked,
            response.Conversations, result);
        Send(client, response, key);
    }

    private static void ApplySocialView(
        List<LegacySocialPlayer> friends,
        List<LegacySocialPlayer> incomingRequests,
        List<LegacySocialPlayer> blocked,
        List<LegacyChatConversation> conversations,
        SocialResult result)
    {
        if (result.View is null)
        {
            return;
        }

        foreach (var friend in result.View.Friends)
        {
            friends.Add(ToWirePlayer(friend.Player, friend.IsOnline));
        }

        foreach (var incoming in result.View.IncomingRequests)
        {
            incomingRequests.Add(ToWirePlayer(incoming.Player, false));
        }

        foreach (var player in result.View.Blocked)
        {
            blocked.Add(ToWirePlayer(player, false));
        }

        foreach (var conversation in result.View.Conversations)
        {
            conversations.Add(new LegacyChatConversation
            {
                ConversationId = conversation.ConversationId,
                Peer = ToWirePlayer(conversation.Peer, conversation.IsOnline),
                LastMessageId = conversation.LastMessageId,
                LastReadMessageId = conversation.LastReadMessageId
            });
        }
    }

    private static LegacySocialPlayer ToWirePlayer(SocialPlayer player, bool isOnline) =>
        new()
        {
            AccountId = player.AccountId,
            DisplayName = player.DisplayName,
            AvatarId = player.AvatarId,
            IsOnline = isOnline
        };

    private static void ApplyRedDotNodes(List<LegacyRedDotNode> nodes, RedDotResult result)
    {
        foreach (var row in result.Rows)
        {
            nodes.Add(new LegacyRedDotNode
            {
                Path = row.Path,
                Version = row.Version,
                SeenVersion = row.SeenVersion
            });
        }
    }

    private static void ApplyAchievementView(
        List<LegacyAchievementProgress> progress,
        List<string> claimedAchievements,
        List<string> claimedAccountLevels,
        AchievementView view)
    {
        foreach (var entry in view.Progress)
        {
            progress.Add(new LegacyAchievementProgress
            {
                AchievementId = entry.AchievementId,
                Progress = entry.Progress
            });
        }

        claimedAchievements.AddRange(view.ClaimedAchievements);
        claimedAccountLevels.AddRange(view.ClaimedAccountLevels);
    }

    private static LegacyGachaOrder ToWireOrder(GachaOrder order)
    {
        var wire = new LegacyGachaOrder
        {
            OrderId = order.OrderId,
            PoolId = order.PoolId,
            PullCount = order.PullCount,
            IsShown = order.IsShown
        };

        foreach (var reward in order.Rewards)
        {
            wire.Rewards.Add(new LegacyGachaReward
            {
                RewardId = reward.RewardId,
                ItemId = reward.ItemId,
                Amount = reward.Amount,
                Quality = reward.Quality
            });
        }

        return wire;
    }

    private static void ApplyForgeView(
        List<LegacyAccountWeapon> weapons,
        List<LegacyInventorySlot> materials,
        ForgeView view)
    {
        foreach (var weapon in view.Weapons)
        {
            weapons.Add(new LegacyAccountWeapon
            {
                WeaponId = weapon.WeaponId,
                Level = weapon.Level,
                Proficiency = weapon.Proficiency,
                KillCount = weapon.KillCount
            });
        }

        foreach (var slot in view.Materials)
        {
            materials.Add(new LegacyInventorySlot
            {
                ItemId = slot.ItemId,
                Quantity = slot.Quantity,
                SlotIndex = slot.SlotIndex
            });
        }
    }

    private static void ApplyShopView(
        List<LegacyShopPurchaseCount> purchases,
        ShopView view,
        out long copper,
        out long silk,
        out long gold)
    {
        foreach (var purchase in view.Purchases)
        {
            purchases.Add(new LegacyShopPurchaseCount
            {
                ProductId = purchase.ProductId,
                PurchasedTotal = purchase.PurchasedTotal
            });
        }

        copper = view.Balances.Get("Copper");
        silk = view.Balances.Get("Silk");
        gold = view.Balances.Get("Gold");
    }

    private static void ApplySnapshot(
        List<LegacyInventorySlot> slots,
        List<LegacyEquipmentSlot> equipment,
        InventorySnapshot snapshot)
    {
        foreach (var slot in snapshot.Slots)
        {
            slots.Add(new LegacyInventorySlot
            {
                ItemId = slot.ItemId,
                Quantity = slot.Quantity,
                SlotIndex = slot.SlotIndex
            });
        }

        foreach (var slot in snapshot.Equipment)
        {
            equipment.Add(new LegacyEquipmentSlot
            {
                SlotKind = slot.SlotKind,
                SlotIndex = slot.SlotIndex,
                ItemId = slot.ItemId
            });
        }
    }

    private static void ApplyProfile(LegacyMsgLobbyProfileResponse response, AccountProfileView view)
    {
        response.AvatarId = view.AvatarId;
        response.AvatarFrameId = view.AvatarFrameId;
        response.SelectedHeroId = view.SelectedHeroId;
        response.SelectedWeaponId = view.SelectedWeaponId;
        response.SelectedPetId = view.SelectedPetId;
        response.AccountXp = view.AccountXp;
        response.AccountLevel = view.AccountLevel;
        response.InventoryTier = view.InventoryTier;
        response.Copper = view.Copper;
        response.Silk = view.Silk;
        response.Gold = view.Gold;
    }

    /// <summary>
    /// 每个 P1 业务请求的统一入口检查：必须携带 RequestId，且必须来自已认证连接。
    /// 账号身份只来自连接会话，请求体里根本没有 AccountId 字段可供伪造。
    /// </summary>
    private bool TryResolveAccount(
        LegacyClientConnection client,
        string? requestId,
        out long accountId,
        out LegacyLobbyOperationStatus failure)
    {
        accountId = 0;
        if (string.IsNullOrWhiteSpace(requestId) || requestId.Length > MaximumRequestIdLength)
        {
            failure = LegacyLobbyOperationStatus.InvalidRequest;
            return false;
        }

        try
        {
            accountId = sessionAccounts.Resolve(client.ConnectionId);
            failure = LegacyLobbyOperationStatus.Success;
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            failure = LegacyLobbyOperationStatus.Unauthenticated;
            return false;
        }
    }

    private static LegacyLobbyOperationStatus ToWireStatus(LobbyOperationStatus status) =>
        (LegacyLobbyOperationStatus)(int)status;

    private static void ValidateEmbeddedProtocol(string protocolName, LegacyProtocolValue protocolType)
    {
        if (LegacyProtocolCatalog.Client.TryGetValue(protocolName, out var expected) && expected == (int)protocolType)
        {
            return;
        }

        if (ApplicationProtocolCatalog.TryGetInbound(protocolName, out var applicationExpected) &&
            applicationExpected == (int)protocolType)
        {
            return;
        }

        throw new InvalidDataException(
            $"Legacy protocol envelope mismatch: name={protocolName}, value={(int)protocolType}.");
    }

    private static void Send(LegacyClientConnection client, LegacyMessage message, string key)
    {
        var messageTypeName = message.GetType().Name.Replace("Legacy", string.Empty, StringComparison.Ordinal);
        var outbound = LegacyOutboundProtocolResolver.Resolve(messageTypeName);
        message.ProtocolType = (LegacyProtocolValue)outbound.EmbeddedProtocolValue;
        var protobuf = LegacyProtobufCodec.Serialize(message);
        var ciphertext = LegacyAesCodec.Encrypt(protobuf, key);
        client.Send(LegacyFrameCodec.Encode(outbound.WireProtocolName, ciphertext));
    }

    private void CloseClient(LegacyClientConnection client, ICollection<LegacyClientConnection> clients)
    {
        loginSessions.Disconnect(client.ConnectionId);
        clients.Remove(client);
        CloseSocket(client.Socket);
        Volatile.Write(ref _connectedClientCount, clients.Count);
    }

    private static void CloseSocket(Socket? socket)
    {
        if (socket is null)
        {
            return;
        }

        try
        {
            socket.Shutdown(SocketShutdown.Both);
        }
        catch (SocketException)
        {
        }
        finally
        {
            socket.Dispose();
        }
    }

    private sealed class LegacyClientConnection(Socket socket)
    {
        private const int ReceiveChunkSize = 8 * 1024;
        private readonly byte[] _receiveChunk = new byte[ReceiveChunkSize];
        private byte[] _buffer = new byte[ReceiveChunkSize];
        private int _bufferedCount;

        public Socket Socket { get; } = socket;
        public ConnectionId ConnectionId { get; } = ConnectionId.New();
        public string? SessionKey { get; set; }
        public DateTime LastHeartbeatUtc { get; set; } = DateTime.UtcNow;

        public int Receive()
        {
            var received = Socket.Receive(_receiveChunk);
            if (received == 0)
            {
                return 0;
            }

            var required = checked(_bufferedCount + received);
            if (required > LegacyFrameCodec.HeaderLength + LegacyFrameCodec.MaximumPayloadLength)
            {
                throw new InvalidDataException("Legacy receive buffer exceeded the maximum frame size.");
            }

            if (required > _buffer.Length)
            {
                Array.Resize(ref _buffer, Math.Min(
                    Math.Max(required, _buffer.Length * 2),
                    LegacyFrameCodec.HeaderLength + LegacyFrameCodec.MaximumPayloadLength));
            }

            _receiveChunk.AsSpan(0, received).CopyTo(_buffer.AsSpan(_bufferedCount));
            _bufferedCount = required;
            return received;
        }

        public bool TryTakeFrame(out LegacyFrame frame)
        {
            if (!LegacyFrameCodec.TryDecode(_buffer.AsSpan(0, _bufferedCount), out var decoded, out var consumed))
            {
                frame = null!;
                return false;
            }

            frame = decoded!;
            _buffer.AsSpan(consumed, _bufferedCount - consumed).CopyTo(_buffer);
            _bufferedCount -= consumed;
            return true;
        }

        public void Send(byte[] packet)
        {
            var sent = 0;
            while (sent < packet.Length)
            {
                var current = Socket.Send(packet, sent, packet.Length - sent, SocketFlags.None);
                if (current == 0)
                {
                    throw new IOException("Legacy socket closed during send.");
                }

                sent += current;
            }
        }
    }
}
