using Naraka.Server.Application.Progression;
using Naraka.Server.Domain;
using Naraka.Server.Domain.Expeditions;

namespace Naraka.Server.Application.Expeditions;

/// <summary>服务端认可的稳定MapId。这些值会进入协议和数据库，不是Unity场景名。</summary>
public static class ExpeditionMapIds
{
    public const string Map01 = "Map01";
    public const string Map02 = "Map02";
}

public sealed record ExpeditionSnapshot(
    ExpeditionId ExpeditionId,
    long AccountId,
    string EntryMapId,
    DateTimeOffset StartedAt,
    ExpeditionStatus Status,
    IReadOnlyList<ExpeditionAsset> TemporaryAssets,
    int DeathCount);

public enum ExpeditionWriteOutcome
{
    Applied = 0,
    AlreadyApplied = 1,
    NotFound = 2,
    Conflict = 3,
    AccountMissing = 4
}

public sealed record StartExpeditionCommand(
    long AccountId,
    string RequestId,
    ExpeditionId ExpeditionId,
    string EntryMapId,
    DateTimeOffset StartedAt);

/// <summary>
/// 掉落只由服务端战斗/掉落模块构造。<paramref name="DropEventId"/>是稳定的服务端事件Id，
/// 同一只怪的同一次掉落重放时不得再写一份临时资产。
/// </summary>
public sealed record AppendExpeditionDropsCommand(
    long AccountId,
    ExpeditionId ExpeditionId,
    string DropEventId,
    IReadOnlyList<ExpeditionAsset> Assets,
    DateTimeOffset OccurredAt);

public sealed record RecordExpeditionDeathCommand(
    long AccountId,
    ExpeditionId ExpeditionId,
    string DeathEventId,
    DateTimeOffset OccurredAt);

public sealed record SettleExpeditionCommand(
    long AccountId,
    ExpeditionId ExpeditionId,
    RequestId RequestId,
    ExpeditionSettlementReason Reason,
    DateTimeOffset SettledAt);

public sealed record ExpeditionWriteResult(
    ExpeditionWriteOutcome Outcome,
    ExpeditionSnapshot? Snapshot = null,
    ExpeditionDeathResult? Death = null,
    ExpeditionSettlementSummary? Settlement = null);

public sealed class ExpeditionStorageException : Exception
{
    public ExpeditionStorageException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// 远征的原子存储端口。实现必须在数据库事务内重建聚合并调用领域方法，
/// 不得在SQL或仓储类里另写一份死亡/结算规则。
/// </summary>
public interface IExpeditionRepository
{
    Task<ExpeditionSnapshot?> FindActiveAsync(long accountId, CancellationToken cancellationToken);

    /// <summary>
    /// 同一账号只能有一个活动远征。同一RequestId重放首次结果，
    /// 另一RequestId在已有活动远征时返回Conflict。
    /// </summary>
    Task<ExpeditionWriteResult> TryStartAsync(
        StartExpeditionCommand command,
        CancellationToken cancellationToken);

    /// <summary>按DropEventId幂等地写入一组服务端生成的怪物掉落。</summary>
    Task<ExpeditionWriteResult> TryAppendDropsAsync(
        AppendExpeditionDropsCommand command,
        CancellationToken cancellationToken);

    /// <summary>原子清理MonsterDrop并写入死亡审计；DeathEventId重复时重放首次结果。</summary>
    Task<ExpeditionWriteResult> TryRecordDeathAsync(
        RecordExpeditionDeathCommand command,
        CancellationToken cancellationToken);

    /// <summary>
    /// 在同一事务内锁定远征、合并临时资产到仓库/邮件和货币流水、
    /// 保存SettlementSummary并关闭远征。重复请求只返回首次摘要。
    /// </summary>
    Task<ExpeditionWriteResult> TrySettleAsync(
        SettleExpeditionCommand command,
        CancellationToken cancellationToken);
}

public interface IExpeditionIdGenerator
{
    ExpeditionId NewId();
}

public sealed class GuidExpeditionIdGenerator : IExpeditionIdGenerator
{
    public ExpeditionId NewId() => new(Guid.NewGuid().ToString("N"));
}

public readonly struct ExpeditionResult
{
    private ExpeditionResult(
        LobbyOperationStatus status,
        ExpeditionSnapshot? snapshot,
        ExpeditionDeathResult? death,
        ExpeditionSettlementSummary? settlement,
        bool isReplay)
    {
        Status = status;
        Snapshot = snapshot;
        Death = death;
        Settlement = settlement;
        IsReplay = isReplay;
    }

    public LobbyOperationStatus Status { get; }

    public ExpeditionSnapshot? Snapshot { get; }

    public ExpeditionDeathResult? Death { get; }

    public ExpeditionSettlementSummary? Settlement { get; }

    public bool IsReplay { get; }

    public static ExpeditionResult Success(
        ExpeditionSnapshot? snapshot = null,
        ExpeditionDeathResult? death = null,
        ExpeditionSettlementSummary? settlement = null,
        bool isReplay = false) =>
        new(LobbyOperationStatus.Success, snapshot, death, settlement, isReplay);

    public static ExpeditionResult Failed(LobbyOperationStatus status) =>
        new(status, null, null, null, false);
}

/// <summary>
/// P3远征用例边界。身份认证由LegacyNetworkV1之上的适配器完成，
/// 本服务只接收已解析的AccountId，不参与Socket、AES或Protobuf。
/// </summary>
public sealed class ExpeditionService(
    IExpeditionRepository repository,
    IExpeditionIdGenerator idGenerator,
    TimeProvider? timeProvider = null)
{
    private const int MaximumIdLength = 64;
    private const string ConnectionLostRequestId = "server-connection-lost";

    private readonly IExpeditionRepository _repository =
        repository ?? throw new ArgumentNullException(nameof(repository));

    private readonly IExpeditionIdGenerator _idGenerator =
        idGenerator ?? throw new ArgumentNullException(nameof(idGenerator));

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public async Task<ExpeditionResult> GetActiveAsync(
        long accountId,
        CancellationToken cancellationToken)
    {
        if (accountId <= 0)
        {
            return ExpeditionResult.Failed(LobbyOperationStatus.InvalidRequest);
        }

        try
        {
            var active = await _repository.FindActiveAsync(accountId, cancellationToken);
            return active is null
                ? ExpeditionResult.Failed(LobbyOperationStatus.NotFound)
                : ExpeditionResult.Success(active);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ExpeditionStorageException)
        {
            return ExpeditionResult.Failed(LobbyOperationStatus.DatabaseUnavailable);
        }
        catch (Exception)
        {
            return ExpeditionResult.Failed(LobbyOperationStatus.InternalError);
        }
    }

    public Task<ExpeditionResult> StartAsync(
        long accountId,
        string? requestId,
        CancellationToken cancellationToken)
    {
        if (accountId <= 0 || !ValidId(requestId))
        {
            return Task.FromResult(ExpeditionResult.Failed(LobbyOperationStatus.InvalidRequest));
        }

        var command = new StartExpeditionCommand(
            accountId,
            requestId!,
            _idGenerator.NewId(),
            ExpeditionMapIds.Map01,
            _time.GetUtcNow());
        return ExecuteWriteAsync(
            () => _repository.TryStartAsync(command, cancellationToken),
            cancellationToken);
    }

    /// <summary>只能由受信的服务端战斗/掉落模块调用，不暴露为客户端协议请求。</summary>
    public Task<ExpeditionResult> GrantMonsterDropsAsync(
        long accountId,
        ExpeditionId expeditionId,
        string? dropEventId,
        IReadOnlyList<ExpeditionAsset>? assets,
        CancellationToken cancellationToken)
    {
        if (accountId <= 0 ||
            !ValidExpeditionId(expeditionId) ||
            !ValidId(dropEventId) ||
            assets is null ||
            assets.Count == 0 ||
            assets.Any(asset => asset is null || asset.Source != ExpeditionAssetSource.MonsterDrop))
        {
            return Task.FromResult(ExpeditionResult.Failed(LobbyOperationStatus.InvalidRequest));
        }

        var command = new AppendExpeditionDropsCommand(
            accountId,
            expeditionId,
            dropEventId!,
            assets.ToArray(),
            _time.GetUtcNow());
        return ExecuteWriteAsync(
            () => _repository.TryAppendDropsAsync(command, cancellationToken),
            cancellationToken);
    }

    public Task<ExpeditionResult> RecordDeathAsync(
        long accountId,
        ExpeditionId expeditionId,
        string? deathEventId,
        CancellationToken cancellationToken)
    {
        if (accountId <= 0 || !ValidExpeditionId(expeditionId) || !ValidId(deathEventId))
        {
            return Task.FromResult(ExpeditionResult.Failed(LobbyOperationStatus.InvalidRequest));
        }

        var command = new RecordExpeditionDeathCommand(
            accountId,
            expeditionId,
            deathEventId!,
            _time.GetUtcNow());
        return ExecuteWriteAsync(
            () => _repository.TryRecordDeathAsync(command, cancellationToken),
            cancellationToken);
    }

    public Task<ExpeditionResult> ReturnToLobbyAsync(
        long accountId,
        ExpeditionId expeditionId,
        string? requestId,
        CancellationToken cancellationToken)
    {
        if (accountId <= 0 || !ValidExpeditionId(expeditionId) || !ValidId(requestId))
        {
            return Task.FromResult(ExpeditionResult.Failed(LobbyOperationStatus.InvalidRequest));
        }

        return SettleAsync(
            accountId,
            expeditionId,
            new RequestId(requestId!),
            ExpeditionSettlementReason.ReturnedToLobby,
            cancellationToken);
    }

    /// <summary>
    /// 断网/闪退只能由连接生命周期调用。没有活动远征时是成功的空操作，
    /// 因为每次大厅断开都可能走到这里。
    /// </summary>
    public async Task<ExpeditionResult> SettleConnectionLossAsync(
        long accountId,
        CancellationToken cancellationToken)
    {
        if (accountId <= 0)
        {
            return ExpeditionResult.Failed(LobbyOperationStatus.InvalidRequest);
        }

        try
        {
            var active = await _repository.FindActiveAsync(accountId, cancellationToken);
            if (active is null)
            {
                return ExpeditionResult.Success();
            }

            return await SettleAsync(
                accountId,
                active.ExpeditionId,
                new RequestId(ConnectionLostRequestId),
                ExpeditionSettlementReason.ConnectionLost,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ExpeditionStorageException)
        {
            return ExpeditionResult.Failed(LobbyOperationStatus.DatabaseUnavailable);
        }
        catch (Exception)
        {
            return ExpeditionResult.Failed(LobbyOperationStatus.InternalError);
        }
    }

    private Task<ExpeditionResult> SettleAsync(
        long accountId,
        ExpeditionId expeditionId,
        RequestId requestId,
        ExpeditionSettlementReason reason,
        CancellationToken cancellationToken)
    {
        var command = new SettleExpeditionCommand(
            accountId,
            expeditionId,
            requestId,
            reason,
            _time.GetUtcNow());
        return ExecuteWriteAsync(
            () => _repository.TrySettleAsync(command, cancellationToken),
            cancellationToken);
    }

    private static async Task<ExpeditionResult> ExecuteWriteAsync(
        Func<Task<ExpeditionWriteResult>> operation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var result = await operation();
            return result.Outcome switch
            {
                ExpeditionWriteOutcome.Applied => ExpeditionResult.Success(
                    result.Snapshot, result.Death, result.Settlement),
                ExpeditionWriteOutcome.AlreadyApplied => ExpeditionResult.Success(
                    result.Snapshot, result.Death, result.Settlement, isReplay: true),
                ExpeditionWriteOutcome.NotFound or ExpeditionWriteOutcome.AccountMissing =>
                    ExpeditionResult.Failed(LobbyOperationStatus.NotFound),
                ExpeditionWriteOutcome.Conflict =>
                    ExpeditionResult.Failed(LobbyOperationStatus.Conflict),
                _ => ExpeditionResult.Failed(LobbyOperationStatus.InternalError)
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ExpeditionStorageException)
        {
            return ExpeditionResult.Failed(LobbyOperationStatus.DatabaseUnavailable);
        }
        catch (Exception)
        {
            return ExpeditionResult.Failed(LobbyOperationStatus.InternalError);
        }
    }

    private static bool ValidExpeditionId(ExpeditionId id) => ValidId(id.Value);

    private static bool ValidId(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= MaximumIdLength;
}
