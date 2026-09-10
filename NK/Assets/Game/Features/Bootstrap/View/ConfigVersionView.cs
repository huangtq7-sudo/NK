using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Naraka.Core.Application.MVC;
using Naraka.Features.Bootstrap.Controller;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace Naraka.Features.Bootstrap.View
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class ConfigVersionView :
        MonoBehaviour,
        IView<ConfigVersionPresentationState>,
        IObserver<ConfigVersionPresentationState>
    {
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private ConfigVersionController _controller;
        private IDisposable _subscription;
        private VisualElement _overlay;
        private Label _status;
        private Button _retry;
        private bool _overlayVisible = true;

        [Inject]
        public void Construct(ConfigVersionController controller)
        {
            _controller = controller;
        }

        private void Start()
        {
            Build(GetComponent<UIDocument>().rootVisualElement);
            _subscription = _controller.Subscribe(this);
        }

        public void Render(ConfigVersionPresentationState state)
        {
            if (_overlay == null)
            {
                return;
            }

            _overlayVisible = state.Phase != ConfigVersionPhase.Ready;
            _overlay.style.display = _overlayVisible ? DisplayStyle.Flex : DisplayStyle.None;
            if (_overlayVisible)
            {
                KeepOverlayOnTop();
            }

            _status.text = state.Message;
            _retry.style.display = state.Phase == ConfigVersionPhase.Blocked
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            _retry.SetEnabled(state.Phase == ConfigVersionPhase.Blocked);
        }

        public void OnNext(ConfigVersionPresentationState value) => Render(value);

        public void OnError(Exception error)
        {
            if (_status != null)
            {
                _status.text = "版本界面发生错误。";
            }
        }

        public void OnCompleted()
        {
        }

        private void Build(VisualElement root)
        {
            _overlay = new VisualElement { name = "ConfigVersionOverlay" };
            _overlay.style.position = Position.Absolute;
            _overlay.style.left = 0;
            _overlay.style.right = 0;
            _overlay.style.top = 0;
            _overlay.style.bottom = 0;
            _overlay.style.justifyContent = Justify.Center;
            _overlay.style.alignItems = Align.Center;
            _overlay.style.backgroundColor = new Color(0.025f, 0.032f, 0.05f, 0.98f);

            var card = new VisualElement();
            card.style.width = 500;
            card.style.paddingLeft = 36;
            card.style.paddingRight = 36;
            card.style.paddingTop = 30;
            card.style.paddingBottom = 30;
            card.style.backgroundColor = new Color(0.08f, 0.105f, 0.15f, 1f);

            var title = new Label("NARAKA 启动检查");
            title.style.fontSize = 26;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.unityTextAlign = TextAnchor.MiddleCenter;
            title.style.color = new Color(0.88f, 0.74f, 0.42f);
            title.style.marginBottom = 18;
            card.Add(title);

            _status = new Label();
            _status.style.whiteSpace = WhiteSpace.Normal;
            _status.style.unityTextAlign = TextAnchor.MiddleCenter;
            _status.style.color = new Color(0.9f, 0.9f, 0.9f);
            _status.style.marginBottom = 18;
            card.Add(_status);

            _retry = new Button(() => RetryAsync().Forget())
            {
                name = "ConfigVersionRetryButton",
                text = "重新检查"
            };
            _retry.style.width = 180;
            _retry.style.alignSelf = Align.Center;
            card.Add(_retry);

            _overlay.Add(card);
            root.Add(_overlay);

            // 其他View在各自的Start中向同一个root追加元素，追加顺序取决于组件顺序。
            // 版本预检覆盖层必须始终位于登录界面之上，因此在本帧所有Start完成后再确认一次层级。
            _overlay.schedule.Execute(KeepOverlayOnTop);
        }

        private void KeepOverlayOnTop()
        {
            if (_overlay != null && _overlayVisible)
            {
                _overlay.BringToFront();
            }
        }

        private async UniTaskVoid RetryAsync()
        {
            await _controller.CheckAsync(_lifetime.Token);
        }

        private void OnDestroy()
        {
            _subscription?.Dispose();
            _lifetime.Cancel();
            _lifetime.Dispose();
        }
    }
}
