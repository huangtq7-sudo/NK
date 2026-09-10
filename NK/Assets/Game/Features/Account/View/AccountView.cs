using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Naraka.Core.Application.MVC;
using Naraka.Features.Account.Controller;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace Naraka.Features.Account.View
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class AccountView : MonoBehaviour, IView<AccountPresentationState>, IObserver<AccountPresentationState>
    {
        private const string BusyModifierClass = "account-panel--busy";
        private const string FailedModifierClass = "account-panel--failed";

        [SerializeField] private VisualTreeAsset loginLayout;

        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private AccountController _controller;
        private IDisposable _subscription;
        private VisualElement _screen;
        private VisualElement _panel;
        private TextField _username;
        private TextField _password;
        private Button _register;
        private Button _login;
        private Label _status;

        [Inject]
        public void Construct(AccountController controller)
        {
            _controller = controller;
        }

        private void Start()
        {
            if (!TryBuild(GetComponent<UIDocument>().rootVisualElement))
            {
                return;
            }

            _subscription = _controller.Subscribe(this);
        }

        public void Render(AccountPresentationState state)
        {
            if (_panel == null)
            {
                return;
            }

            var authenticated = state.Phase == AccountFlowPhase.Authenticated;
            var display = authenticated ? DisplayStyle.None : DisplayStyle.Flex;
            _screen.style.display = display;
            _panel.style.display = display;
            if (authenticated)
            {
                _password.value = string.Empty;
            }

            _username.SetEnabled(!state.IsBusy);
            _password.SetEnabled(!state.IsBusy);
            _register.SetEnabled(!state.IsBusy);
            _login.SetEnabled(!state.IsBusy);
            _panel.EnableInClassList(BusyModifierClass, state.IsBusy);
            _panel.EnableInClassList(FailedModifierClass, state.Phase == AccountFlowPhase.Failed);
            _status.text = state.Message;
            if (!string.IsNullOrEmpty(state.Username) && string.IsNullOrEmpty(_username.value))
            {
                _username.value = state.Username;
            }
        }

        public void OnNext(AccountPresentationState value) => Render(value);

        public void OnError(Exception error)
        {
            if (_status != null)
            {
                _status.text = "账号界面发生错误。";
            }
        }

        public void OnCompleted()
        {
        }

        private bool TryBuild(VisualElement root)
        {
            if (loginLayout == null)
            {
                Debug.LogError(
                    "AccountView 未绑定 AccountLogin.uxml；请运行菜单 NARAKA/Setup/Apply P0 Project Settings 重新装配启动场景。",
                    this);
                return false;
            }

            loginLayout.CloneTree(root);
            _screen = root.Q<VisualElement>("AccountScreen");
            _panel = root.Q<VisualElement>("AccountPanel");
            _username = root.Q<TextField>("UsernameField");
            _password = root.Q<TextField>("PasswordField");
            _register = root.Q<Button>("RegisterButton");
            _login = root.Q<Button>("LoginButton");
            _status = root.Q<Label>("AccountStatusLabel");
            if (_screen == null || _panel == null || _username == null || _password == null ||
                _register == null || _login == null || _status == null)
            {
                Debug.LogError("AccountLogin.uxml 缺少登录界面必需的元素名称，登录界面未能装配。", this);
                _panel = null;
                return false;
            }

            _password.isPasswordField = true;
            _register.clicked += OnRegisterClicked;
            _login.clicked += OnLoginClicked;
            return true;
        }

        private void OnRegisterClicked() => RegisterAsync().Forget();

        private void OnLoginClicked() => LoginAsync().Forget();

        private async UniTaskVoid RegisterAsync()
        {
            await _controller.RegisterAsync(_username.value, _password.value, _lifetime.Token);
        }

        private async UniTaskVoid LoginAsync()
        {
            await _controller.LoginAsync(_username.value, _password.value, _lifetime.Token);
        }

        private void OnDestroy()
        {
            if (_register != null)
            {
                _register.clicked -= OnRegisterClicked;
            }

            if (_login != null)
            {
                _login.clicked -= OnLoginClicked;
            }

            _screen?.RemoveFromHierarchy();
            _subscription?.Dispose();
            _lifetime.Cancel();
            _lifetime.Dispose();
        }
    }
}
