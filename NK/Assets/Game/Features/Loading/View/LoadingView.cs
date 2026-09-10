using System;
using Naraka.Core.Application.MVC;
using Naraka.Features.Loading.Controller;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace Naraka.Features.Loading.View
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class LoadingView :
        MonoBehaviour,
        IView<LoadingPresentationState>,
        IObserver<LoadingPresentationState>
    {
        [SerializeField] private VisualTreeAsset loadingLayout;

        private ILoadingController _controller;
        private IDisposable _subscription;
        private VisualElement _screen;
        private VisualElement _fill;
        private Label _percent;
        private bool _wasVisible;

        [Inject]
        public void Construct(ILoadingController controller)
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

        public void Render(LoadingPresentationState state)
        {
            if (_screen == null)
            {
                return;
            }

            _screen.style.display = state.IsVisible ? DisplayStyle.Flex : DisplayStyle.None;
            if (!state.IsVisible)
            {
                _wasVisible = false;
                return;
            }

            if (!_wasVisible)
            {
                _wasVisible = true;
                // 加载界面必须盖住登录页与大厅；只在转为可见时提层，避免每帧重排。
                _screen.BringToFront();
            }

            var percent = state.Progress * 100f;
            _fill.style.width = Length.Percent(percent);
            _percent.text = Mathf.RoundToInt(percent) + "%";
        }

        public void OnNext(LoadingPresentationState value) => Render(value);

        public void OnError(Exception error)
        {
        }

        public void OnCompleted()
        {
        }

        private bool TryBuild(VisualElement root)
        {
            if (loadingLayout == null)
            {
                Debug.LogError(
                    "LoadingView 未绑定 LoadingScreen.uxml；请运行菜单 NARAKA/Setup/Apply P0 Project Settings。",
                    this);
                return false;
            }

            loadingLayout.CloneTree(root);
            _screen = root.Q<VisualElement>("LoadingScreen");
            _fill = _screen?.Q<VisualElement>("LoadingBarFill");
            _percent = _screen?.Q<Label>("LoadingPercentLabel");
            if (_screen == null || _fill == null || _percent == null)
            {
                Debug.LogError("LoadingScreen.uxml 缺少必需的元素名称，加载界面未能装配。", this);
                _screen = null;
                return false;
            }

            _screen.style.display = DisplayStyle.None;
            return true;
        }

        private void OnDestroy()
        {
            _screen?.RemoveFromHierarchy();
            _subscription?.Dispose();
        }
    }
}
