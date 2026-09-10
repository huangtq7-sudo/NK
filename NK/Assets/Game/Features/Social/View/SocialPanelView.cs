using System;
using System.Collections.Generic;
using System.Globalization;
using Naraka.Core.Application.MVC;
using Naraka.Features.Social.Controller;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace Naraka.Features.Social.View
{
    /// <summary>
    /// 好友与一对一聊天界面。
    ///
    /// 四个分页共用一份列表：行的内容与按钮由当前分页决定，因此新增一个分页只是多一个
    /// 构建函数，而不是又一棵界面树。聊天区只在选中会话时显示。
    ///
    /// 在线绿点/离线灰点画在这里，而不是走红点系统：它表达"对方在不在"，不是"有事待处理"。
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class SocialPanelView :
        MonoBehaviour,
        IView<SocialPresentationState>,
        IObserver<SocialPresentationState>
    {
        [SerializeField] private VisualTreeAsset socialLayout;

        private readonly List<KeyValuePair<Button, Action>> _handlers =
            new List<KeyValuePair<Button, Action>>();

        private readonly List<KeyValuePair<Button, Action>> _rowHandlers =
            new List<KeyValuePair<Button, Action>>();

        private ISocialController _controller;
        private IDisposable _subscription;
        private VisualElement _screen;
        private VisualElement _list;
        private VisualElement _chat;
        private VisualElement _chatMessages;
        private VisualElement _searchRow;
        private ScrollView _listScroll;
        private ScrollView _chatScroll;
        private TextField _searchField;
        private TextField _chatInput;
        private Label _searchResult;
        private Label _chatPeer;
        private Label _empty;
        private Label _status;
        private Button _searchButton;
        private Button _searchAdd;
        private Button _chatSend;
        private Button _tabFriends;
        private Button _tabRequests;
        private Button _tabChat;
        private Button _tabBlocked;
        private Button _close;

        private int _builtListSignature;
        private long _builtMessageSignature;

        [Inject]
        public void Construct(ISocialController controller) => _controller = controller;

        private void Start()
        {
            if (!TryBuild(GetComponent<UIDocument>().rootVisualElement))
            {
                return;
            }

            _subscription = _controller.Subscribe(this);
        }

        public void Render(SocialPresentationState state)
        {
            if (_screen == null)
            {
                return;
            }

            _screen.style.display = state.IsOpen ? DisplayStyle.Flex : DisplayStyle.None;
            if (!state.IsOpen)
            {
                return;
            }

            _status.text = state.StatusMessage;
            _status.style.display = string.IsNullOrEmpty(state.StatusMessage)
                ? DisplayStyle.None
                : DisplayStyle.Flex;

            SetSelected(_tabFriends, state.Tab == SocialTab.Friends);
            SetSelected(_tabRequests, state.Tab == SocialTab.Requests);
            SetSelected(_tabChat, state.Tab == SocialTab.Chat);
            SetSelected(_tabBlocked, state.Tab == SocialTab.Blocked);

            // 搜索只在好友分页出现：在黑名单或聊天页搜人只会让人困惑。
            _searchRow.style.display =
                state.Tab == SocialTab.Friends ? DisplayStyle.Flex : DisplayStyle.None;
            _searchButton.SetEnabled(!state.IsBusy);

            var found = state.SearchResult;
            if (!state.HasSearched)
            {
                _searchResult.text = string.Empty;
                _searchAdd.style.display = DisplayStyle.None;
            }
            else if (!found.IsValid)
            {
                _searchResult.text = "未找到该玩家。";
                _searchAdd.style.display = DisplayStyle.None;
            }
            else
            {
                _searchResult.text = found.DisplayName;
                _searchAdd.style.display = DisplayStyle.Flex;
                var alreadyFriend = state.IsFriend(found.AccountId);
                _searchAdd.text = alreadyFriend ? "已是好友" : "申请好友";
                _searchAdd.SetEnabled(!alreadyFriend && !state.IsBusy);
            }

            if (!state.HasServerState)
            {
                _empty.style.display = DisplayStyle.Flex;
                _empty.text = state.IsLoading ? "正在读取好友数据…" : "好友数据暂不可用。";
                _listScroll.style.display = DisplayStyle.None;
                _chat.style.display = DisplayStyle.None;
                return;
            }

            _listScroll.style.display = DisplayStyle.Flex;
            RebuildList(state);
            RenderChat(state);
        }

        public void OnNext(SocialPresentationState value) => Render(value);

        public void OnError(Exception error)
        {
        }

        public void OnCompleted()
        {
        }

        /// <summary>只在列表内容真的变化时重建，避免每帧丢弃并重新分配全部行。</summary>
        private void RebuildList(SocialPresentationState state)
        {
            var signature = (int)state.Tab * 397 ^ (state.IsBusy ? 1 : 0);
            signature = signature * 31 + state.Friends.Count;
            signature = signature * 31 + state.IncomingRequests.Count;
            signature = signature * 31 + state.Blocked.Count;
            signature = signature * 31 + state.Conversations.Count;
            signature = signature * 31 + state.ActiveConversationId.GetHashCode();
            foreach (var friend in state.Friends)
            {
                signature = signature * 31 + friend.AccountId.GetHashCode() + (friend.IsOnline ? 1 : 0);
            }

            foreach (var conversation in state.Conversations)
            {
                signature = signature * 31 +
                            conversation.ConversationId.GetHashCode() +
                            (conversation.HasUnread ? 1 : 0);
            }

            if (signature == _builtListSignature)
            {
                return;
            }

            _builtListSignature = signature;
            ClearRowHandlers();
            _list.Clear();

            switch (state.Tab)
            {
                case SocialTab.Requests:
                    BuildRequests(state);
                    break;
                case SocialTab.Chat:
                    BuildConversations(state);
                    break;
                case SocialTab.Blocked:
                    BuildBlocked(state);
                    break;
                default:
                    BuildFriends(state);
                    break;
            }
        }

        private void BuildFriends(SocialPresentationState state)
        {
            SetEmpty(state.Friends.Count == 0, "还没有好友。搜索玩家名并发送申请吧。");
            foreach (var friend in state.Friends)
            {
                var row = BuildRow(friend, showPresence: true);
                var id = friend.AccountId;
                AddButton(row, "SocialChatWith_" + id, "聊天", true, () => _controller.OpenConversation(id));
                AddButton(row, "SocialRemove_" + id, "删除", !state.IsBusy,
                    () => _controller.RemoveFriend(id), danger: true);
                AddButton(row, "SocialBlock_" + id, "拉黑", !state.IsBusy,
                    () => _controller.Block(id), danger: true);
                _list.Add(row);
            }
        }

        private void BuildRequests(SocialPresentationState state)
        {
            SetEmpty(state.IncomingRequests.Count == 0, "没有待处理的好友申请。");
            foreach (var request in state.IncomingRequests)
            {
                var row = BuildRow(request, showPresence: false);
                var id = request.AccountId;
                AddButton(row, "SocialAccept_" + id, "接受", !state.IsBusy,
                    () => _controller.AcceptRequest(id));
                AddButton(row, "SocialReject_" + id, "拒绝", !state.IsBusy,
                    () => _controller.RejectRequest(id), danger: true);
                _list.Add(row);
            }
        }

        private void BuildConversations(SocialPresentationState state)
        {
            SetEmpty(state.Conversations.Count == 0, "还没有会话。在好友列表里点“聊天”开始。");
            foreach (var conversation in state.Conversations)
            {
                var row = BuildRow(conversation.Peer, showPresence: true);
                if (conversation.ConversationId == state.ActiveConversationId)
                {
                    row.AddToClassList("social-row--selected");
                }

                if (conversation.HasUnread)
                {
                    var note = new Label("未读");
                    note.AddToClassList("social-row-note");
                    note.pickingMode = PickingMode.Ignore;
                    row.Add(note);
                }

                var peerId = conversation.Peer.AccountId;
                AddButton(row, "SocialOpen_" + peerId, "打开", !state.IsBusy,
                    () => _controller.OpenConversation(peerId));
                _list.Add(row);
            }
        }

        private void BuildBlocked(SocialPresentationState state)
        {
            SetEmpty(state.Blocked.Count == 0, "黑名单是空的。");
            foreach (var blocked in state.Blocked)
            {
                var row = BuildRow(blocked, showPresence: false);
                var id = blocked.AccountId;
                AddButton(row, "SocialUnblock_" + id, "解除", !state.IsBusy,
                    () => _controller.Unblock(id));
                _list.Add(row);
            }
        }

        private void RenderChat(SocialPresentationState state)
        {
            if (state.ActiveConversationId <= 0)
            {
                _chat.style.display = DisplayStyle.None;
                return;
            }

            _chat.style.display = DisplayStyle.Flex;
            _chatPeer.text = ResolvePeerName(state);
            _chatSend.SetEnabled(!state.IsBusy);
            _chatInput.SetEnabled(!state.IsBusy);

            long signature = state.ActiveConversationId;
            foreach (var message in state.Messages)
            {
                signature = signature * 31 + message.MessageId;
            }

            if (signature == _builtMessageSignature)
            {
                return;
            }

            _builtMessageSignature = signature;
            _chatMessages.Clear();
            foreach (var message in state.Messages)
            {
                var bubble = new VisualElement
                {
                    name = "SocialMessage_" + message.MessageId.ToString(CultureInfo.InvariantCulture)
                };
                bubble.AddToClassList("social-chat-bubble");
                if (message.SenderAccountId == state.SelfAccountId)
                {
                    bubble.AddToClassList("social-chat-bubble--self");
                }

                bubble.pickingMode = PickingMode.Ignore;
                var text = new Label(message.Body);
                text.AddToClassList("social-chat-bubble-text");
                text.pickingMode = PickingMode.Ignore;
                bubble.Add(text);
                _chatMessages.Add(bubble);
            }

            _chatScroll.scrollOffset = new Vector2(0f, float.MaxValue);
        }

        private static string ResolvePeerName(SocialPresentationState state)
        {
            foreach (var conversation in state.Conversations)
            {
                if (conversation.ConversationId == state.ActiveConversationId)
                {
                    return conversation.Peer.DisplayName;
                }
            }

            return string.Empty;
        }

        private VisualElement BuildRow(SocialPlayerSnapshot player, bool showPresence)
        {
            var row = new VisualElement
            {
                name = "SocialRow_" + player.AccountId.ToString(CultureInfo.InvariantCulture)
            };
            row.AddToClassList("social-row");

            if (showPresence)
            {
                var presence = new VisualElement { name = row.name + "Presence" };
                presence.AddToClassList("social-presence");
                if (player.IsOnline)
                {
                    presence.AddToClassList("social-presence--online");
                }

                presence.pickingMode = PickingMode.Ignore;
                row.Add(presence);
            }

            var name = new Label(player.DisplayName);
            name.AddToClassList("social-row-name");
            name.pickingMode = PickingMode.Ignore;
            row.Add(name);
            return row;
        }

        private void AddButton(
            VisualElement row,
            string name,
            string text,
            bool enabled,
            Action handler,
            bool danger = false)
        {
            var button = new Button { name = name, text = text };
            button.AddToClassList("social-row-button");
            if (danger)
            {
                button.AddToClassList("social-row-button--danger");
            }

            button.SetEnabled(enabled);
            button.clicked += handler;
            _rowHandlers.Add(new KeyValuePair<Button, Action>(button, handler));
            row.Add(button);
        }

        private void SetEmpty(bool isEmpty, string message)
        {
            _empty.style.display = isEmpty ? DisplayStyle.Flex : DisplayStyle.None;
            _empty.text = message;
        }

        private static void SetSelected(Button button, bool isSelected)
        {
            if (isSelected)
            {
                button.AddToClassList("social-tab--selected");
            }
            else
            {
                button.RemoveFromClassList("social-tab--selected");
            }
        }

        private bool TryBuild(VisualElement root)
        {
            if (socialLayout == null)
            {
                Debug.LogError(
                    "SocialPanelView 未绑定 SocialPanel.uxml；请运行菜单 NARAKA/Setup/Apply P0 Project Settings。",
                    this);
                return false;
            }

            socialLayout.CloneTree(root);
            _screen = root.Q<VisualElement>("SocialScreen");
            _list = root.Q<VisualElement>("SocialList");
            _listScroll = root.Q<ScrollView>("SocialListScroll");
            _chat = root.Q<VisualElement>("SocialChat");
            _chatMessages = root.Q<VisualElement>("SocialChatMessages");
            _chatScroll = root.Q<ScrollView>("SocialChatScroll");
            _searchRow = root.Q<VisualElement>("SocialSearchRow");
            _searchField = root.Q<TextField>("SocialSearchField");
            _chatInput = root.Q<TextField>("SocialChatInput");
            _searchResult = root.Q<Label>("SocialSearchResultLabel");
            _chatPeer = root.Q<Label>("SocialChatPeerLabel");
            _empty = root.Q<Label>("SocialEmptyLabel");
            _status = root.Q<Label>("SocialStatusLabel");
            _searchButton = root.Q<Button>("SocialSearchButton");
            _searchAdd = root.Q<Button>("SocialSearchAddButton");
            _chatSend = root.Q<Button>("SocialChatSendButton");
            _tabFriends = root.Q<Button>("SocialTabFriends");
            _tabRequests = root.Q<Button>("SocialTabRequests");
            _tabChat = root.Q<Button>("SocialTabChat");
            _tabBlocked = root.Q<Button>("SocialTabBlocked");
            _close = root.Q<Button>("SocialCloseButton");

            if (_screen == null || _list == null || _listScroll == null || _chat == null ||
                _chatMessages == null || _chatScroll == null || _searchRow == null ||
                _searchField == null || _chatInput == null || _searchResult == null ||
                _chatPeer == null || _empty == null || _status == null || _searchButton == null ||
                _searchAdd == null || _chatSend == null || _tabFriends == null ||
                _tabRequests == null || _tabChat == null || _tabBlocked == null || _close == null)
            {
                Debug.LogError("SocialPanel.uxml 缺少必需的元素名称，好友界面未能装配。", this);
                _screen = null;
                return false;
            }

            _screen.style.display = DisplayStyle.None;
            _chat.style.display = DisplayStyle.None;
            _searchAdd.style.display = DisplayStyle.None;

            Bind(_tabFriends, () => _controller.SelectTab(SocialTab.Friends));
            Bind(_tabRequests, () => _controller.SelectTab(SocialTab.Requests));
            Bind(_tabChat, () => _controller.SelectTab(SocialTab.Chat));
            Bind(_tabBlocked, () => _controller.SelectTab(SocialTab.Blocked));
            Bind(_searchButton, () => _controller.Search(_searchField.value));
            Bind(_searchAdd, () =>
            {
                var found = _controller.Current.SearchResult;
                if (found.IsValid)
                {
                    _controller.SendRequest(found.AccountId);
                }
            });
            Bind(_chatSend, () =>
            {
                _controller.SendMessage(_chatInput.value);
                // 立刻清空输入框，避免玩家以为消息没发出去而再点一次。
                _chatInput.SetValueWithoutNotify(string.Empty);
            });
            Bind(_close, () => _controller.Close());
            return true;
        }

        private void Bind(Button button, Action handler)
        {
            button.clicked += handler;
            _handlers.Add(new KeyValuePair<Button, Action>(button, handler));
        }

        private void ClearRowHandlers()
        {
            foreach (var entry in _rowHandlers)
            {
                entry.Key.clicked -= entry.Value;
            }

            _rowHandlers.Clear();
        }

        private void OnDestroy()
        {
            foreach (var entry in _handlers)
            {
                entry.Key.clicked -= entry.Value;
            }

            _handlers.Clear();
            ClearRowHandlers();
            _screen?.RemoveFromHierarchy();
            _subscription?.Dispose();
        }
    }
}
