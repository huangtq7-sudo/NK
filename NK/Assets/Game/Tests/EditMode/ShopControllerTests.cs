using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Naraka.Config;
using Naraka.Core.Application.Bootstrap;
using Naraka.Features.Lobby.Controller;
using Naraka.Features.Lobby.Model;
using Naraka.Features.Shop.Controller;
using NUnit.Framework;

namespace Naraka.P0.Tests
{
    internal sealed class FakeShopGateway : IShopGateway
    {
        public List<(string ProductId, int Quantity, string OrderId)> Purchases { get; } =
            new List<(string, int, string)>();

        public int LoadCount { get; private set; }

        public ShopResult Result { get; set; } =
            ShopResult.Success(Array.Empty<ShopPurchaseCount>(), 1000, 1000, 1000);

        public Exception ThrowWith { get; set; }

        public UniTask<ShopResult> RequestShopAsync(CancellationToken cancellationToken)
        {
            LoadCount++;
            return Respond();
        }

        public UniTask<ShopResult> PurchaseAsync(
            string productId,
            int quantity,
            string orderId,
            CancellationToken cancellationToken)
        {
            Purchases.Add((productId, quantity, orderId));
            return Respond();
        }

        private UniTask<ShopResult> Respond() =>
            ThrowWith != null
                ? UniTask.FromException<ShopResult>(ThrowWith)
                : UniTask.FromResult(Result);
    }

    /// <summary>
    /// 商店客户端：分类过滤、数量夹取、限购、订单号与能力门禁。
    /// </summary>
    public sealed class ShopControllerTests
    {
        private const string CheapProduct = "shop_cheap";
        private const string LimitedProduct = "shop_limited";

        private static FakeGameConfigProvider ShopConfig()
        {
            var catalog = new NarakaConfigCatalog
            {
                SchemaVersion = "1.0.0",
                ConfigVersion = "test",
                Currencies = new[]
                {
                    new CurrencyConfig { CurrencyId = "Copper", DisplayName = "铜币", SortOrder = 1 }
                },
                Items = new[]
                {
                    new ItemConfig
                    {
                        ItemId = "mat_cheap", DisplayName = "廉价材料",
                        Category = ConfigItemCategory.Material, Quality = ConfigQuality.White,
                        StackLimit = 999, SortOrder = 1, IconKey = "mat_cheap"
                    },
                    new ItemConfig
                    {
                        ItemId = "soul_limited", DisplayName = "限购魂玉",
                        Category = ConfigItemCategory.Soulstone, Quality = ConfigQuality.Purple,
                        StackLimit = 20, SortOrder = 2, IconKey = "soul_limited"
                    }
                },
                ShopProducts = new[]
                {
                    new ShopProductConfig
                    {
                        ProductId = CheapProduct, ItemId = "mat_cheap",
                        ShopCategory = ConfigItemCategory.Material, CurrencyId = "Copper",
                        UnitPrice = 100, PurchaseLimit = 0, SortOrder = 1, IsAvailable = true
                    },
                    new ShopProductConfig
                    {
                        ProductId = LimitedProduct, ItemId = "soul_limited",
                        ShopCategory = ConfigItemCategory.Soulstone, CurrencyId = "Copper",
                        UnitPrice = 50, PurchaseLimit = 3, SortOrder = 2, IsAvailable = true
                    },
                    new ShopProductConfig
                    {
                        ProductId = "shop_hidden", ItemId = "mat_cheap",
                        ShopCategory = ConfigItemCategory.Material, CurrencyId = "Copper",
                        UnitPrice = 1, PurchaseLimit = 0, SortOrder = 3, IsAvailable = false
                    }
                }
            };

            return new FakeGameConfigProvider(catalog);
        }

        private static (ShopController Shop, LobbyController Lobby, FakeShopGateway Gateway) Create(
            IServerCapabilities capabilities = null)
        {
            LobbyAccountSnapshot.TryCreate(1, 1000, 1000, 1000, out var summary);
            var caps = capabilities ?? LobbyTestCapabilities.Full();
            var lobby = new LobbyController(
                new LobbyModel(),
                new FakeLobbySceneGateway(),
                "Map1",
                new FakeLobbyAccountGateway { Result = LobbyAccountSummaryResult.Success(summary) },
                new FakeLobbyProfileGateway(),
                caps);

            var gateway = new FakeShopGateway();
            var shop = new ShopController(ShopConfig(), gateway, lobby, caps);

            lobby.Enter("player-one", 42);
            lobby.RequestFeature(LobbyFeature.Shop);
            return (shop, lobby, gateway);
        }

        [Test]
        public void OpeningTheShopEntryLoadsAccountState()
        {
            var (shop, _, gateway) = Create();

            Assert.That(shop.Current.IsOpen, Is.True);
            Assert.That(gateway.LoadCount, Is.EqualTo(1));
            Assert.That(shop.Current.HasServerState, Is.True);
            Assert.That(shop.Current.Copper, Is.EqualTo(1000));
        }

        [Test]
        public void CompatibilityModeNeverSendsAShopRequest()
        {
            var (shop, _, gateway) = Create(LobbyTestCapabilities.LegacyCloud());

            Assert.That(gateway.LoadCount, Is.Zero);
            Assert.That(shop.Current.StatusMessage, Does.Contain("服务器功能尚未升级"));
        }

        [Test]
        public void UnavailableProductsAreNeverListed()
        {
            var (shop, _, _) = Create();

            var ids = shop.VisibleProducts.Select(product => product.ProductId).ToArray();

            Assert.That(ids, Is.EqualTo(new[] { CheapProduct, LimitedProduct }));
        }

        [Test]
        public void CategoryFilterKeepsOnlyMatchingProducts()
        {
            var (shop, _, _) = Create();

            shop.SelectCategory(ShopCategory.Soulstone);

            Assert.That(
                shop.VisibleProducts.Select(product => product.ProductId).ToArray(),
                Is.EqualTo(new[] { LimitedProduct }));
        }

        [Test]
        public void PurchaseDialogStartsAtOneAndSendsNothingUntilConfirmed()
        {
            var (shop, _, gateway) = Create();
            shop.SelectProduct(CheapProduct);

            shop.OpenPurchaseDialog();

            Assert.That(shop.Current.IsDialogOpen, Is.True);
            Assert.That(shop.Current.PurchaseQuantity, Is.EqualTo(1));
            Assert.That(gateway.Purchases, Is.Empty);

            shop.ConfirmPurchase();

            Assert.That(gateway.Purchases.Count, Is.EqualTo(1));
            Assert.That(gateway.Purchases[0].ProductId, Is.EqualTo(CheapProduct));
            Assert.That(gateway.Purchases[0].Quantity, Is.EqualTo(1));
        }

        [Test]
        public void QuantityNeverDropsBelowOne()
        {
            var (shop, _, _) = Create();
            shop.SelectProduct(CheapProduct);
            shop.OpenPurchaseDialog();

            shop.ChangePurchaseQuantity(-5);

            Assert.That(shop.Current.PurchaseQuantity, Is.EqualTo(1));
        }

        [Test]
        public void QuantityIsCappedByWhatTheBalanceCanAfford()
        {
            var (shop, _, _) = Create();
            shop.SelectProduct(CheapProduct);
            shop.OpenPurchaseDialog();

            // 余额 1000、单价 100，因此最多 10 件。
            shop.SetPurchaseQuantity(999);

            Assert.That(shop.Current.PurchaseQuantity, Is.EqualTo(10));
        }

        [Test]
        public void QuantityIsCappedByTheRemainingPurchaseLimit()
        {
            var (shop, _, gateway) = Create();
            gateway.Result = ShopResult.Success(
                new[] { new ShopPurchaseCount(LimitedProduct, 1) }, 1000, 1000, 1000);
            shop.ReloadAsync(CancellationToken.None).Forget();
            shop.SelectProduct(LimitedProduct);
            shop.OpenPurchaseDialog();

            shop.SetPurchaseQuantity(50);

            // 限购 3、已买 1，因此最多再买 2。
            Assert.That(shop.Current.PurchaseQuantity, Is.EqualTo(2));
            Assert.That(shop.RemainingPurchaseLimit, Is.EqualTo(2));
        }

        [Test]
        public void SoldOutProductCannotOpenThePurchaseDialog()
        {
            var (shop, _, gateway) = Create();
            gateway.Result = ShopResult.Success(
                new[] { new ShopPurchaseCount(LimitedProduct, 3) }, 1000, 1000, 1000);
            shop.ReloadAsync(CancellationToken.None).Forget();
            shop.SelectProduct(LimitedProduct);

            shop.OpenPurchaseDialog();

            Assert.That(shop.Current.IsDialogOpen, Is.False);
            Assert.That(shop.Current.StatusMessage, Does.Contain("上限"));
        }

        [Test]
        public void EachPurchaseUsesAFreshOrderId()
        {
            var (shop, _, gateway) = Create();
            shop.SelectProduct(CheapProduct);

            shop.OpenPurchaseDialog();
            shop.ConfirmPurchase();
            shop.OpenPurchaseDialog();
            shop.ConfirmPurchase();

            Assert.That(gateway.Purchases.Count, Is.EqualTo(2));
            Assert.That(gateway.Purchases[0].OrderId, Is.Not.EqualTo(gateway.Purchases[1].OrderId));
            Assert.That(gateway.Purchases[0].OrderId, Is.Not.Empty);
        }

        [Test]
        public void CancellingTheDialogSendsNothing()
        {
            var (shop, _, gateway) = Create();
            shop.SelectProduct(CheapProduct);
            shop.OpenPurchaseDialog();

            shop.CancelPurchase();
            shop.ConfirmPurchase();

            Assert.That(gateway.Purchases, Is.Empty);
        }

        [Test]
        public void RejectedPurchaseKeepsThePreviousServerState()
        {
            var (shop, _, gateway) = Create();
            shop.SelectProduct(CheapProduct);
            shop.OpenPurchaseDialog();
            gateway.Result = ShopResult.Failed(LobbyOperationStatus.InsufficientCurrency);

            shop.ConfirmPurchase();

            Assert.That(shop.Current.HasServerState, Is.True);
            Assert.That(shop.Current.Copper, Is.EqualTo(1000));
            Assert.That(shop.Current.StatusMessage, Does.Contain("货币不足"));
            Assert.That(shop.Current.IsBusy, Is.False);
        }

        [Test]
        public void TransportFailureShowsAReadableMessage()
        {
            var (shop, _, gateway) = Create();
            shop.SelectProduct(CheapProduct);
            shop.OpenPurchaseDialog();
            gateway.ThrowWith = new InvalidOperationException("socket down");

            shop.ConfirmPurchase();

            Assert.That(shop.Current.StatusMessage, Does.Contain("无法连接服务器"));
        }

        [Test]
        public void SwitchingCategoryClearsTheSelectionAndDialog()
        {
            var (shop, _, _) = Create();
            shop.SelectProduct(CheapProduct);
            shop.OpenPurchaseDialog();

            shop.SelectCategory(ShopCategory.Soulstone);

            Assert.That(shop.Current.SelectedProductId, Is.Empty);
            Assert.That(shop.Current.IsDialogOpen, Is.False);
        }

        [Test]
        public void SingleRequestQuantityCapMatchesTheServerContract()
        {
            Assert.That(ShopPresentationState.MaximumQuantityPerPurchase, Is.EqualTo(999));
        }

        [Test]
        public void DisposingReleasesTheLobbySubscription()
        {
            var (shop, lobby, gateway) = Create();

            shop.Dispose();
            lobby.CloseFeature();
            lobby.RequestFeature(LobbyFeature.Shop);

            Assert.That(gateway.LoadCount, Is.EqualTo(1));
        }
    }
}
