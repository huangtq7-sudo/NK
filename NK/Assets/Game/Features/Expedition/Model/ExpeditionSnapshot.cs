using System;
using System.Collections.Generic;
using Naraka.Core.Domain;

namespace Naraka.Features.Expedition.Model
{
    public enum ExpeditionState
    {
        None = 0,
        Active = 1,
        Settled = 2
    }

    public enum ExpeditionAssetKind
    {
        None = 0,
        Item = 1,
        Currency = 2
    }

    public enum ExpeditionAssetSource
    {
        None = 0,
        MonsterDrop = 1
    }

    public enum ExpeditionSettlementReason
    {
        None = 0,
        ReturnedToLobby = 1,
        ConnectionLost = 2
    }

    /// <summary>一次远征中的服务端权威临时资产。客户端只有读取权，没有写入或授予入口。</summary>
    public readonly struct ExpeditionAsset : IEquatable<ExpeditionAsset>
    {
        public ExpeditionAsset(
            ExpeditionAssetKind kind,
            string assetId,
            long quantity,
            ExpeditionAssetSource source)
        {
            Kind = kind;
            AssetId = assetId ?? string.Empty;
            Quantity = quantity;
            Source = source;
        }

        public ExpeditionAssetKind Kind { get; }

        public string AssetId { get; }

        public long Quantity { get; }

        public ExpeditionAssetSource Source { get; }

        public bool IsValid =>
            (Kind == ExpeditionAssetKind.Item || Kind == ExpeditionAssetKind.Currency) &&
            !string.IsNullOrWhiteSpace(AssetId) &&
            AssetId.Length <= 64 &&
            Quantity > 0 &&
            Source == ExpeditionAssetSource.MonsterDrop;

        public bool Equals(ExpeditionAsset other) =>
            Kind == other.Kind &&
            string.Equals(AssetId, other.AssetId, StringComparison.Ordinal) &&
            Quantity == other.Quantity &&
            Source == other.Source;

        public override bool Equals(object obj) => obj is ExpeditionAsset other && Equals(other);

        public override int GetHashCode()
        {
            var hash = (int)Kind;
            hash = (hash * 397) ^
                   (AssetId == null ? 0 : StringComparer.Ordinal.GetHashCode(AssetId));
            hash = (hash * 397) ^ Quantity.GetHashCode();
            return (hash * 397) ^ (int)Source;
        }
    }

    /// <summary>活动远征的不可变客户端快照。</summary>
    public sealed class ExpeditionSnapshot
    {
        private ExpeditionSnapshot(
            string expeditionId,
            string entryMapId,
            long startedUnixMilliseconds,
            ExpeditionState state,
            IReadOnlyList<ExpeditionAsset> temporaryAssets,
            int deathCount)
        {
            ExpeditionId = expeditionId;
            EntryMapId = entryMapId;
            StartedUnixMilliseconds = startedUnixMilliseconds;
            State = state;
            TemporaryAssets = temporaryAssets;
            DeathCount = deathCount;
        }

        public string ExpeditionId { get; }

        public string EntryMapId { get; }

        public long StartedUnixMilliseconds { get; }

        public ExpeditionState State { get; }

        public IReadOnlyList<ExpeditionAsset> TemporaryAssets { get; }

        public int DeathCount { get; }

        public static bool TryCreate(
            string expeditionId,
            string entryMapId,
            long startedUnixMilliseconds,
            ExpeditionState state,
            IReadOnlyList<ExpeditionAsset> temporaryAssets,
            int deathCount,
            out ExpeditionSnapshot snapshot)
        {
            snapshot = null;
            if (!ValidId(expeditionId) || !ValidId(entryMapId) || startedUnixMilliseconds <= 0 ||
                (state != ExpeditionState.Active && state != ExpeditionState.Settled) ||
                temporaryAssets == null || deathCount < 0)
            {
                return false;
            }

            var copy = new ExpeditionAsset[temporaryAssets.Count];
            var keys = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < temporaryAssets.Count; i++)
            {
                var asset = temporaryAssets[i];
                var key = ((int)asset.Kind) + ":" + asset.AssetId + ":" + ((int)asset.Source);
                if (!asset.IsValid || !keys.Add(key))
                {
                    return false;
                }

                copy[i] = asset;
            }

            snapshot = new ExpeditionSnapshot(
                expeditionId, entryMapId, startedUnixMilliseconds, state, copy, deathCount);
            return true;
        }

        private static bool ValidId(string value) =>
            !string.IsNullOrWhiteSpace(value) && value.Length <= 64;
    }

    public sealed class ExpeditionDeathSummary
    {
        public ExpeditionDeathSummary(
            string expeditionId,
            long occurredUnixMilliseconds,
            IReadOnlyList<ExpeditionAsset> clearedAssets,
            int deathCount)
        {
            ExpeditionId = expeditionId ?? string.Empty;
            OccurredUnixMilliseconds = occurredUnixMilliseconds;
            ClearedAssets = Copy(clearedAssets);
            DeathCount = deathCount;
        }

        public string ExpeditionId { get; }

        public long OccurredUnixMilliseconds { get; }

        public IReadOnlyList<ExpeditionAsset> ClearedAssets { get; }

        public int DeathCount { get; }

        public bool IsValid =>
            !string.IsNullOrWhiteSpace(ExpeditionId) && ExpeditionId.Length <= 64 &&
            OccurredUnixMilliseconds > 0 && DeathCount > 0 && AllValid(ClearedAssets);

        private static ExpeditionAsset[] Copy(IReadOnlyList<ExpeditionAsset> assets)
        {
            if (assets == null || assets.Count == 0)
            {
                return Array.Empty<ExpeditionAsset>();
            }

            var copy = new ExpeditionAsset[assets.Count];
            for (var i = 0; i < assets.Count; i++)
            {
                copy[i] = assets[i];
            }

            return copy;
        }

        internal static bool AllValid(IReadOnlyList<ExpeditionAsset> assets)
        {
            if (assets == null)
            {
                return false;
            }

            for (var i = 0; i < assets.Count; i++)
            {
                if (!assets[i].IsValid)
                {
                    return false;
                }
            }

            return true;
        }
    }

    public sealed class ExpeditionSettlementSummary
    {
        public ExpeditionSettlementSummary(
            string expeditionId,
            string requestId,
            ExpeditionSettlementReason reason,
            long settledUnixMilliseconds,
            IReadOnlyList<ExpeditionAsset> assets,
            int deathCount)
        {
            ExpeditionId = expeditionId ?? string.Empty;
            RequestId = requestId ?? string.Empty;
            Reason = reason;
            SettledUnixMilliseconds = settledUnixMilliseconds;
            Assets = assets == null ? Array.Empty<ExpeditionAsset>() : Copy(assets);
            DeathCount = deathCount;
        }

        public string ExpeditionId { get; }

        public string RequestId { get; }

        public ExpeditionSettlementReason Reason { get; }

        public long SettledUnixMilliseconds { get; }

        public IReadOnlyList<ExpeditionAsset> Assets { get; }

        public int DeathCount { get; }

        public bool IsValid =>
            !string.IsNullOrWhiteSpace(ExpeditionId) && ExpeditionId.Length <= 64 &&
            !string.IsNullOrWhiteSpace(RequestId) && RequestId.Length <= 64 &&
            (Reason == ExpeditionSettlementReason.ReturnedToLobby ||
             Reason == ExpeditionSettlementReason.ConnectionLost) &&
            SettledUnixMilliseconds > 0 && DeathCount >= 0 &&
            ExpeditionDeathSummary.AllValid(Assets);

        private static ExpeditionAsset[] Copy(IReadOnlyList<ExpeditionAsset> assets)
        {
            var copy = new ExpeditionAsset[assets.Count];
            for (var i = 0; i < assets.Count; i++)
            {
                copy[i] = assets[i];
            }

            return copy;
        }
    }

    /// <summary>跨场景持久的远征模型。只接受控制器已经校验过的服务端快照。</summary>
    public sealed class ExpeditionModel : IModel
    {
        public ExpeditionSnapshot Active { get; private set; }

        public ExpeditionDeathSummary LastDeath { get; private set; }

        public ExpeditionSettlementSummary LastSettlement { get; private set; }

        public bool HasActive => Active != null && Active.State == ExpeditionState.Active;

        public void ApplyActive(ExpeditionSnapshot snapshot)
        {
            Active = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        }

        public void ApplyDeath(ExpeditionSnapshot snapshot, ExpeditionDeathSummary death)
        {
            ApplyActive(snapshot);
            LastDeath = death ?? throw new ArgumentNullException(nameof(death));
        }

        public void ApplySettlement(ExpeditionSettlementSummary settlement)
        {
            LastSettlement = settlement ?? throw new ArgumentNullException(nameof(settlement));
            Active = null;
        }

        public void ClearActive() => Active = null;

        public void ClearAll()
        {
            Active = null;
            LastDeath = null;
            LastSettlement = null;
        }
    }
}
