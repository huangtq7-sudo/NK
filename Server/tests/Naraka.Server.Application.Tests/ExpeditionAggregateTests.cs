using Naraka.Server.Domain;
using Naraka.Server.Domain.Expeditions;

namespace Naraka.Server.Application.Tests;

public sealed class ExpeditionAggregateTests
{
    private static readonly DateTimeOffset StartedAt =
        new(2026, 10, 7, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void StartRequiresValidAccountAndStableMapId()
    {
        var id = new ExpeditionId("exp-001");

        Assert.Throws<ArgumentException>(() =>
            ExpeditionAggregate.Start(default, 42, "map_task_01", StartedAt));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ExpeditionAggregate.Start(id, 0, "map_task_01", StartedAt));
        Assert.Throws<ArgumentException>(() =>
            ExpeditionAggregate.Start(id, 42, " ", StartedAt));
    }

    [Fact]
    public void RepeatedDropsAreAggregatedByKindAndId()
    {
        var expedition = Create();

        expedition.AddMonsterDrop(ExpeditionAssetKind.Item, "mat_wolf_fang", 2);
        expedition.AddMonsterDrop(ExpeditionAssetKind.Item, "mat_wolf_fang", 3);
        expedition.AddMonsterDrop(ExpeditionAssetKind.Currency, "currency_coin", 7);

        Assert.Collection(
            expedition.TemporaryAssets,
            item =>
            {
                Assert.Equal(ExpeditionAssetKind.Item, item.Kind);
                Assert.Equal("mat_wolf_fang", item.AssetId);
                Assert.Equal(5, item.Quantity);
            },
            currency =>
            {
                Assert.Equal(ExpeditionAssetKind.Currency, currency.Kind);
                Assert.Equal("currency_coin", currency.AssetId);
                Assert.Equal(7, currency.Quantity);
            });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void DropQuantityMustBePositive(long quantity)
    {
        var expedition = Create();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            expedition.AddMonsterDrop(ExpeditionAssetKind.Item, "mat_wolf_fang", quantity));
    }

    [Fact]
    public void DeathClearsOnlyCurrentTemporaryDropsAndKeepsExpeditionActive()
    {
        var expedition = Create();
        expedition.AddMonsterDrop(ExpeditionAssetKind.Item, "mat_wolf_fang", 2);
        expedition.AddMonsterDrop(ExpeditionAssetKind.Currency, "currency_coin", 11);

        var death = expedition.RecordDeath(StartedAt.AddMinutes(3));

        Assert.Equal(ExpeditionStatus.Active, expedition.Status);
        Assert.Equal(1, expedition.DeathCount);
        Assert.Equal(2, death.ClearedAssets.Count);
        Assert.Empty(expedition.TemporaryAssets);
    }

    [Fact]
    public void NormalSettlementCapturesAssetsAndClosesExpedition()
    {
        var expedition = Create();
        expedition.AddMonsterDrop(ExpeditionAssetKind.Item, "mat_wolf_fang", 2);

        var result = expedition.Settle(
            new RequestId("settle-normal-001"),
            ExpeditionSettlementReason.ReturnedToLobby,
            StartedAt.AddMinutes(10));

        Assert.False(result.IsReplay);
        Assert.Equal(ExpeditionStatus.Settled, expedition.Status);
        Assert.Single(result.Summary.Assets);
        Assert.Empty(expedition.TemporaryAssets);
        Assert.Equal(ExpeditionSettlementReason.ReturnedToLobby, result.Summary.Reason);
    }

    [Fact]
    public void SettlementRejectsDefaultRequestId()
    {
        var expedition = Create();

        Assert.Throws<ArgumentException>(() => expedition.Settle(
            default,
            ExpeditionSettlementReason.ReturnedToLobby,
            StartedAt.AddMinutes(10)));
        Assert.Equal(ExpeditionStatus.Active, expedition.Status);
    }

    [Fact]
    public void ConnectionLossUsesTheSameSettlementPath()
    {
        var expedition = Create();
        expedition.AddMonsterDrop(ExpeditionAssetKind.Currency, "currency_coin", 19);

        var result = expedition.Settle(
            new RequestId("connection-lost:exp-001"),
            ExpeditionSettlementReason.ConnectionLost,
            StartedAt.AddMinutes(5));

        Assert.False(result.IsReplay);
        Assert.Equal(19, Assert.Single(result.Summary.Assets).Quantity);
        Assert.Equal(ExpeditionSettlementReason.ConnectionLost, result.Summary.Reason);
    }

    [Fact]
    public void RepeatedSettlementReplaysFirstSummaryEvenWithAnotherRequestId()
    {
        var expedition = Create();
        expedition.AddMonsterDrop(ExpeditionAssetKind.Item, "mat_wolf_fang", 4);

        var first = expedition.Settle(
            new RequestId("settle-001"),
            ExpeditionSettlementReason.ReturnedToLobby,
            StartedAt.AddMinutes(5));
        var repeat = expedition.Settle(
            new RequestId("settle-002"),
            ExpeditionSettlementReason.ConnectionLost,
            StartedAt.AddMinutes(6));

        Assert.False(first.IsReplay);
        Assert.True(repeat.IsReplay);
        Assert.Equal(first.Summary, repeat.Summary);
        Assert.Equal("settle-001", repeat.Summary.RequestId.Value);
        Assert.Equal(ExpeditionSettlementReason.ReturnedToLobby, repeat.Summary.Reason);
    }

    [Fact]
    public void SettledExpeditionRejectsNewDropsAndDeath()
    {
        var expedition = Create();
        expedition.Settle(
            new RequestId("settle-001"),
            ExpeditionSettlementReason.ReturnedToLobby,
            StartedAt.AddMinutes(5));

        Assert.Throws<InvalidOperationException>(() =>
            expedition.AddMonsterDrop(ExpeditionAssetKind.Item, "mat_wolf_fang", 1));
        Assert.Throws<InvalidOperationException>(() =>
            expedition.RecordDeath(StartedAt.AddMinutes(6)));
    }

    [Fact]
    public void AggregationOverflowDoesNotWrapIntoANegativeQuantity()
    {
        var expedition = Create();
        expedition.AddMonsterDrop(ExpeditionAssetKind.Item, "mat_wolf_fang", long.MaxValue);

        Assert.Throws<OverflowException>(() =>
            expedition.AddMonsterDrop(ExpeditionAssetKind.Item, "mat_wolf_fang", 1));
    }

    private static ExpeditionAggregate Create() =>
        ExpeditionAggregate.Start(
            new ExpeditionId("exp-001"),
            42,
            "map_task_01",
            StartedAt);
}
