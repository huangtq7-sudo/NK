using System;
using Naraka.Core.Application.MVC;
using Naraka.Features.Lobby.Controller;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace Naraka.Features.Lobby.View
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class LobbyView : MonoBehaviour, IView<LobbyPresentationState>, IObserver<LobbyPresentationState>
    {
        private LobbyController _controller;
        private IDisposable _subscription;
        private VisualElement _panel;
        private Label _identity;

        [Inject]
        public void Construct(LobbyController controller)
        {
            _controller = controller;
        }

        private void Start()
        {
            Build(GetComponent<UIDocument>().rootVisualElement);
            _subscription = _controller.Subscribe(this);
        }

        public void Render(LobbyPresentationState state)
        {
            if (_panel == null)
            {
                return;
            }

            _panel.style.display = state.IsVisible ? DisplayStyle.Flex : DisplayStyle.None;
            _identity.text = state.IsVisible
                ? $"账号：{state.Username}  ·  ID {state.AccountId}"
                : string.Empty;
        }

        public void OnNext(LobbyPresentationState value) => Render(value);

        public void OnError(Exception error)
        {
        }

        public void OnCompleted()
        {
        }

        private void Build(VisualElement root)
        {
            _panel = new VisualElement { name = "LobbyPanel" };
            _panel.style.position = Position.Absolute;
            _panel.style.left = 0;
            _panel.style.right = 0;
            _panel.style.top = 0;
            _panel.style.bottom = 0;
            _panel.style.paddingLeft = 48;
            _panel.style.paddingRight = 48;
            _panel.style.paddingTop = 38;
            _panel.style.paddingBottom = 38;
            _panel.style.backgroundColor = new Color(0.025f, 0.035f, 0.055f, 0.98f);

            var title = new Label("空大厅 · P0");
            title.style.fontSize = 32;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = new Color(0.88f, 0.74f, 0.42f);
            _panel.Add(title);

            _identity = new Label();
            _identity.style.marginTop = 12;
            _identity.style.fontSize = 17;
            _panel.Add(_identity);

            var placeholder = new Label("账号登录闭环已完成。英雄、武器、仓库与远征入口将在后续 P0/P4 模块接入。");
            placeholder.style.marginTop = 48;
            placeholder.style.fontSize = 18;
            placeholder.style.whiteSpace = WhiteSpace.Normal;
            placeholder.style.color = new Color(0.78f, 0.8f, 0.84f);
            _panel.Add(placeholder);

            root.Add(_panel);
        }

        private void OnDestroy()
        {
            _subscription?.Dispose();
        }
    }
}
