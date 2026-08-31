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
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private AccountController _controller;
        private IDisposable _subscription;
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
            Build(GetComponent<UIDocument>().rootVisualElement);
            _subscription = _controller.Subscribe(this);
        }

        public void Render(AccountPresentationState state)
        {
            if (_panel == null)
            {
                return;
            }

            _panel.style.display = state.Phase == AccountFlowPhase.Authenticated
                ? DisplayStyle.None
                : DisplayStyle.Flex;
            if (state.Phase == AccountFlowPhase.Authenticated)
            {
                _password.value = string.Empty;
            }

            _username.SetEnabled(!state.IsBusy);
            _password.SetEnabled(!state.IsBusy);
            _register.SetEnabled(!state.IsBusy);
            _login.SetEnabled(!state.IsBusy);
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

        private void Build(VisualElement root)
        {
            root.style.flexGrow = 1;
            root.style.justifyContent = Justify.Center;
            root.style.alignItems = Align.Center;
            root.style.backgroundColor = new Color(0.035f, 0.047f, 0.07f, 0.96f);

            _panel = new VisualElement { name = "AccountPanel" };
            _panel.style.width = 460;
            _panel.style.paddingLeft = 36;
            _panel.style.paddingRight = 36;
            _panel.style.paddingTop = 32;
            _panel.style.paddingBottom = 32;
            _panel.style.backgroundColor = new Color(0.08f, 0.105f, 0.15f, 0.98f);
            _panel.style.borderTopLeftRadius = 8;
            _panel.style.borderTopRightRadius = 8;
            _panel.style.borderBottomLeftRadius = 8;
            _panel.style.borderBottomRightRadius = 8;

            var title = new Label("NARAKA");
            title.style.fontSize = 34;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = new Color(0.88f, 0.74f, 0.42f);
            title.style.marginBottom = 24;
            title.style.unityTextAlign = TextAnchor.MiddleCenter;
            _panel.Add(title);

            _username = new TextField("账号") { name = "UsernameField" };
            _username.style.marginBottom = 12;
            _panel.Add(_username);

            _password = new TextField("密码")
            {
                name = "PasswordField",
                isPasswordField = true
            };
            _password.style.marginBottom = 18;
            _panel.Add(_password);

            var actions = new VisualElement();
            actions.style.flexDirection = FlexDirection.Row;
            actions.style.justifyContent = Justify.SpaceBetween;

            _register = new Button(() => RegisterAsync().Forget()) { text = "注册" };
            _register.style.width = 180;
            actions.Add(_register);

            _login = new Button(() => LoginAsync().Forget()) { text = "登录" };
            _login.style.width = 180;
            actions.Add(_login);
            _panel.Add(actions);

            _status = new Label();
            _status.style.marginTop = 18;
            _status.style.minHeight = 22;
            _status.style.whiteSpace = WhiteSpace.Normal;
            _status.style.color = new Color(0.9f, 0.9f, 0.9f);
            _panel.Add(_status);
            root.Add(_panel);
        }

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
            _subscription?.Dispose();
            _lifetime.Cancel();
            _lifetime.Dispose();
        }
    }
}
