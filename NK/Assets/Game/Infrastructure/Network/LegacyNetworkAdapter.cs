using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Naraka.Core.Application.Networking;
using Naraka.Core.Domain;
using Naraka.Features.Account.Controller;
using Naraka.Features.Achievement.Controller;
using Naraka.Features.Forge.Controller;
using Naraka.Features.Gacha.Controller;
using Naraka.Features.Inventory.Controller;
using Naraka.Features.Inventory.Model;
using Naraka.Features.Loadout.Controller;
using Naraka.Features.RedDot.Controller;
using Naraka.Features.Shop.Controller;
using Naraka.Features.SignIn.Controller;
using Naraka.Features.Social.Controller;
using Naraka.Features.Lobby.Controller;
using Naraka.Features.Lobby.Model;

namespace Naraka.Infrastructure.Network
{
    /// <summary>
    /// Client adapter for the frozen LegacyNetworkV1 wire. It never accesses Unity objects.
    /// </summary>
    public sealed class LegacyNetworkAdapter :
        INetworkFacade,
        IAccountGateway,
        ILobbyAccountGateway,
        ILobbyProfileGateway,
        ILoadoutGateway,
        IInventoryGateway,
        IShopGateway,
        IForgeGateway,
        IGachaGateway,
        ISignInGateway,
        IAchievementGateway,
        IRedDotGateway,
        ISocialGateway,
        IDisposable
    {
        public const string PublicHandshakeKey = "abc123";
        public const int HeartbeatIntervalSeconds = 300;

        private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(10);
        private readonly string _host;
        private readonly int _port;
        private readonly object _socketLock = new object();
        private readonly object _sendLock = new object();
        private readonly SemaphoreSlim _connectGate = new SemaphoreSlim(1, 1);
        private readonly ConcurrentDictionary<LegacyProtocolValue, TaskCompletionSource<LegacyMessage>> _pending =
            new ConcurrentDictionary<LegacyProtocolValue, TaskCompletionSource<LegacyMessage>>();
        private readonly ManualResetEventSlim _stop = new ManualResetEventSlim(false);
        private Socket _socket;
        private Thread _receiveThread;
        private Thread _heartbeatThread;
        private TaskCompletionSource<LegacyMsgSecret> _handshake;
        private byte[] _receiveBuffer = new byte[8 * 1024];
        private int _receiveCount;
        private string _sessionKey = string.Empty;
        private DateTime _lastPingUtc;
        private bool _disposed;

        public LegacyNetworkAdapter(string host, int port)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                throw new ArgumentException("Legacy server host cannot be empty.", nameof(host));
            }

            if (port <= 0 || port > ushort.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(port));
            }

            _host = host;
            _port = port;
        }

        public async UniTask<AccountRegistrationResult> RegisterAsync(
            string username,
            string password,
            CancellationToken cancellationToken)
        {
            var response = await SendRequestAsync<LegacyMsgRegister>(
                new LegacyMsgRegister { Account = username, Password = password },
                DefaultRequestTimeout,
                cancellationToken);

            var status = response.Result switch
            {
                LegacyRegisterResult.Success => AccountRegistrationStatus.Success,
                LegacyRegisterResult.AlreadyExist => AccountRegistrationStatus.AlreadyExists,
                LegacyRegisterResult.Forbidden => AccountRegistrationStatus.Forbidden,
                _ => AccountRegistrationStatus.Failed
            };
            return new AccountRegistrationResult(status);
        }

        public async UniTask<AccountLoginResult> LoginAsync(
            string username,
            string password,
            CancellationToken cancellationToken)
        {
            var response = await SendRequestAsync<LegacyMsgLogin>(
                new LegacyMsgLogin { Account = username, Password = password },
                DefaultRequestTimeout,
                cancellationToken);

            var status = response.Result switch
            {
                LegacyLoginResult.Success => AccountLoginStatus.Success,
                LegacyLoginResult.WrongPwd => AccountLoginStatus.WrongPassword,
                LegacyLoginResult.UserNotExist => AccountLoginStatus.UserNotFound,
                LegacyLoginResult.TimeoutToken => AccountLoginStatus.Timeout,
                _ => AccountLoginStatus.Failed
            };
            return new AccountLoginResult(status, response.AccountId);
        }

        /// <summary>
        /// 请求当前已认证连接的大厅账号概要。刻意不发送 accountId：服务端从连接会话决定读谁的数据。
        /// </summary>
        public async UniTask<LobbyAccountSummaryResult> RequestAccountSummaryAsync(
            CancellationToken cancellationToken)
        {
            LegacyMsgLobbyAccountSummaryResponse response;
            try
            {
                response = await SendRequestAsync<LegacyMsgLobbyAccountSummaryRequest, LegacyMsgLobbyAccountSummaryResponse>(
                    new LegacyMsgLobbyAccountSummaryRequest { RequestId = NewRequestId() },
                    LegacyProtocolValue.MsgLobbyAccountSummaryResponse,
                    DefaultRequestTimeout,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // 连接失败、超时或解码失败都归为传输失败，不伪装成业务错误码。
                return LobbyAccountSummaryResult.Failed(LobbyAccountSummaryStatus.TransportFailure);
            }

            if (response.Status != LegacyLobbyAccountSummaryStatus.Success)
            {
                return LobbyAccountSummaryResult.Failed(ToSummaryStatus(response.Status));
            }

            // 服务端已经校验过，这里再校验一次：非法数据绝不进入业务层。
            return LobbyAccountSnapshot.TryCreate(
                response.AccountLevel,
                response.Copper,
                response.Silk,
                response.Gold,
                out var snapshot)
                ? LobbyAccountSummaryResult.Success(snapshot)
                : LobbyAccountSummaryResult.Failed(LobbyAccountSummaryStatus.InternalError);
        }

        /// <summary>
        /// 读取账号资料。与账号概要一样不发送 accountId：服务端从已认证连接会话决定读谁的数据。
        /// </summary>
        public async UniTask<LobbyProfileResult> RequestProfileAsync(CancellationToken cancellationToken)
        {
            LegacyMsgLobbyProfileResponse response;
            try
            {
                response = await SendRequestAsync<LegacyMsgLobbyProfileRequest, LegacyMsgLobbyProfileResponse>(
                    new LegacyMsgLobbyProfileRequest { RequestId = NewRequestId() },
                    LegacyProtocolValue.MsgLobbyProfileResponse,
                    DefaultRequestTimeout,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return LobbyProfileResult.Failed(LobbyOperationStatus.TransportFailure);
            }

            return ToProfileResult(response);
        }

        public async UniTask<LobbyProfileResult> SetAppearanceAsync(
            string avatarId,
            string avatarFrameId,
            CancellationToken cancellationToken)
        {
            LegacyMsgLobbySetAppearanceResponse response;
            try
            {
                response = await SendRequestAsync<
                    LegacyMsgLobbySetAppearanceRequest, LegacyMsgLobbySetAppearanceResponse>(
                    new LegacyMsgLobbySetAppearanceRequest
                    {
                        RequestId = NewRequestId(),
                        AvatarId = avatarId,
                        AvatarFrameId = avatarFrameId
                    },
                    LegacyProtocolValue.MsgLobbySetAppearanceResponse,
                    DefaultRequestTimeout,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return LobbyProfileResult.Failed(LobbyOperationStatus.TransportFailure);
            }

            if (response.Status != LegacyLobbyOperationStatus.Success)
            {
                return LobbyProfileResult.Failed(ToOperationStatus(response.Status));
            }

            // 修改成功后立刻重新读取完整资料：修改响应只回传外观两项，
            // 而界面需要的是与服务端完全一致的整份资料。
            return await RequestProfileAsync(cancellationToken);
        }

        /// <summary>
        /// 提交出战英雄、兵器与宠物。三个都是稳定配置 ID，由服务端对照配置校验。
        /// </summary>
        public async UniTask<LobbyProfileResult> SetLoadoutAsync(
            string heroId,
            string weaponId,
            string petId,
            CancellationToken cancellationToken)
        {
            LegacyMsgLobbySetLoadoutResponse response;
            try
            {
                response = await SendRequestAsync<
                    LegacyMsgLobbySetLoadoutRequest, LegacyMsgLobbySetLoadoutResponse>(
                    new LegacyMsgLobbySetLoadoutRequest
                    {
                        RequestId = NewRequestId(),
                        HeroId = heroId,
                        WeaponId = weaponId,
                        PetId = petId
                    },
                    LegacyProtocolValue.MsgLobbySetLoadoutResponse,
                    DefaultRequestTimeout,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return LobbyProfileResult.Failed(LobbyOperationStatus.TransportFailure);
            }

            if (response.Status != LegacyLobbyOperationStatus.Success)
            {
                return LobbyProfileResult.Failed(ToOperationStatus(response.Status));
            }

            // 修改响应只回传三个 ID；界面需要的是与服务端完全一致的整份资料。
            return await RequestProfileAsync(cancellationToken);
        }

        public async UniTask<InventoryResult> RequestInventoryAsync(CancellationToken cancellationToken)
        {
            LegacyMsgLobbyInventoryResponse response;
            try
            {
                response = await SendRequestAsync<LegacyMsgLobbyInventoryRequest, LegacyMsgLobbyInventoryResponse>(
                    new LegacyMsgLobbyInventoryRequest { RequestId = NewRequestId() },
                    LegacyProtocolValue.MsgLobbyInventoryResponse,
                    DefaultRequestTimeout,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return InventoryResult.Failed(LobbyOperationStatus.TransportFailure);
            }

            if (response.Status != LegacyLobbyOperationStatus.Success)
            {
                return InventoryResult.Failed(ToOperationStatus(response.Status));
            }

            return ToInventorySnapshot(
                response.Slots, response.Equipment, response.Capacity, response.Tier, out var snapshot)
                ? InventoryResult.Success(snapshot)
                : InventoryResult.Failed(LobbyOperationStatus.InternalError);
        }

        public async UniTask<InventoryResult> MutateAsync(
            InventoryOperation operation,
            string itemId,
            long quantity,
            string slotKind,
            int slotIndex,
            IReadOnlyList<string> itemOrder,
            CancellationToken cancellationToken)
        {
            var request = new LegacyMsgLobbyInventoryMutateRequest
            {
                RequestId = NewRequestId(),
                Operation = (LegacyInventoryOperation)(int)operation,
                ItemId = itemId,
                Quantity = quantity,
                SlotKind = slotKind,
                SlotIndex = slotIndex
            };

            if (itemOrder != null)
            {
                foreach (var entry in itemOrder)
                {
                    request.ItemOrder.Add(entry);
                }
            }

            LegacyMsgLobbyInventoryMutateResponse response;
            try
            {
                response = await SendRequestAsync<
                    LegacyMsgLobbyInventoryMutateRequest, LegacyMsgLobbyInventoryMutateResponse>(
                    request,
                    LegacyProtocolValue.MsgLobbyInventoryMutateResponse,
                    DefaultRequestTimeout,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return InventoryResult.Failed(LobbyOperationStatus.TransportFailure);
            }

            if (response.Status != LegacyLobbyOperationStatus.Success)
            {
                return InventoryResult.Failed(ToOperationStatus(response.Status));
            }

            if (!ToInventorySnapshot(
                    response.Slots, response.Equipment, response.Capacity, response.Tier, out var snapshot))
            {
                return InventoryResult.Failed(LobbyOperationStatus.InternalError);
            }

            return response.HasBalances
                ? InventoryResult.SuccessWithBalances(
                    snapshot, response.Copper, response.Silk, response.Gold)
                : InventoryResult.Success(snapshot);
        }

        /// <summary>
        /// 服务端已经校验过，这里再校验一次：负数量、重复物品或越界槽位绝不进入业务层。
        /// </summary>
        private static bool ToInventorySnapshot(
            List<LegacyInventorySlot> slots,
            List<LegacyEquipmentSlot> equipment,
            int capacity,
            int tier,
            out InventorySnapshot snapshot)
        {
            var mappedSlots = new List<InventorySlotSnapshot>(slots.Count);
            foreach (var slot in slots)
            {
                mappedSlots.Add(new InventorySlotSnapshot(slot.ItemId, slot.Quantity, slot.SlotIndex));
            }

            var mappedEquipment = new List<EquipmentSlotSnapshot>(equipment.Count);
            foreach (var slot in equipment)
            {
                mappedEquipment.Add(new EquipmentSlotSnapshot(slot.SlotKind, slot.SlotIndex, slot.ItemId));
            }

            return InventorySnapshot.TryCreate(mappedSlots, mappedEquipment, capacity, tier, out snapshot);
        }

        public async UniTask<ShopResult> RequestShopAsync(CancellationToken cancellationToken)
        {
            LegacyMsgLobbyShopResponse response;
            try
            {
                response = await SendRequestAsync<LegacyMsgLobbyShopRequest, LegacyMsgLobbyShopResponse>(
                    new LegacyMsgLobbyShopRequest { RequestId = NewRequestId() },
                    LegacyProtocolValue.MsgLobbyShopResponse,
                    DefaultRequestTimeout,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return ShopResult.Failed(LobbyOperationStatus.TransportFailure);
            }

            return response.Status != LegacyLobbyOperationStatus.Success
                ? ShopResult.Failed(ToOperationStatus(response.Status))
                : ShopResult.Success(
                    ToPurchaseCounts(response.Purchases), response.Copper, response.Silk, response.Gold);
        }

        /// <summary>
        /// 购买。总价由服务端按配置计算；这里只提交商品 ID、数量与订单号，
        /// 同一个订单号重复提交只会扣一次费。
        /// </summary>
        public async UniTask<ShopResult> PurchaseAsync(
            string productId,
            int quantity,
            string orderId,
            CancellationToken cancellationToken)
        {
            LegacyMsgLobbyShopPurchaseResponse response;
            try
            {
                response = await SendRequestAsync<
                    LegacyMsgLobbyShopPurchaseRequest, LegacyMsgLobbyShopPurchaseResponse>(
                    new LegacyMsgLobbyShopPurchaseRequest
                    {
                        RequestId = orderId,
                        ProductId = productId,
                        Quantity = quantity
                    },
                    LegacyProtocolValue.MsgLobbyShopPurchaseResponse,
                    DefaultRequestTimeout,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return ShopResult.Failed(LobbyOperationStatus.TransportFailure);
            }

            return response.Status != LegacyLobbyOperationStatus.Success
                ? ShopResult.Failed(ToOperationStatus(response.Status))
                : ShopResult.Success(
                    ToPurchaseCounts(response.Purchases), response.Copper, response.Silk, response.Gold);
        }

        private static IReadOnlyList<ShopPurchaseCount> ToPurchaseCounts(
            List<LegacyShopPurchaseCount> purchases)
        {
            var mapped = new List<ShopPurchaseCount>(purchases.Count);
            foreach (var purchase in purchases)
            {
                // 负计数说明服务端或解码出了问题；直接丢掉而不是把它显示成限购剩余量。
                if (!string.IsNullOrEmpty(purchase.ProductId) && purchase.PurchasedTotal >= 0)
                {
                    mapped.Add(new ShopPurchaseCount(purchase.ProductId, purchase.PurchasedTotal));
                }
            }

            return mapped;
        }

        public async UniTask<ForgeResult> RequestForgeAsync(CancellationToken cancellationToken)
        {
            LegacyMsgLobbyForgeResponse response;
            try
            {
                response = await SendRequestAsync<LegacyMsgLobbyForgeRequest, LegacyMsgLobbyForgeResponse>(
                    new LegacyMsgLobbyForgeRequest { RequestId = NewRequestId() },
                    LegacyProtocolValue.MsgLobbyForgeResponse,
                    DefaultRequestTimeout,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return ForgeResult.Failed(LobbyOperationStatus.TransportFailure);
            }

            return response.Status != LegacyLobbyOperationStatus.Success
                ? ForgeResult.Failed(ToOperationStatus(response.Status))
                : ForgeResult.Success(
                    ToWeapons(response.Weapons), ToMaterials(response.Materials),
                    response.Copper, response.Silk, response.Gold);
        }

        public async UniTask<ForgeResult> UpgradeAsync(
            string weaponId,
            int expectedLevel,
            string requestId,
            CancellationToken cancellationToken)
        {
            LegacyMsgLobbyForgeUpgradeResponse response;
            try
            {
                response = await SendRequestAsync<
                    LegacyMsgLobbyForgeUpgradeRequest, LegacyMsgLobbyForgeUpgradeResponse>(
                    new LegacyMsgLobbyForgeUpgradeRequest
                    {
                        RequestId = requestId,
                        WeaponId = weaponId,
                        ExpectedLevel = expectedLevel
                    },
                    LegacyProtocolValue.MsgLobbyForgeUpgradeResponse,
                    DefaultRequestTimeout,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return ForgeResult.Failed(LobbyOperationStatus.TransportFailure);
            }

            return response.Status != LegacyLobbyOperationStatus.Success
                ? ForgeResult.Failed(ToOperationStatus(response.Status))
                : ForgeResult.Success(
                    ToWeapons(response.Weapons), ToMaterials(response.Materials),
                    response.Copper, response.Silk, response.Gold);
        }

        /// <summary>非法等级或空 ID 直接丢弃，绝不让它们进入业务层显示成一件真实武器。</summary>
        private static IReadOnlyList<AccountWeaponSnapshot> ToWeapons(List<LegacyAccountWeapon> weapons)
        {
            var mapped = new List<AccountWeaponSnapshot>(weapons.Count);
            foreach (var weapon in weapons)
            {
                if (!string.IsNullOrEmpty(weapon.WeaponId) && weapon.Level >= 1 &&
                    weapon.Proficiency >= 0 && weapon.KillCount >= 0)
                {
                    mapped.Add(new AccountWeaponSnapshot(
                        weapon.WeaponId, weapon.Level, weapon.Proficiency, weapon.KillCount));
                }
            }

            return mapped;
        }

        private static IReadOnlyList<ForgeMaterialSnapshot> ToMaterials(List<LegacyInventorySlot> slots)
        {
            var mapped = new List<ForgeMaterialSnapshot>(slots.Count);
            foreach (var slot in slots)
            {
                if (!string.IsNullOrEmpty(slot.ItemId) && slot.Quantity > 0)
                {
                    mapped.Add(new ForgeMaterialSnapshot(slot.ItemId, slot.Quantity));
                }
            }

            return mapped;
        }

        public async UniTask<GachaResult> RequestGachaAsync(string poolId, CancellationToken cancellationToken)
        {
            LegacyMsgLobbyGachaResponse response;
            try
            {
                response = await SendRequestAsync<LegacyMsgLobbyGachaRequest, LegacyMsgLobbyGachaResponse>(
                    new LegacyMsgLobbyGachaRequest { RequestId = NewRequestId(), PoolId = poolId },
                    LegacyProtocolValue.MsgLobbyGachaResponse,
                    DefaultRequestTimeout,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return GachaResult.Failed(LobbyOperationStatus.TransportFailure);
            }

            return response.Status != LegacyLobbyOperationStatus.Success
                ? GachaResult.Failed(ToOperationStatus(response.Status))
                : GachaResult.Success(
                    response.PoolId, response.PityCounter, response.TotalPulls,
                    ToOrders(response.UnshownOrders), null,
                    response.Copper, response.Silk, response.Gold);
        }

        public async UniTask<GachaResult> PullAsync(
            string poolId,
            int pullCount,
            string orderId,
            CancellationToken cancellationToken)
        {
            LegacyMsgLobbyGachaPullResponse response;
            try
            {
                response = await SendRequestAsync<
                    LegacyMsgLobbyGachaPullRequest, LegacyMsgLobbyGachaPullResponse>(
                    new LegacyMsgLobbyGachaPullRequest
                    {
                        RequestId = orderId,
                        PoolId = poolId,
                        PullCount = pullCount
                    },
                    LegacyProtocolValue.MsgLobbyGachaPullResponse,
                    DefaultRequestTimeout,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return GachaResult.Failed(LobbyOperationStatus.TransportFailure);
            }

            if (response.Status != LegacyLobbyOperationStatus.Success)
            {
                return GachaResult.Failed(ToOperationStatus(response.Status));
            }

            var order = response.Order == null ? null : ToOrder(response.Order);
            return GachaResult.Success(
                poolId, response.PityCounter, response.TotalPulls,
                Array.Empty<GachaOrderSnapshot>(), order,
                response.Copper, response.Silk, response.Gold);
        }

        public async UniTask<GachaResult> AcknowledgeAsync(
            string poolId,
            string orderId,
            CancellationToken cancellationToken)
        {
            LegacyMsgLobbyGachaAcknowledgeResponse response;
            try
            {
                response = await SendRequestAsync<
                    LegacyMsgLobbyGachaAcknowledgeRequest, LegacyMsgLobbyGachaAcknowledgeResponse>(
                    new LegacyMsgLobbyGachaAcknowledgeRequest
                    {
                        RequestId = NewRequestId(),
                        PoolId = poolId,
                        OrderId = orderId
                    },
                    LegacyProtocolValue.MsgLobbyGachaAcknowledgeResponse,
                    DefaultRequestTimeout,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return GachaResult.Failed(LobbyOperationStatus.TransportFailure);
            }

            return response.Status != LegacyLobbyOperationStatus.Success
                ? GachaResult.Failed(ToOperationStatus(response.Status))
                : GachaResult.Success(
                    poolId, response.PityCounter, 0,
                    ToOrders(response.UnshownOrders), null, 0, 0, 0);
        }

        public async UniTask<SignInResult> RequestSignInAsync(CancellationToken cancellationToken)
        {
            LegacyMsgLobbySignInResponse response;
            try
            {
                response = await SendRequestAsync<LegacyMsgLobbySignInRequest, LegacyMsgLobbySignInResponse>(
                    new LegacyMsgLobbySignInRequest { RequestId = NewRequestId() },
                    LegacyProtocolValue.MsgLobbySignInResponse,
                    DefaultRequestTimeout,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return SignInResult.Failed(LobbyOperationStatus.TransportFailure);
            }

            return ToSignInResult(
                response.Status, response.CycleStartDay, response.ServerDay, response.ConsecutiveDays,
                response.MakeupCardCount, response.Claims, response.ClaimedMilestones);
        }

        public UniTask<SignInResult> ClaimTodayAsync(CancellationToken cancellationToken) =>
            ClaimSignInAsync(LegacySignInClaimKind.Today, 0, cancellationToken);

        public UniTask<SignInResult> MakeUpAsync(int dayIndex, CancellationToken cancellationToken) =>
            ClaimSignInAsync(LegacySignInClaimKind.MakeUp, dayIndex, cancellationToken);

        public UniTask<SignInResult> ClaimMilestoneAsync(
            int milestoneDays,
            CancellationToken cancellationToken) =>
            ClaimSignInAsync(LegacySignInClaimKind.Milestone, milestoneDays, cancellationToken);

        private async UniTask<SignInResult> ClaimSignInAsync(
            LegacySignInClaimKind kind,
            int target,
            CancellationToken cancellationToken)
        {
            LegacyMsgLobbySignInClaimResponse response;
            try
            {
                response = await SendRequestAsync<
                    LegacyMsgLobbySignInClaimRequest, LegacyMsgLobbySignInClaimResponse>(
                    new LegacyMsgLobbySignInClaimRequest
                    {
                        RequestId = NewRequestId(),
                        Kind = kind,
                        Target = target
                    },
                    LegacyProtocolValue.MsgLobbySignInClaimResponse,
                    DefaultRequestTimeout,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return SignInResult.Failed(LobbyOperationStatus.TransportFailure);
            }

            return ToSignInResult(
                response.Status, response.CycleStartDay, response.ServerDay, response.ConsecutiveDays,
                response.MakeupCardCount, response.Claims, response.ClaimedMilestones);
        }

        private static SignInResult ToSignInResult(
            LegacyLobbyOperationStatus status,
            long cycleStartDay,
            long serverDay,
            int consecutiveDays,
            long makeupCardCount,
            List<LegacySignInClaim> claims,
            List<string> claimedMilestones)
        {
            if (status != LegacyLobbyOperationStatus.Success)
            {
                return SignInResult.Failed(ToOperationStatus(status));
            }

            var records = new List<SignInClaimRecord>(claims.Count);
            foreach (var claim in claims)
            {
                records.Add(new SignInClaimRecord(claim.DayIndex, claim.IsMakeup));
            }

            return SignInResult.Success(
                cycleStartDay, serverDay, consecutiveDays, makeupCardCount,
                records, claimedMilestones.ToArray());
        }

        public async UniTask<AchievementResult> RequestAchievementsAsync(CancellationToken cancellationToken)
        {
            LegacyMsgLobbyAchievementResponse response;
            try
            {
                response = await SendRequestAsync<
                    LegacyMsgLobbyAchievementRequest, LegacyMsgLobbyAchievementResponse>(
                    new LegacyMsgLobbyAchievementRequest { RequestId = NewRequestId() },
                    LegacyProtocolValue.MsgLobbyAchievementResponse,
                    DefaultRequestTimeout,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return AchievementResult.Failed(LobbyOperationStatus.TransportFailure);
            }

            return ToAchievementResult(
                response.Status, response.AccountXp, response.AccountLevel, response.AchievementXp,
                response.AchievementLevel, response.Progress, response.ClaimedAchievements,
                response.ClaimedAccountLevels);
        }

        public UniTask<AchievementResult> ClaimAchievementAsync(
            string achievementId,
            CancellationToken cancellationToken) =>
            ClaimAchievementInternalAsync(
                LegacyAchievementClaimKind.Achievement, achievementId, 0, cancellationToken);

        public UniTask<AchievementResult> ClaimAccountLevelRewardAsync(
            int level,
            CancellationToken cancellationToken) =>
            ClaimAchievementInternalAsync(
                LegacyAchievementClaimKind.AccountLevel, null, level, cancellationToken);

        private async UniTask<AchievementResult> ClaimAchievementInternalAsync(
            LegacyAchievementClaimKind kind,
            string achievementId,
            int level,
            CancellationToken cancellationToken)
        {
            LegacyMsgLobbyAchievementClaimResponse response;
            try
            {
                response = await SendRequestAsync<
                    LegacyMsgLobbyAchievementClaimRequest, LegacyMsgLobbyAchievementClaimResponse>(
                    new LegacyMsgLobbyAchievementClaimRequest
                    {
                        RequestId = NewRequestId(),
                        Kind = kind,
                        AchievementId = achievementId,
                        AccountLevel = level
                    },
                    LegacyProtocolValue.MsgLobbyAchievementClaimResponse,
                    DefaultRequestTimeout,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return AchievementResult.Failed(LobbyOperationStatus.TransportFailure);
            }

            return ToAchievementResult(
                response.Status, response.AccountXp, response.AccountLevel, response.AchievementXp,
                response.AchievementLevel, response.Progress, response.ClaimedAchievements,
                response.ClaimedAccountLevels);
        }

        private static AchievementResult ToAchievementResult(
            LegacyLobbyOperationStatus status,
            long accountXp,
            int accountLevel,
            long achievementXp,
            int achievementLevel,
            List<LegacyAchievementProgress> progress,
            List<string> claimedAchievements,
            List<string> claimedAccountLevels)
        {
            if (status != LegacyLobbyOperationStatus.Success)
            {
                return AchievementResult.Failed(ToOperationStatus(status));
            }

            var records = new List<AchievementProgressRecord>(progress.Count);
            foreach (var entry in progress)
            {
                records.Add(new AchievementProgressRecord(entry.AchievementId, entry.Progress));
            }

            return AchievementResult.Success(
                accountXp, accountLevel, achievementXp, achievementLevel,
                records, claimedAchievements.ToArray(), claimedAccountLevels.ToArray());
        }

        async UniTask<IReadOnlyList<RedDotStateRecord>> IRedDotGateway.LoadAsync(
            CancellationToken cancellationToken)
        {
            var response = await SendRequestAsync<LegacyMsgLobbyRedDotRequest, LegacyMsgLobbyRedDotResponse>(
                new LegacyMsgLobbyRedDotRequest { RequestId = NewRequestId() },
                LegacyProtocolValue.MsgLobbyRedDotResponse,
                DefaultRequestTimeout,
                cancellationToken);

            if (response.Status != LegacyLobbyOperationStatus.Success)
            {
                return Array.Empty<RedDotStateRecord>();
            }

            var records = new List<RedDotStateRecord>(response.Nodes.Count);
            foreach (var node in response.Nodes)
            {
                if (!string.IsNullOrEmpty(node.Path))
                {
                    records.Add(new RedDotStateRecord(node.Path, node.Version, node.SeenVersion));
                }
            }

            return records;
        }

        async UniTask IRedDotGateway.SaveSeenAsync(
            string path,
            long seenVersion,
            CancellationToken cancellationToken)
        {
            await SendRequestAsync<LegacyMsgLobbyRedDotSeenRequest, LegacyMsgLobbyRedDotSeenResponse>(
                new LegacyMsgLobbyRedDotSeenRequest
                {
                    RequestId = NewRequestId(),
                    Path = path,
                    SeenVersion = seenVersion
                },
                LegacyProtocolValue.MsgLobbyRedDotSeenResponse,
                DefaultRequestTimeout,
                cancellationToken);
        }

        public async UniTask<Naraka.Features.Social.Controller.SocialResult> RequestSocialAsync(
            CancellationToken cancellationToken)
        {
            LegacyMsgLobbySocialResponse response;
            try
            {
                response = await SendRequestAsync<LegacyMsgLobbySocialRequest, LegacyMsgLobbySocialResponse>(
                    new LegacyMsgLobbySocialRequest { RequestId = NewRequestId() },
                    LegacyProtocolValue.MsgLobbySocialResponse,
                    DefaultRequestTimeout,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return Naraka.Features.Social.Controller.SocialResult.Failed(
                    LobbyOperationStatus.TransportFailure);
            }

            return response.Status != LegacyLobbyOperationStatus.Success
                ? Naraka.Features.Social.Controller.SocialResult.Failed(ToOperationStatus(response.Status))
                : Naraka.Features.Social.Controller.SocialResult.Success(
                    ToPlayers(response.Friends),
                    ToPlayers(response.IncomingRequests),
                    ToPlayers(response.Blocked),
                    ToConversations(response.Conversations));
        }

        public async UniTask<Naraka.Features.Social.Controller.SocialResult> SearchAsync(
            string displayName,
            CancellationToken cancellationToken)
        {
            LegacyMsgLobbySocialSearchResponse response;
            try
            {
                response = await SendRequestAsync<
                    LegacyMsgLobbySocialSearchRequest, LegacyMsgLobbySocialSearchResponse>(
                    new LegacyMsgLobbySocialSearchRequest
                    {
                        RequestId = NewRequestId(),
                        DisplayName = displayName
                    },
                    LegacyProtocolValue.MsgLobbySocialSearchResponse,
                    DefaultRequestTimeout,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return Naraka.Features.Social.Controller.SocialResult.Failed(
                    LobbyOperationStatus.TransportFailure);
            }

            return response.Status != LegacyLobbyOperationStatus.Success
                ? Naraka.Features.Social.Controller.SocialResult.Failed(ToOperationStatus(response.Status))
                : Naraka.Features.Social.Controller.SocialResult.Success(
                    ToPlayers(response.Friends),
                    ToPlayers(response.IncomingRequests),
                    ToPlayers(response.Blocked),
                    ToConversations(response.Conversations),
                    response.Found == null ? default : ToPlayer(response.Found));
        }

        public async UniTask<Naraka.Features.Social.Controller.SocialResult> ActAsync(
            SocialAction action,
            long targetAccountId,
            CancellationToken cancellationToken)
        {
            LegacyMsgLobbySocialActionResponse response;
            try
            {
                response = await SendRequestAsync<
                    LegacyMsgLobbySocialActionRequest, LegacyMsgLobbySocialActionResponse>(
                    new LegacyMsgLobbySocialActionRequest
                    {
                        RequestId = NewRequestId(),
                        Action = (LegacySocialAction)action,
                        TargetAccountId = targetAccountId
                    },
                    LegacyProtocolValue.MsgLobbySocialActionResponse,
                    DefaultRequestTimeout,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return Naraka.Features.Social.Controller.SocialResult.Failed(
                    LobbyOperationStatus.TransportFailure);
            }

            return response.Status != LegacyLobbyOperationStatus.Success
                ? Naraka.Features.Social.Controller.SocialResult.Failed(ToOperationStatus(response.Status))
                : Naraka.Features.Social.Controller.SocialResult.Success(
                    ToPlayers(response.Friends),
                    ToPlayers(response.IncomingRequests),
                    ToPlayers(response.Blocked),
                    ToConversations(response.Conversations));
        }

        public UniTask<Naraka.Features.Social.Controller.SocialResult> OpenConversationAsync(
            long peerAccountId,
            CancellationToken cancellationToken) =>
            ChatAsync(
                LegacyChatAction.OpenConversation, peerAccountId, null, 0, 0, cancellationToken);

        public UniTask<Naraka.Features.Social.Controller.SocialResult> SendMessageAsync(
            long peerAccountId,
            string body,
            CancellationToken cancellationToken) =>
            ChatAsync(LegacyChatAction.SendMessage, peerAccountId, body, 0, 0, cancellationToken);

        public UniTask<Naraka.Features.Social.Controller.SocialResult> MarkReadAsync(
            long conversationId,
            long lastReadMessageId,
            CancellationToken cancellationToken) =>
            ChatAsync(
                LegacyChatAction.MarkRead, 0, null, conversationId, lastReadMessageId, cancellationToken);

        private async UniTask<Naraka.Features.Social.Controller.SocialResult> ChatAsync(
            LegacyChatAction action,
            long peerAccountId,
            string body,
            long conversationId,
            long lastReadMessageId,
            CancellationToken cancellationToken)
        {
            LegacyMsgLobbyChatResponse response;
            try
            {
                response = await SendRequestAsync<LegacyMsgLobbyChatRequest, LegacyMsgLobbyChatResponse>(
                    new LegacyMsgLobbyChatRequest
                    {
                        RequestId = NewRequestId(),
                        Action = action,
                        PeerAccountId = peerAccountId,
                        Body = body,
                        ConversationId = conversationId,
                        LastReadMessageId = lastReadMessageId
                    },
                    LegacyProtocolValue.MsgLobbyChatResponse,
                    DefaultRequestTimeout,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return Naraka.Features.Social.Controller.SocialResult.Failed(
                    LobbyOperationStatus.TransportFailure);
            }

            if (response.Status != LegacyLobbyOperationStatus.Success)
            {
                return Naraka.Features.Social.Controller.SocialResult.Failed(
                    ToOperationStatus(response.Status));
            }

            var messages = new List<ChatMessageSnapshot>(response.Messages.Count);
            foreach (var message in response.Messages)
            {
                messages.Add(new ChatMessageSnapshot(
                    message.MessageId, message.ConversationId, message.SenderAccountId,
                    message.Body, message.SentUnixSeconds));
            }

            return Naraka.Features.Social.Controller.SocialResult.Success(
                ToPlayers(response.Friends),
                ToPlayers(response.IncomingRequests),
                ToPlayers(response.Blocked),
                ToConversations(response.Conversations),
                default,
                response.ConversationId,
                messages);
        }

        private static SocialPlayerSnapshot ToPlayer(LegacySocialPlayer player) =>
            new SocialPlayerSnapshot(
                player.AccountId, player.DisplayName, player.AvatarId, player.IsOnline);

        private static IReadOnlyList<SocialPlayerSnapshot> ToPlayers(List<LegacySocialPlayer> players)
        {
            var mapped = new List<SocialPlayerSnapshot>(players.Count);
            foreach (var player in players)
            {
                // 账号号非法的行直接丢弃：显示一个点不动的好友比不显示更糟。
                if (player != null && player.AccountId > 0)
                {
                    mapped.Add(ToPlayer(player));
                }
            }

            return mapped;
        }

        private static IReadOnlyList<ChatConversationSnapshot> ToConversations(
            List<LegacyChatConversation> conversations)
        {
            var mapped = new List<ChatConversationSnapshot>(conversations.Count);
            foreach (var conversation in conversations)
            {
                if (conversation == null || conversation.ConversationId <= 0 || conversation.Peer == null)
                {
                    continue;
                }

                mapped.Add(new ChatConversationSnapshot(
                    conversation.ConversationId,
                    ToPlayer(conversation.Peer),
                    conversation.LastMessageId,
                    conversation.LastReadMessageId));
            }

            return mapped;
        }

        private static IReadOnlyList<GachaOrderSnapshot> ToOrders(List<LegacyGachaOrder> orders)
        {
            var mapped = new List<GachaOrderSnapshot>(orders.Count);
            foreach (var order in orders)
            {
                var snapshot = ToOrder(order);
                if (snapshot.IsValid)
                {
                    mapped.Add(snapshot);
                }
            }

            return mapped;
        }

        /// <summary>非法奖励（空 ID 或非正数量）直接丢弃，绝不显示成一个玩家其实没有拿到的奖励。</summary>
        private static GachaOrderSnapshot ToOrder(LegacyGachaOrder order)
        {
            var rewards = new List<GachaRewardSnapshot>(order.Rewards.Count);
            foreach (var reward in order.Rewards)
            {
                if (!string.IsNullOrEmpty(reward.ItemId) && reward.Amount > 0)
                {
                    rewards.Add(new GachaRewardSnapshot(
                        reward.RewardId, reward.ItemId, reward.Amount, reward.Quality));
                }
            }

            return new GachaOrderSnapshot(
                order.OrderId, order.PoolId, order.PullCount, order.IsShown, rewards);
        }

        private static LobbyProfileResult ToProfileResult(LegacyMsgLobbyProfileResponse response)
        {
            if (response.Status != LegacyLobbyOperationStatus.Success)
            {
                return LobbyProfileResult.Failed(ToOperationStatus(response.Status));
            }

            // 服务端已经校验过，这里再校验一次：非法数据绝不进入业务层。
            return LobbyProfileSnapshot.TryCreate(
                response.AvatarId,
                response.AvatarFrameId,
                response.SelectedHeroId,
                response.SelectedWeaponId,
                response.SelectedPetId,
                response.AccountXp,
                response.AccountLevel,
                response.InventoryTier,
                response.Copper,
                response.Silk,
                response.Gold,
                out var snapshot)
                ? LobbyProfileResult.Success(snapshot)
                : LobbyProfileResult.Failed(LobbyOperationStatus.InternalError);
        }

        /// <summary>
        /// 线级状态码到业务状态码的映射。两个枚举数值一一对应，但仍然显式转换：
        /// 未知的新状态码必须落到 InternalError，而不是变成一个业务层不认识的枚举值。
        /// </summary>
        private static LobbyOperationStatus ToOperationStatus(LegacyLobbyOperationStatus status)
        {
            switch (status)
            {
                case LegacyLobbyOperationStatus.Success:
                    return LobbyOperationStatus.Success;
                case LegacyLobbyOperationStatus.Unauthenticated:
                    return LobbyOperationStatus.Unauthenticated;
                case LegacyLobbyOperationStatus.InvalidRequest:
                    return LobbyOperationStatus.InvalidRequest;
                case LegacyLobbyOperationStatus.NotFound:
                    return LobbyOperationStatus.NotFound;
                case LegacyLobbyOperationStatus.DatabaseUnavailable:
                    return LobbyOperationStatus.DatabaseUnavailable;
                case LegacyLobbyOperationStatus.Forbidden:
                    return LobbyOperationStatus.Forbidden;
                case LegacyLobbyOperationStatus.InsufficientCurrency:
                    return LobbyOperationStatus.InsufficientCurrency;
                case LegacyLobbyOperationStatus.InsufficientItems:
                    return LobbyOperationStatus.InsufficientItems;
                case LegacyLobbyOperationStatus.InventoryFull:
                    return LobbyOperationStatus.InventoryFull;
                case LegacyLobbyOperationStatus.LimitReached:
                    return LobbyOperationStatus.LimitReached;
                case LegacyLobbyOperationStatus.AlreadyClaimed:
                    return LobbyOperationStatus.AlreadyClaimed;
                case LegacyLobbyOperationStatus.NotAvailable:
                    return LobbyOperationStatus.NotAvailable;
                case LegacyLobbyOperationStatus.RateLimited:
                    return LobbyOperationStatus.RateLimited;
                case LegacyLobbyOperationStatus.Conflict:
                    return LobbyOperationStatus.Conflict;
                default:
                    return LobbyOperationStatus.InternalError;
            }
        }

        // 每个写/读请求都携带 RequestId，为后续 P1 幂等语义预留。
        private static string NewRequestId() =>
            new RequestId(Guid.NewGuid().ToString("N")).Value;

        private static LobbyAccountSummaryStatus ToSummaryStatus(LegacyLobbyAccountSummaryStatus status)
        {
            switch (status)
            {
                case LegacyLobbyAccountSummaryStatus.Unauthenticated:
                    return LobbyAccountSummaryStatus.Unauthenticated;
                case LegacyLobbyAccountSummaryStatus.InvalidRequest:
                    return LobbyAccountSummaryStatus.InvalidRequest;
                case LegacyLobbyAccountSummaryStatus.NotFound:
                    return LobbyAccountSummaryStatus.NotFound;
                case LegacyLobbyAccountSummaryStatus.DatabaseUnavailable:
                    return LobbyAccountSummaryStatus.DatabaseUnavailable;
                default:
                    return LobbyAccountSummaryStatus.InternalError;
            }
        }

        public UniTask SendAsync<TMessage>(TMessage message, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new NotSupportedException(
                "No LegacyNetworkV1 mapping is registered for " + typeof(TMessage).FullName + ".");
        }

        public UniTask<TResponse> RequestAsync<TRequest, TResponse>(
            TRequest request,
            RequestId requestId,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new NotSupportedException(
                "Use a feature gateway so legacy DTOs do not leak into business controllers.");
        }

        public IObservable<TEvent> Events<TEvent>() => EmptyObservable<TEvent>.Instance;

        public UniTask SendCriticalAsync<TMessage>(TMessage message, CancellationToken cancellationToken) =>
            SendAsync(message, cancellationToken);

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Close(new ObjectDisposedException(nameof(LegacyNetworkAdapter)));
            StopWorkers();
            _connectGate.Dispose();
            _stop.Dispose();
        }

        private UniTask<TMessage> SendRequestAsync<TMessage>(
            TMessage request,
            TimeSpan timeout,
            CancellationToken cancellationToken)
            where TMessage : LegacyMessage =>
            SendRequestAsync<TMessage, TMessage>(request, request.ProtocolType, timeout, cancellationToken);

        /// <summary>
        /// 请求与响应使用不同协议号时，等待队列必须以响应协议号为键。
        /// 冻结的 P0 消息请求与响应同号，因此上面的重载沿用原有行为。
        /// </summary>
        private async UniTask<TResponse> SendRequestAsync<TRequest, TResponse>(
            TRequest request,
            LegacyProtocolValue responseProtocol,
            TimeSpan timeout,
            CancellationToken cancellationToken)
            where TRequest : LegacyMessage
            where TResponse : LegacyMessage
        {
            ThrowIfDisposed();
            await EnsureConnectedAsync(cancellationToken);

            var completion = new TaskCompletionSource<LegacyMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_pending.TryAdd(responseProtocol, completion))
            {
                throw new InvalidOperationException(
                    "A legacy request awaiting " + responseProtocol + " is already pending.");
            }

            try
            {
                Send(request, _sessionKey);
                var response = await AwaitWithTimeoutAsync(completion.Task, timeout, cancellationToken);
                return (TResponse)response;
            }
            finally
            {
                _pending.TryRemove(responseProtocol, out _);
            }
        }

        private async UniTask EnsureConnectedAsync(CancellationToken cancellationToken)
        {
            if (IsReady())
            {
                return;
            }

            await _connectGate.WaitAsync(cancellationToken);
            try
            {
                if (IsReady())
                {
                    return;
                }

                Close(new IOException("Legacy connection is being replaced."));
                StopWorkers();
                _stop.Reset();
                _receiveCount = 0;
                _sessionKey = string.Empty;
                _handshake = new TaskCompletionSource<LegacyMsgSecret>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
                {
                    NoDelay = true
                };
                lock (_socketLock)
                {
                    _socket = socket;
                }

                try
                {
                    await Task.Run(() => socket.Connect(_host, _port), cancellationToken);
                    StartWorkers();
                    Send(new LegacyMsgSecret(), PublicHandshakeKey);
                    var secret = await AwaitWithTimeoutAsync(
                        _handshake.Task,
                        DefaultRequestTimeout,
                        cancellationToken);
                    if (string.IsNullOrWhiteSpace(secret.Secret))
                    {
                        throw new InvalidDataException("Legacy handshake returned an empty session key.");
                    }
                }
                catch
                {
                    Close(new IOException("Legacy handshake failed."));
                    throw;
                }
            }
            finally
            {
                _connectGate.Release();
            }
        }

        private void StartWorkers()
        {
            _receiveThread = new Thread(ReceiveLoop)
            {
                IsBackground = true,
                Name = "NARAKA Legacy Receive"
            };
            _receiveThread.Start();

            _lastPingUtc = DateTime.UtcNow;
            _heartbeatThread = new Thread(HeartbeatLoop)
            {
                IsBackground = true,
                Name = "NARAKA Legacy Heartbeat"
            };
            _heartbeatThread.Start();
        }

        private void ReceiveLoop()
        {
            var chunk = new byte[8 * 1024];
            try
            {
                while (!_stop.IsSet)
                {
                    var socket = GetSocket();
                    if (socket == null)
                    {
                        return;
                    }

                    var received = socket.Receive(chunk);
                    if (received <= 0)
                    {
                        throw new IOException("Legacy server closed the connection.");
                    }

                    AppendReceived(chunk, received);
                    DrainFrames();
                }
            }
            catch (Exception exception)
            {
                if (!_stop.IsSet)
                {
                    Close(exception);
                }
            }
        }

        private void HeartbeatLoop()
        {
            while (!_stop.Wait(TimeSpan.FromSeconds(1)))
            {
                if (string.IsNullOrEmpty(_sessionKey) ||
                    DateTime.UtcNow - _lastPingUtc < TimeSpan.FromSeconds(HeartbeatIntervalSeconds))
                {
                    continue;
                }

                try
                {
                    Send(new LegacyMsgPing(), _sessionKey);
                    _lastPingUtc = DateTime.UtcNow;
                }
                catch (Exception exception)
                {
                    Close(exception);
                    return;
                }
            }
        }

        private void AppendReceived(byte[] source, int count)
        {
            var required = checked(_receiveCount + count);
            if (required > LegacyWireCodec.HeaderLength + LegacyWireCodec.MaximumPayloadLength)
            {
                throw new InvalidDataException("Legacy receive buffer exceeded the safety limit.");
            }

            if (required > _receiveBuffer.Length)
            {
                var next = Math.Min(
                    Math.Max(required, _receiveBuffer.Length * 2),
                    LegacyWireCodec.HeaderLength + LegacyWireCodec.MaximumPayloadLength);
                Array.Resize(ref _receiveBuffer, next);
            }

            Buffer.BlockCopy(source, 0, _receiveBuffer, _receiveCount, count);
            _receiveCount = required;
        }

        private void DrainFrames()
        {
            while (true)
            {
                var key = string.IsNullOrEmpty(_sessionKey) ? PublicHandshakeKey : _sessionKey;
                if (!LegacyWireCodec.TryDecode(
                        _receiveBuffer,
                        _receiveCount,
                        key,
                        out var message,
                        out var consumed))
                {
                    return;
                }

                Buffer.BlockCopy(_receiveBuffer, consumed, _receiveBuffer, 0, _receiveCount - consumed);
                _receiveCount -= consumed;
                Dispatch(message);
            }
        }

        private void Dispatch(LegacyMessage message)
        {
            if (message is LegacyMsgSecret secret)
            {
                _sessionKey = secret.Secret ?? string.Empty;
                _handshake.TrySetResult(secret);
                return;
            }

            if (message is LegacyMsgPing)
            {
                return;
            }

            if (_pending.TryRemove(message.ProtocolType, out var completion))
            {
                completion.TrySetResult(message);
            }
        }

        private void Send(LegacyMessage message, string passphrase)
        {
            var packet = LegacyWireCodec.Encode(message, passphrase);
            lock (_sendLock)
            {
                var socket = GetSocket();
                if (socket == null || !socket.Connected)
                {
                    throw new IOException("Legacy socket is not connected.");
                }

                var offset = 0;
                while (offset < packet.Length)
                {
                    var sent = socket.Send(packet, offset, packet.Length - offset, SocketFlags.None);
                    if (sent <= 0)
                    {
                        throw new IOException("Legacy socket closed during send.");
                    }

                    offset += sent;
                }
            }
        }

        private bool IsReady()
        {
            var socket = GetSocket();
            return socket != null && socket.Connected && !string.IsNullOrEmpty(_sessionKey) && !_stop.IsSet;
        }

        private Socket GetSocket()
        {
            lock (_socketLock)
            {
                return _socket;
            }
        }

        private void Close(Exception reason)
        {
            _stop.Set();
            Socket socket;
            lock (_socketLock)
            {
                socket = _socket;
                _socket = null;
            }

            if (socket != null)
            {
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

            _sessionKey = string.Empty;
            _handshake?.TrySetException(reason);
            foreach (var pair in _pending.ToArray())
            {
                if (_pending.TryRemove(pair.Key, out var completion))
                {
                    completion.TrySetException(reason);
                }
            }
        }

        private void StopWorkers()
        {
            if (_receiveThread != null && _receiveThread != Thread.CurrentThread)
            {
                _receiveThread.Join(TimeSpan.FromSeconds(1));
            }

            if (_heartbeatThread != null && _heartbeatThread != Thread.CurrentThread)
            {
                _heartbeatThread.Join(TimeSpan.FromSeconds(1));
            }

            _receiveThread = null;
            _heartbeatThread = null;
        }

        private static async Task<T> AwaitWithTimeoutAsync<T>(
            Task<T> task,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            var delay = Task.Delay(timeout, cancellationToken);
            var completed = await Task.WhenAny(task, delay);
            if (completed == task)
            {
                return await task;
            }

            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException("Legacy network request timed out.");
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(LegacyNetworkAdapter));
            }
        }

        private sealed class EmptyObservable<T> : IObservable<T>
        {
            public static readonly EmptyObservable<T> Instance = new EmptyObservable<T>();

            public IDisposable Subscribe(IObserver<T> observer)
            {
                observer?.OnCompleted();
                return EmptyDisposable.Instance;
            }
        }

        private sealed class EmptyDisposable : IDisposable
        {
            public static readonly EmptyDisposable Instance = new EmptyDisposable();

            public void Dispose()
            {
            }
        }
    }
}
