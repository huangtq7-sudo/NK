using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Naraka.Config;
using Naraka.Core.Application.Bootstrap;
using Naraka.Core.Application.MVC;
using Naraka.Core.Application.Presentation;
using Naraka.Core.Domain;
using Naraka.Features.Expedition.Model;

namespace Naraka.Features.Expedition.Controller
{
    public interface IExpeditionController : IReadOnlyState<ExpeditionPresentationState>
    {
        UniTask<ExpeditionOperationStatus> LoadActiveAsync(CancellationToken cancellationToken);

        UniTask<ExpeditionOperationStatus> StartOrResumeAsync(CancellationToken cancellationToken);

        UniTask<ExpeditionOperationStatus> RecordDeathAsync(CancellationToken cancellationToken);

        UniTask<ExpeditionOperationStatus> SettleReturnToLobbyAsync(CancellationToken cancellationToken);
    }

    public sealed class GuidExpeditionRequestIdSource : IExpeditionRequestIdSource
    {
        public string NewId() => new RequestId(Guid.NewGuid().ToString("N")).Value;
    }

    /// <summary>
    /// 跨场景远征控制器。临时资产和结算摘要只接受服务端响应，客户端没有添加掉落的方法。
    /// </summary>
    public sealed class ExpeditionController : IController, IExpeditionController, IDisposable
    {
        private readonly ExpeditionModel _model;
        private readonly IExpeditionGateway _gateway;
        private readonly IServerCapabilities _capabilities;
        private readonly IExpeditionRequestIdSource _requestIds;
        private readonly ReactiveState<ExpeditionPresentationState> _state;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private bool _isBusy;
        private bool _disposed;
        private string _pendingStartRequestId;
        private string _pendingDeathRequestId;
        private string _pendingReturnRequestId;

        public ExpeditionController(
            ExpeditionModel model,
            IExpeditionGateway gateway,
            IServerCapabilities capabilities,
            IExpeditionRequestIdSource requestIds)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
            _requestIds = requestIds ?? throw new ArgumentNullException(nameof(requestIds));
            _state = new ReactiveState<ExpeditionPresentationState>(ExpeditionPresentationState.Initial);
        }

        public ExpeditionPresentationState Current => _state.Current;

        public async UniTask<ExpeditionOperationStatus> LoadActiveAsync(
            CancellationToken cancellationToken)
        {
            if (!TryBegin(out var rejection))
            {
                return rejection;
            }

            try
            {
                var result = await ExecuteAsync(
                    token => _gateway.GetActiveAsync(_requestIds.NewId(), token), cancellationToken);
                if (result.Status == ExpeditionOperationStatus.NotFound)
                {
                    _model.ClearActive();
                    Publish(false, ExpeditionOperationStatus.Success, string.Empty);
                    return ExpeditionOperationStatus.NotFound;
                }

                if (!result.IsSuccess || !IsActiveSnapshot(result.Snapshot))
                {
                    return Fail(result.IsSuccess
                        ? ExpeditionOperationStatus.InternalError
                        : result.Status);
                }

                _model.ApplyActive(result.Snapshot);
                Publish(false, ExpeditionOperationStatus.Success, string.Empty);
                return ExpeditionOperationStatus.Success;
            }
            finally
            {
                _isBusy = false;
            }
        }

        public async UniTask<ExpeditionOperationStatus> StartOrResumeAsync(
            CancellationToken cancellationToken)
        {
            if (_model.HasActive)
            {
                Publish(false, ExpeditionOperationStatus.Success, string.Empty);
                return ExpeditionOperationStatus.Success;
            }

            if (!TryBegin(out var rejection))
            {
                return rejection;
            }

            try
            {
                var active = await ExecuteAsync(
                    token => _gateway.GetActiveAsync(_requestIds.NewId(), token), cancellationToken);
                if (active.IsSuccess && IsActiveSnapshot(active.Snapshot))
                {
                    _model.ApplyActive(active.Snapshot);
                    Publish(false, ExpeditionOperationStatus.Success, string.Empty);
                    return ExpeditionOperationStatus.Success;
                }

                if (active.Status != ExpeditionOperationStatus.NotFound)
                {
                    return Fail(active.IsSuccess
                        ? ExpeditionOperationStatus.InternalError
                        : active.Status);
                }

                if (string.IsNullOrEmpty(_pendingStartRequestId))
                {
                    _pendingStartRequestId = _requestIds.NewId();
                }

                var started = await ExecuteAsync(
                    token => _gateway.StartAsync(_pendingStartRequestId, token), cancellationToken);
                if (!started.IsSuccess || !IsActiveSnapshot(started.Snapshot))
                {
                    return Fail(started.IsSuccess
                        ? ExpeditionOperationStatus.InternalError
                        : started.Status);
                }

                _pendingStartRequestId = string.Empty;
                _model.ApplyActive(started.Snapshot);
                Publish(false, ExpeditionOperationStatus.Success, string.Empty);
                return ExpeditionOperationStatus.Success;
            }
            finally
            {
                _isBusy = false;
            }
        }

        public async UniTask<ExpeditionOperationStatus> RecordDeathAsync(
            CancellationToken cancellationToken)
        {
            if (!_model.HasActive)
            {
                return Fail(ExpeditionOperationStatus.NotFound);
            }

            if (!TryBegin(out var rejection))
            {
                return rejection;
            }

            try
            {
                if (string.IsNullOrEmpty(_pendingDeathRequestId))
                {
                    _pendingDeathRequestId = _requestIds.NewId();
                }

                var result = await ExecuteAsync(
                    token => _gateway.RecordDeathAsync(
                        _model.Active.ExpeditionId, _pendingDeathRequestId, token),
                    cancellationToken);
                if (!result.IsSuccess || !IsActiveSnapshot(result.Snapshot) ||
                    result.Death == null || !result.Death.IsValid ||
                    !string.Equals(
                        result.Snapshot.ExpeditionId,
                        result.Death.ExpeditionId,
                        StringComparison.Ordinal))
                {
                    return Fail(result.IsSuccess
                        ? ExpeditionOperationStatus.InternalError
                        : result.Status);
                }

                _pendingDeathRequestId = string.Empty;
                _model.ApplyDeath(result.Snapshot, result.Death);
                Publish(false, ExpeditionOperationStatus.Success, string.Empty);
                return ExpeditionOperationStatus.Success;
            }
            finally
            {
                _isBusy = false;
            }
        }

        public async UniTask<ExpeditionOperationStatus> SettleReturnToLobbyAsync(
            CancellationToken cancellationToken)
        {
            if (!_model.HasActive)
            {
                return Fail(ExpeditionOperationStatus.NotFound);
            }

            if (!TryBegin(out var rejection))
            {
                return rejection;
            }

            try
            {
                if (string.IsNullOrEmpty(_pendingReturnRequestId))
                {
                    _pendingReturnRequestId = _requestIds.NewId();
                }

                var result = await ExecuteAsync(
                    token => _gateway.ReturnToLobbyAsync(
                        _model.Active.ExpeditionId, _pendingReturnRequestId, token),
                    cancellationToken);
                if (!result.IsSuccess || result.Settlement == null ||
                    !result.Settlement.IsValid ||
                    !string.Equals(
                        _model.Active.ExpeditionId,
                        result.Settlement.ExpeditionId,
                        StringComparison.Ordinal))
                {
                    return Fail(result.IsSuccess
                        ? ExpeditionOperationStatus.InternalError
                        : result.Status);
                }

                _pendingReturnRequestId = string.Empty;
                _pendingDeathRequestId = string.Empty;
                _model.ApplySettlement(result.Settlement);
                Publish(false, ExpeditionOperationStatus.Success, string.Empty);
                return ExpeditionOperationStatus.Success;
            }
            finally
            {
                _isBusy = false;
            }
        }

        public IDisposable Subscribe(IObserver<ExpeditionPresentationState> observer) =>
            _state.Subscribe(observer);

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _lifetime.Cancel();
            _lifetime.Dispose();
            _state.Dispose();
        }

        private bool TryBegin(out ExpeditionOperationStatus rejection)
        {
            if (!_capabilities.Has(NarakaServerCapabilities.Expedition))
            {
                rejection = Fail(ExpeditionOperationStatus.ServerCapabilityMissing);
                return false;
            }

            if (_isBusy)
            {
                rejection = ExpeditionOperationStatus.Conflict;
                return false;
            }

            _isBusy = true;
            Publish(true, ExpeditionOperationStatus.Success, string.Empty);
            rejection = ExpeditionOperationStatus.Success;
            return true;
        }

        private async UniTask<ExpeditionGatewayResult> ExecuteAsync(
            Func<CancellationToken, UniTask<ExpeditionGatewayResult>> operation,
            CancellationToken cancellationToken)
        {
            try
            {
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(
                           cancellationToken, _lifetime.Token))
                {
                    return await operation(linked.Token);
                }
            }
            catch (OperationCanceledException)
            {
                Publish(false, ExpeditionOperationStatus.Success, string.Empty);
                throw;
            }
            catch (Exception)
            {
                return ExpeditionGatewayResult.Failed(ExpeditionOperationStatus.TransportFailure);
            }
        }

        private ExpeditionOperationStatus Fail(ExpeditionOperationStatus status)
        {
            Publish(false, status, ExpeditionOperationMessages.Describe(status));
            return status;
        }

        private static bool IsActiveSnapshot(ExpeditionSnapshot snapshot) =>
            snapshot != null && snapshot.State == ExpeditionState.Active;

        private void Publish(bool isBusy, ExpeditionOperationStatus status, string message)
        {
            if (_disposed)
            {
                return;
            }

            _state.Set(new ExpeditionPresentationState(
                isBusy,
                _model.HasActive,
                _model.Active,
                _model.LastDeath,
                _model.LastSettlement,
                status,
                message));
        }
    }
}
