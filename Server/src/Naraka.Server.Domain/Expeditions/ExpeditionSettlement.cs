using Naraka.Server.Domain;

namespace Naraka.Server.Domain.Expeditions;

public enum ExpeditionSettlementReason
{
    ReturnedToLobby = 0,
    ConnectionLost = 1
}

/// <summary>
/// 首次成功结算的不可变摘要。后续重复请求必须重放这份结果，
/// 而不是再计算、再入库或再写货币流水。
/// </summary>
public sealed record ExpeditionSettlementSummary(
    ExpeditionId ExpeditionId,
    long AccountId,
    RequestId RequestId,
    ExpeditionSettlementReason Reason,
    DateTimeOffset SettledAt,
    IReadOnlyList<ExpeditionAsset> Assets,
    int DeathCount);

public sealed record ExpeditionSettlementResult(
    ExpeditionSettlementSummary Summary,
    bool IsReplay);

public sealed record ExpeditionDeathResult(
    ExpeditionId ExpeditionId,
    DateTimeOffset OccurredAt,
    IReadOnlyList<ExpeditionAsset> ClearedAssets,
    int DeathCount);
