using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Naraka.Config;
using Naraka.Core.Application.Bootstrap;
using Naraka.Core.Application.Config;
using Naraka.Core.Application.MVC;
using Naraka.Core.Application.Presentation;
using Naraka.Features.Quest.Model;

namespace Naraka.Features.Quest.Controller
{
    public interface IQuestController : IReadOnlyState<QuestPresentationState>
    {
        /// <summary>服务端声明了任务能力且客户端配置可用。为 false 时不发送任何任务协议。</summary>
        bool IsSupported { get; }

        bool IsBusy { get; }

        /// <summary>最近一次服务端日志。尚未同步时为 null。</summary>
        QuestJournalSnapshot Journal { get; }

        int JournalVersion { get; }

        QuestContentCatalog Content { get; }

        UniTask<QuestOperationStatus> RefreshAsync(CancellationToken cancellationToken);

        UniTask<QuestOperationStatus> AcceptAsync(string questId, string dialogueId, CancellationToken cancellationToken);

        UniTask<QuestOperationStatus> CompleteDialogueAsync(
            string questId,
            string objectiveId,
            string dialogueId,
            CancellationToken cancellationToken);

        UniTask<QuestOperationStatus> SubmitAsync(string questId, string dialogueId, CancellationToken cancellationToken);

        UniTask<QuestOperationStatus> ClaimAsync(string questId, CancellationToken cancellationToken);
    }

    /// <summary>
    /// 跨场景任务控制器。任务状态只来自服务端响应：客户端不在本地推进、完成或发奖。
    ///
    /// 同一时间只允许一个任务请求在途。传输失败（结果未知）后再次发起同一意图时复用原 RequestId，
    /// 服务端据此重放首次结果；收到服务端明确响应后，该意图的 RequestId 即作废。
    /// </summary>
    public sealed class QuestController : IController, IQuestController, IDisposable
    {
        private readonly QuestModel _model;
        private readonly IQuestGateway _gateway;
        private readonly IServerCapabilities _capabilities;
        private readonly IGameConfigProvider _config;
        private readonly IQuestRequestIdSource _requestIds;
        private readonly ReactiveState<QuestPresentationState> _state;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly Dictionary<string, string> _pendingRequestIds = new Dictionary<string, string>(StringComparer.Ordinal);
        private QuestContentCatalog _content;
        private QuestOperationStatus _lastStatus = QuestOperationStatus.Success;
        private bool _isBusy;
        private bool _disposed;

        public QuestController(
            QuestModel model,
            IQuestGateway gateway,
            IServerCapabilities capabilities,
            IGameConfigProvider config,
            IQuestRequestIdSource requestIds)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _requestIds = requestIds ?? throw new ArgumentNullException(nameof(requestIds));
            _state = new ReactiveState<QuestPresentationState>(QuestPresentationState.Initial);
        }

        public QuestPresentationState Current => _state.Current;

        public bool IsSupported => _capabilities.Has(NarakaServerCapabilities.Quest) && Content != null;

        public bool IsBusy => _isBusy;

        public QuestJournalSnapshot Journal => _model.Journal;

        public int JournalVersion => _model.Version;

        public QuestContentCatalog Content
        {
            get
            {
                if (_content == null && _config.IsLoaded && _config.Catalog != null)
                {
                    _content = new QuestContentCatalog(_config.Catalog.Catalog);
                }

                return _content;
            }
        }

        public UniTask<QuestOperationStatus> RefreshAsync(CancellationToken cancellationToken) =>
            RunAsync(
                string.Empty,
                (requestId, token) => _gateway.GetJournalAsync(requestId, token),
                cancellationToken);

        public UniTask<QuestOperationStatus> AcceptAsync(
            string questId,
            string dialogueId,
            CancellationToken cancellationToken) =>
            RunAsync(
                "accept:" + questId,
                (requestId, token) => _gateway.AcceptAsync(requestId, questId, dialogueId, token),
                cancellationToken);

        public UniTask<QuestOperationStatus> CompleteDialogueAsync(
            string questId,
            string objectiveId,
            string dialogueId,
            CancellationToken cancellationToken) =>
            RunAsync(
                "talk:" + questId + ":" + objectiveId,
                (requestId, token) => _gateway.CompleteDialogueAsync(requestId, questId, objectiveId, dialogueId, token),
                cancellationToken);

        public UniTask<QuestOperationStatus> SubmitAsync(
            string questId,
            string dialogueId,
            CancellationToken cancellationToken) =>
            RunAsync(
                "submit:" + questId,
                (requestId, token) => _gateway.SubmitAsync(requestId, questId, dialogueId, token),
                cancellationToken);

        public UniTask<QuestOperationStatus> ClaimAsync(string questId, CancellationToken cancellationToken) =>
            RunAsync(
                "claim:" + questId,
                (requestId, token) => _gateway.ClaimAsync(requestId, questId, token),
                cancellationToken);

        public IDisposable Subscribe(IObserver<QuestPresentationState> observer) => _state.Subscribe(observer);

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

        /// <param name="intentKey">写意图的键。为空表示只读请求，每次使用新 RequestId。</param>
        private async UniTask<QuestOperationStatus> RunAsync(
            string intentKey,
            Func<string, CancellationToken, UniTask<QuestGatewayResult>> operation,
            CancellationToken cancellationToken)
        {
            if (_disposed)
            {
                return QuestOperationStatus.InternalError;
            }

            if (!_capabilities.Has(NarakaServerCapabilities.Quest))
            {
                return Finish(QuestOperationStatus.ServerCapabilityMissing);
            }

            if (Content == null)
            {
                return Finish(QuestOperationStatus.ConfigUnavailable);
            }

            if (_isBusy)
            {
                return QuestOperationStatus.Conflict;
            }

            var isWrite = !string.IsNullOrEmpty(intentKey);
            string requestId;
            if (!isWrite || !_pendingRequestIds.TryGetValue(intentKey, out requestId))
            {
                requestId = _requestIds.NewId();
                if (isWrite)
                {
                    _pendingRequestIds[intentKey] = requestId;
                }
            }

            _isBusy = true;
            Publish();
            QuestGatewayResult result;
            try
            {
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token))
                {
                    result = await operation(requestId, linked.Token);
                }
            }
            catch (OperationCanceledException)
            {
                // 取消时结果未知：保留 RequestId，下一次同一意图会让服务端重放而不是重复执行。
                _isBusy = false;
                Publish();
                throw;
            }
            catch (Exception)
            {
                result = QuestGatewayResult.Failed(QuestOperationStatus.TransportFailure);
            }

            _isBusy = false;
            if (isWrite && result.Status != QuestOperationStatus.TransportFailure)
            {
                _pendingRequestIds.Remove(intentKey);
            }

            if (result.Journal != null)
            {
                _model.ApplyJournal(result.Journal);
            }

            if (result.IsSuccess && result.Claim != null)
            {
                _model.ApplyClaim(result.Claim);
            }

            return Finish(result.Status);
        }

        private QuestOperationStatus Finish(QuestOperationStatus status)
        {
            _lastStatus = status;
            Publish();
            return status;
        }

        private void Publish()
        {
            if (_disposed)
            {
                return;
            }

            _state.Set(QuestPresentationState.Build(
                _isBusy,
                IsSupported,
                _model.Journal,
                Content,
                _model.LastClaim,
                _lastStatus));
        }
    }
}
