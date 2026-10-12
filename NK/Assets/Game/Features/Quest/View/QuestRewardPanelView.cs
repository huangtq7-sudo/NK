using Naraka.Features.Quest.Controller;
using Naraka.Infrastructure.Camera;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using VContainer;

namespace Naraka.Features.Quest.View
{
    /// <summary>
    /// 任务奖励领取面板。显示配置中的奖励预览，领取按钮只发出"领取"意图；
    /// 实际发放数量以服务端领取摘要为准，面板不改写任何资产。
    ///
    /// 战斗场景平时锁定并隐藏指针。面板打开期间暂停视角并释放指针，以便点击按钮；
    /// 关闭后恢复。角色输入由交互控制器的输入锁屏蔽，世界不会暂停。
    /// </summary>
    public sealed class QuestRewardPanelView : MonoBehaviour
    {
        [Tooltip("面板根对象（本组件的子物体）。")]
        [SerializeField] private GameObject root;

        [SerializeField] private Text titleLabel;

        [Tooltip("奖励列表，每项一行。")]
        [SerializeField] private Text rewardsLabel;

        [SerializeField] private Button claimButton;

        [SerializeField] private Button closeButton;

        [Tooltip("可选。领取请求在途时显示。")]
        [SerializeField] private GameObject busyRoot;

        private INpcInteractionController _interaction;
        private ThirdPersonCameraRig _cameraRig;
        private InputAction _escape;
        private bool _renderedOpen = true;
        private bool _renderedBusy = true;
        private string _renderedQuest;
        private bool _lookSuspended;

        [Inject]
        public void Construct(INpcInteractionController interaction, IObjectResolver resolver)
        {
            _interaction = interaction;

            // 镜头装配缺失时面板仍可显示，只是无法释放指针；不因为可选依赖让整张地图注入失败。
            resolver.TryResolve(out _cameraRig);
        }

        private void OnEnable()
        {
            EnsureEventSystem();
            _escape = new InputAction("RewardPanelClose", InputActionType.Button, "<Keyboard>/escape");
            _escape.Enable();
            if (claimButton != null)
            {
                claimButton.onClick.AddListener(OnClaimClicked);
            }

            if (closeButton != null)
            {
                closeButton.onClick.AddListener(OnCloseClicked);
            }

            SetOpen(false);
        }

        private void OnDisable()
        {
            if (claimButton != null)
            {
                claimButton.onClick.RemoveListener(OnClaimClicked);
            }

            if (closeButton != null)
            {
                closeButton.onClick.RemoveListener(OnCloseClicked);
            }

            if (_escape != null)
            {
                _escape.Disable();
                _escape.Dispose();
                _escape = null;
            }

            ResumeLook();
        }

        private void Update()
        {
            if (_interaction == null)
            {
                return;
            }

            var state = _interaction.Current;
            SetOpen(state.IsRewardPanelOpen);
            if (!state.IsRewardPanelOpen)
            {
                return;
            }

            if (state.RewardQuestId != _renderedQuest)
            {
                _renderedQuest = state.RewardQuestId;
                if (titleLabel != null)
                {
                    titleLabel.text = state.RewardTitle;
                }

                if (rewardsLabel != null)
                {
                    rewardsLabel.text = string.Join("\n", state.RewardLines);
                }
            }

            if (state.IsClaimInFlight != _renderedBusy)
            {
                _renderedBusy = state.IsClaimInFlight;
                if (busyRoot != null)
                {
                    busyRoot.SetActive(state.IsClaimInFlight);
                }

                if (claimButton != null)
                {
                    claimButton.interactable = !state.IsClaimInFlight;
                }
            }

            if (_escape != null && _escape.WasPressedThisFrame())
            {
                _interaction.CloseRewardPanel();
            }
        }

        /// <summary>
        /// 按钮点击需要 EventSystem。大厅的 EventSystem 随大厅场景卸载，正式地图场景也不保存 EventSystem
        /// （场景派生工具会移除它），因此缺少时在本场景创建一个，随地图一起卸载；已有时不重复创建。
        /// </summary>
        private void EnsureEventSystem()
        {
            if (EventSystem.current != null || FindObjectOfType<EventSystem>() != null)
            {
                return;
            }

            var eventSystem = new GameObject("QuestEventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(eventSystem, gameObject.scene);
        }

        private void OnClaimClicked() => _interaction?.RequestClaim();

        private void OnCloseClicked() => _interaction?.CloseRewardPanel();

        private void SetOpen(bool open)
        {
            if (open == _renderedOpen)
            {
                return;
            }

            _renderedOpen = open;
            _renderedQuest = null;
            if (root != null && root != gameObject)
            {
                root.SetActive(open);
            }

            if (open)
            {
                SuspendLook();
            }
            else
            {
                ResumeLook();
            }
        }

        private void SuspendLook()
        {
            if (_cameraRig == null || _lookSuspended)
            {
                return;
            }

            _lookSuspended = true;
            _cameraRig.SetLookEnabled(false);
        }

        private void ResumeLook()
        {
            if (!_lookSuspended)
            {
                return;
            }

            _lookSuspended = false;
            if (_cameraRig != null)
            {
                _cameraRig.SetLookEnabled(true);
            }
        }
    }
}
