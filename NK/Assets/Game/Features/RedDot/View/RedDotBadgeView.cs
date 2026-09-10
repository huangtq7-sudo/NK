using System;
using System.Collections.Generic;
using Naraka.Core.Application.MVC;
using Naraka.Core.Application.RedDot;
using Naraka.Features.RedDot.Controller;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace Naraka.Features.RedDot.View
{
    /// <summary>
    /// 大厅入口上的红点角标。
    ///
    /// 这个视图<b>只</b>订阅 <see cref="RedDotPresentationState"/>，不认识签到、抽奖或社交。
    /// 它把大厅按钮名映射到一个红点节点，然后按"该节点或它的任意后代是否亮起"显示角标——
    /// 因此新增一个子节点（例如一段新会话）不需要改动这里。
    ///
    /// 好友的在线绿点/离线灰点不走这里：那是状态显示，不是"有未处理内容"。
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class RedDotBadgeView :
        MonoBehaviour,
        IView<RedDotPresentationState>,
        IObserver<RedDotPresentationState>
    {
        /// <summary>大厅按钮名 → 红点节点。角标按整棵子树聚合显示。</summary>
        private static readonly KeyValuePair<string, string>[] BadgeTargets =
        {
            new KeyValuePair<string, string>("InventoryButton", RedDotPath.Inventory),
            new KeyValuePair<string, string>("ForgeButton", RedDotPath.Forge),
            new KeyValuePair<string, string>("DrawButton", RedDotPath.Gacha),
            new KeyValuePair<string, string>("CheckInButton", RedDotPath.SignIn),
            new KeyValuePair<string, string>("AccountLevelRewardButton", RedDotPath.AccountLevel),
            new KeyValuePair<string, string>("FriendsButton", RedDotPath.Social),
            new KeyValuePair<string, string>("ChatButton", RedDotPath.SocialUnreadChat)
        };

        private readonly List<KeyValuePair<string, VisualElement>> _badges =
            new List<KeyValuePair<string, VisualElement>>();

        private IRedDotController _controller;
        private IDisposable _subscription;

        [Inject]
        public void Construct(IRedDotController controller) => _controller = controller;

        private void Start()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            if (root == null)
            {
                return;
            }

            foreach (var target in BadgeTargets)
            {
                var button = root.Q<VisualElement>(target.Key);
                if (button == null)
                {
                    // 该入口在当前布局里不存在时静默跳过：红点不是必需功能，
                    // 不应该因为少一个按钮就报错刷屏。
                    continue;
                }

                var badge = new VisualElement { name = target.Key + "RedDot" };
                badge.AddToClassList("reddot-badge");
                badge.pickingMode = PickingMode.Ignore;
                badge.style.display = DisplayStyle.None;
                button.Add(badge);
                _badges.Add(new KeyValuePair<string, VisualElement>(target.Value, badge));
            }

            _subscription = _controller.Subscribe(this);
        }

        public void Render(RedDotPresentationState state)
        {
            foreach (var entry in _badges)
            {
                // 自身亮起，或子树里还有亮起的叶子。两者都没有时不显示任何角标。
                var isActive = state.IsActive(entry.Key) || state.HasActiveDescendant(entry.Key);
                entry.Value.style.display = isActive ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        public void OnNext(RedDotPresentationState value) => Render(value);

        public void OnError(Exception error)
        {
        }

        public void OnCompleted()
        {
        }

        private void OnDestroy()
        {
            foreach (var entry in _badges)
            {
                entry.Value?.RemoveFromHierarchy();
            }

            _badges.Clear();
            _subscription?.Dispose();
        }
    }
}
