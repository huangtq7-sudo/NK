using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Naraka.Features.Inventory.Model;
using Naraka.Features.Lobby.Controller;

namespace Naraka.Features.Inventory.Controller
{
    /// <summary>仓库写操作。数值与线级枚举一一对应。</summary>
    public enum InventoryOperation
    {
        None = 0,
        Discard = 1,
        Sell = 2,
        Reorder = 3,
        AutoSort = 4,
        Equip = 5,
        Unequip = 6,
        Expand = 7
    }

    public readonly struct InventoryResult
    {
        private InventoryResult(
            LobbyOperationStatus status,
            InventorySnapshot snapshot,
            bool hasBalances,
            long copper,
            long silk,
            long gold)
        {
            Status = status;
            Snapshot = snapshot;
            HasBalances = hasBalances;
            Copper = copper;
            Silk = silk;
            Gold = gold;
        }

        public LobbyOperationStatus Status { get; }

        public InventorySnapshot Snapshot { get; }

        /// <summary>只有引起货币变动的操作（售卖、扩容）才携带余额。</summary>
        public bool HasBalances { get; }

        public long Copper { get; }

        public long Silk { get; }

        public long Gold { get; }

        public bool IsSuccess => Status == LobbyOperationStatus.Success && Snapshot != null;

        public static InventoryResult Success(InventorySnapshot snapshot) =>
            new InventoryResult(LobbyOperationStatus.Success, snapshot, false, 0, 0, 0);

        public static InventoryResult SuccessWithBalances(
            InventorySnapshot snapshot, long copper, long silk, long gold) =>
            new InventoryResult(LobbyOperationStatus.Success, snapshot, true, copper, silk, gold);

        public static InventoryResult Failed(LobbyOperationStatus status) =>
            new InventoryResult(status, null, false, 0, 0, 0);
    }

    /// <summary>
    /// 仓库网关。刻意不接受 accountId：操作对象由服务端的已认证连接会话决定（ADR-0007）。
    /// 每个写请求都携带 RequestId，由适配器生成。
    /// </summary>
    public interface IInventoryGateway
    {
        UniTask<InventoryResult> RequestInventoryAsync(CancellationToken cancellationToken);

        /// <summary>
        /// 提交一次仓库写操作。价格、堆叠上限与容量都由服务端按配置重新判定，
        /// 这里传的数量只是意图。
        /// </summary>
        UniTask<InventoryResult> MutateAsync(
            InventoryOperation operation,
            string itemId,
            long quantity,
            string slotKind,
            int slotIndex,
            IReadOnlyList<string> itemOrder,
            CancellationToken cancellationToken);
    }
}
