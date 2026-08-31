using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Naraka.Core.Application.MVC;
using Naraka.Core.Application.Presentation;
using Naraka.Features.Account.Model;

namespace Naraka.Features.Account.Controller
{
    public sealed class AccountController : IController, IReadOnlyState<AccountPresentationState>
    {
        private readonly IAccountGateway _gateway;
        private readonly AccountSessionModel _model;
        private readonly List<IObserver<AccountPresentationState>> _observers =
            new List<IObserver<AccountPresentationState>>();

        public AccountController(IAccountGateway gateway, AccountSessionModel model)
        {
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            _model = model ?? throw new ArgumentNullException(nameof(model));
            Current = new AccountPresentationState(AccountFlowPhase.Ready, string.Empty, 0, string.Empty);
        }

        public AccountPresentationState Current { get; private set; }

        public async UniTask RegisterAsync(string username, string password, CancellationToken cancellationToken)
        {
            if (!TryValidate(username, password, out var normalizedUsername, out var validationMessage))
            {
                Publish(new AccountPresentationState(AccountFlowPhase.Failed, normalizedUsername, 0, validationMessage));
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

        public IDisposable Subscribe(IObserver<AccountPresentationState> observer)
        {
            if (observer == null)
            {
                throw new ArgumentNullException(nameof(observer));
            }

            _observers.Add(observer);
            observer.OnNext(Current);
            return new Subscription(_observers, observer);
        }

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

        private void Publish(AccountPresentationState state)
        {
            Current = state;
            foreach (var observer in _observers.ToArray())
            {
                observer.OnNext(state);
            }
        }

        private sealed class Subscription : IDisposable
        {
            private readonly List<IObserver<AccountPresentationState>> _observers;
            private IObserver<AccountPresentationState> _observer;

            public Subscription(
                List<IObserver<AccountPresentationState>> observers,
                IObserver<AccountPresentationState> observer)
            {
                _observers = observers;
                _observer = observer;
            }

            public void Dispose()
            {
                if (_observer == null)
                {
                    return;
                }

                _observers.Remove(_observer);
                _observer = null;
            }
        }
    }
}
