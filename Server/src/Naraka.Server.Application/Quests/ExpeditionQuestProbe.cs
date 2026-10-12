using Naraka.Server.Application.Expeditions;

namespace Naraka.Server.Application.Quests;

/// <summary>用既有远征仓储回答"账号是否在开放世界里"。只读，不触碰远征资产。</summary>
public sealed class ExpeditionQuestProbe(IExpeditionRepository expeditions) : IActiveExpeditionProbe
{
    private readonly IExpeditionRepository _expeditions =
        expeditions ?? throw new ArgumentNullException(nameof(expeditions));

    public async Task<bool> HasActiveExpeditionAsync(long accountId, CancellationToken cancellationToken)
    {
        try
        {
            return await _expeditions.FindActiveAsync(accountId, cancellationToken) is not null;
        }
        catch (ExpeditionStorageException exception)
        {
            throw new QuestStorageException("Active expedition lookup failed.", exception);
        }
    }
}
