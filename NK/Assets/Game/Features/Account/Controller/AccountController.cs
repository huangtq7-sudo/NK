using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Naraka.Core.Application.Bootstrap;
using Naraka.Core.Application.Messaging;
using Naraka.Core.Application.MVC;
using Naraka.Core.Application.Presentation;
using Naraka.Features.Account.Model;

namespace Naraka.Features.Account.Controller
{
    public sealed class AccountController : IController, IReadOnlyState<AccountPresentationState>, IDisposable
    {
        private readonly IAccountGateway _gateway;
        private readonly AccountSessionModel _model;
        private readonly IDomainEventBus _events;
        private readonly IStartupReadiness _startupReadiness;
        private readonly ReactiveState<AccountPresentationState> _state;

        public AccountController(
            IAccountGateway gateway,
            AccountSessionModel model,
            IDomainEventBus events,
            IStartupReadiness startupReadiness)
        {
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _events = events ?? throw new ArgumentNullException(nameof(events));
            _startupReadiness = startupReadiness ??
                throw new ArgumentNullException(nameof(startupReadiness));
            _state = new ReactiveState<AccountPresentationState>(
                new AccountPresentationState(AccountFlowPhase.Ready, string.Empty, 0, string.Empty));
        }

        public AccountPresentationState Current => _state.Current;

        public async UniTask RegisterAsync(string username, string password, CancellationToken cancellationToken)
        {
            if (!TryValidate(username, password, out var normalizedUsername, out var validationMessage))
            {
                Publish(new AccountPresentationState(AccountFlowPhase.Failed, normalizedUsername, 0, validationMessage));
                return;
            }

            if (!TryPassStartupGate(normalizedUsername))
            {
                return;
            }

            Publish(new AccountPresentationState(
                AccountFlowPhase.Registering,
                normalizedUsername,
                0,
                "正在创建账号…"));

            try
            {
                var result = await _gateway.RegisterAsync(normalizedUsername, password, cancellationToken);
                await UniTask.SwitchToMainThread();
                var message = result.Status switch
                {
                    AccountRegistrationStatus.Success => "注册成功，可以登录。",
                    AccountRegistrationStatus.AlreadyExists => "账号已存在。",
                    AccountRegistrationStatus.Forbidden => "当前账号禁止注册。",
                    _ => "注册失败，请稍后重试。"
                };
                var phase = result.Status == AccountRegistrationStatus.Success
                    ? AccountFlowPhase.Ready
                    : AccountFlowPhase.Failed;
                Publish(new AccountPresentationState(phase, normalizedUsername, 0, message));
            }
            catch (Exception exception) when (!(exception is OperationCanceledException))
            {
                await UniTask.SwitchToMainThread();
                Publish(new AccountPresentationState(
                    AccountFlowPhase.Failed,
                    normalizedUsername,
                    0,
                    ToSafeNetworkMessage(exception)));
            }
        }

        public async UniTask LoginAsync(string username, string password, CancellationToken cancellationToken)
        {
            if (!TryValidate(username, password, out var normalizedUsername, out var validationMessage))
            {
                Publish(new AccountPresentationState(AccountFlowPhase.Failed, normalizedUsername, 0, validationMessage));
                return;
            }

            if (!TryPassStartupGate(normalizedUsername))
            {
                return;
            }

            Publish(new AccountPresentationState(
                AccountFlowPhase.LoggingIn,
                normalizedUsername,
                0,
                "正在登录…"));

            try
            {
                var result = await _gateway.LoginAsync(normalizedUsername, password, cancellationToken);
                await UniTask.SwitchToMainThread();
                if (result.Status == AccountLoginStatus.Success && result.AccountId > 0)
                {
                    _model.Authenticate(normalizedUsername, result.AccountId);
                    _events.Publish(new AccountAuthenticatedEvent(_model.Username, _model.AccountId));
                    Publish(new AccountPresentationState(
                        AccountFlowPhase.Authenticated,
                        _model.Username,
                        _model.AccountId,
                        "登录成功。"));
                    return;
                }

                var message = result.Status switch
                {
                    AccountLoginStatus.WrongPassword => "密码错误。",
                    AccountLoginStatus.UserNotFound => "账号不存在。",
                    AccountLoginStatus.Timeout => "登录超时，请重试。",
                    _ => "登录失败，请稍后重试。"
                };
                Publish(new AccountPresentationState(AccountFlowPhase.Failed, normalizedUsername, 0, message));
            }
            catch (Exception exception) when (!(exception is OperationCanceledException))
            {
                await UniTask.SwitchToMainThread();
                Publish(new AccountPresentationState(
                    AccountFlowPhase.Failed,
                    normalizedUsername,
                    0,
                    ToSafeNetworkMessage(exception)));
            }
        }

        public IDisposable Subscribe(IObserver<AccountPresentationState> observer) =>
            _state.Subscribe(observer);

        public void Dispose() => _state.Dispose();

        private static bool TryValidate(
            string username,
            string password,
            out string normalizedUsername,
            out string message)
        {
            normalizedUsername = username?.Trim() ?? string.Empty;
            if (normalizedUsername.Length < 3 || normalizedUsername.Length > 64)
            {
                message = "账号长度必须为 3–64 个字符。";
                return false;
            }

            if (string.IsNullOrEmpty(password) || password.Length < 10 || password.Length > 128)
            {
                message = "密码长度必须为 10–128 个字符。";
                return false;
            }

            message = string.Empty;
            return true;
        }

        private static string ToSafeNetworkMessage(Exception exception) =>
            exception is TimeoutException
                ? "请求超时，请检查服务器连接。"
                : "无法连接服务器，请稍后重试。";

        private bool TryPassStartupGate(string username)
        {
            if (_startupReadiness.IsReady)
            {
                return true;
            }

            Publish(new AccountPresentationState(
                AccountFlowPhase.Failed,
                username,
                0,
                _startupReadiness.BlockingReason));
            return false;
        }

        private void Publish(AccountPresentationState state) => _state.Set(state);
    }
}
