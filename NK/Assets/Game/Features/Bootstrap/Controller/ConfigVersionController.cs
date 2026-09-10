using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Naraka.Core.Application.Bootstrap;
using Naraka.Core.Application.MVC;
using Naraka.Core.Application.Presentation;
using Naraka.Features.Bootstrap.Model;

namespace Naraka.Features.Bootstrap.Controller
{
    public sealed class ConfigVersionController :
        IController,
        IStartupReadiness,
        IReadOnlyState<ConfigVersionPresentationState>,
        IDisposable
    {
        private readonly IConfigVersionGateway _gateway;
        private readonly ConfigVersionModel _model;
        private readonly ServerCapabilityRegistry _capabilities;
        private readonly ReactiveState<ConfigVersionPresentationState> _state;

        public ConfigVersionController(
            IConfigVersionGateway gateway,
            ConfigVersionModel model,
            ServerCapabilityRegistry capabilities)
        {
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
            _state = new ReactiveState<ConfigVersionPresentationState>(
                new ConfigVersionPresentationState(
                    ConfigVersionPhase.Pending,
                    _model.ConfigVersion,
                    string.Empty,
                    "正在准备版本检查…"));
        }

        public ConfigVersionPresentationState Current => _state.Current;

        public bool IsReady => Current.Phase == ConfigVersionPhase.Ready;

        public string BlockingReason => IsReady
            ? string.Empty
            : string.IsNullOrEmpty(Current.Message)
                ? "版本检查尚未完成。"
                : Current.Message;

        public async UniTask CheckAsync(CancellationToken cancellationToken)
        {
            if (Current.Phase == ConfigVersionPhase.Checking)
            {
                return;
            }

            Publish(new ConfigVersionPresentationState(
                ConfigVersionPhase.Checking,
                _model.ConfigVersion,
                string.Empty,
                "正在检查客户端与配置版本…"));

            // 重新预检可能指向另一台服务器，先清空上一次的能力集合，
            // 否则失败重试期间仍会按旧服务器的能力放行业务请求。
            _capabilities.Reset();

            try
            {
                var manifest = await _gateway.GetRequiredVersionAsync(cancellationToken);
                await UniTask.SwitchToMainThread();
                var compatibility = _model.Evaluate(manifest);
                if (compatibility.IsCompatible)
                {
                    // 只有兼容性通过才记录能力：不兼容时客户端根本不该进入业务流程。
                    _capabilities.Resolve(manifest.ServerCapabilities);
                    Publish(new ConfigVersionPresentationState(
                        ConfigVersionPhase.Ready,
                        _model.ConfigVersion,
                        manifest.ConfigVersion,
                        "版本检查通过。"));
                    return;
                }

                Publish(new ConfigVersionPresentationState(
                    ConfigVersionPhase.Blocked,
                    _model.ConfigVersion,
                    manifest.ConfigVersion,
                    ToBlockingMessage(compatibility.Status)));
            }
            catch (Exception exception) when (!(exception is OperationCanceledException))
            {
                await UniTask.SwitchToMainThread();
                Publish(new ConfigVersionPresentationState(
                    ConfigVersionPhase.Blocked,
                    _model.ConfigVersion,
                    string.Empty,
                    exception is TimeoutException
                        ? "版本检查超时，请重试。"
                        : "无法连接版本服务，请检查服务器后重试。"));
            }
        }

        public IDisposable Subscribe(IObserver<ConfigVersionPresentationState> observer) =>
            _state.Subscribe(observer);

        public void Dispose() => _state.Dispose();

        private void Publish(ConfigVersionPresentationState state) => _state.Set(state);

        private static string ToBlockingMessage(ConfigCompatibilityStatus status) => status switch
        {
            ConfigCompatibilityStatus.ConfigVersionMismatch => "本地配置版本与服务器不一致，需要更新配置。",
            ConfigCompatibilityStatus.ClientVersionTooOld => "客户端版本过旧，请更新客户端。",
            ConfigCompatibilityStatus.ClientVersionTooNew => "客户端版本超出当前服务器兼容范围。",
            ConfigCompatibilityStatus.ProtocolVersionMismatch => "客户端协议版本与服务器不兼容。",
            _ => "服务器返回的版本清单无效。"
        };
    }
}
