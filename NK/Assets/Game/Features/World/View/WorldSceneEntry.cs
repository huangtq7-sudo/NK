using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Naraka.Core.Application.Scenes;
using Naraka.Features.Character.Model;
using Naraka.Features.Character.View;
using Naraka.Features.World.Controller;
using Naraka.Infrastructure.Camera;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Naraka.Features.World.View
{
    /// <summary>
    /// 世界场景入口控制器。
    ///
    /// 每张地图只有一个：它负责在正确的出生点生成玩家、把相机重新绑定到这个新玩家、
    /// 按到达方式播放出场动画，并在动画结束后恢复输入。
    ///
    /// 相机与输入都属于跨场景的持久对象，因此这里只做"重新绑定"，
    /// 不创建第二台相机，也不创建第二个组合根。
    /// </summary>
    public sealed class WorldSceneEntry : MonoBehaviour
    {
        [Tooltip("稳定业务地图 ID，不是 Unity 场景名。")]
        [SerializeField] private string mapId = WorldMapIds.Map01;

        [SerializeField] private GameObject playerPrefab;

        [Tooltip("没有找到对应出生点时使用的兜底位置。")]
        [SerializeField] private Transform fallbackSpawn;

        [Tooltip("勾选后本场景在死亡动画结束时异步返回地图一。地图一自己也勾选：死亡后回到重生点。")]
        [SerializeField] private bool returnToMap01OnDeath = true;

        private IObjectResolver _resolver;
        private IWorldFlowController _world;
        private ThirdPersonCameraRig _cameraRig;
        private CancellationTokenSource _lifetime;
        private PlayerCharacterView _player;
        private bool _deathHandled;

        public string MapId => mapId;

        public PlayerCharacterView Player => _player;

        /// <summary>本次进入任务界面播放的出场色幕；没有播放或已播完销毁时为 null。</summary>
        public EntryScreenFade EntryFade { get; private set; }

        /// <summary>本场景生成玩家的次数。PlayMode 测试用它断言没有重复玩家。</summary>
        public int SpawnCount { get; private set; }

        [Inject]
        public void Construct(
            IObjectResolver resolver,
            IWorldFlowController world,
            ThirdPersonCameraRig cameraRig)
        {
            _resolver = resolver;
            _world = world;
            _cameraRig = cameraRig;
        }

        private void Awake()
        {
            _lifetime = new CancellationTokenSource();
        }

        private void Start()
        {
            if (_resolver == null || _world == null)
            {
                Debug.LogError(
                    "WorldSceneEntry 未完成注入；场景缺少 WorldSceneLifetimeScope 或持久化 App Root。",
                    this);
                return;
            }

            var arrival = _world.ConsumeArrival();
            SpawnPlayerAsync(arrival).Forget();
        }

        private async UniTaskVoid SpawnPlayerAsync(WorldArrival arrival)
        {
            if (playerPrefab == null)
            {
                Debug.LogError("WorldSceneEntry 未绑定玩家 Prefab。", this);
                return;
            }

            var spawn = FindSpawn(arrival);
            var position = spawn != null ? spawn.position : Vector3.zero;
            var rotation = spawn != null ? spawn.rotation : Quaternion.identity;

            // 已经存在玩家时不再生成第二个：场景重载不能累积角色实例。
            _player = FindObjectOfType<PlayerCharacterView>();
            if (_player == null)
            {
                var instance = _resolver.Instantiate(playerPrefab, position, rotation);
                _player = instance.GetComponent<PlayerCharacterView>();
                SpawnCount++;
                if (_player == null)
                {
                    Debug.LogError("玩家 Prefab 上缺少 PlayerCharacterView。", this);
                    return;
                }
            }

            _player.TeleportTo(position, rotation);
            _player.DeathSequenceCompleted -= OnPlayerDeathSequenceCompleted;
            _player.DeathSequenceCompleted += OnPlayerDeathSequenceCompleted;

            // 相机重新绑定到这个新玩家，绝不保留上一场景已销毁的 Transform。
            _cameraRig?.Rebind(_player.transform);

            if (arrival == WorldArrival.DeathToMap01)
            {
                // 重生：生命 100%、护甲 50%、体力回满、F/V 冷却清零、3 秒重生保护。
                _player.Respawn();
                _deathHandled = false;
            }

            var spawnAction = ToSpawnAction(arrival);
            if (spawnAction == ActionState.None)
            {
                _player.SetLoading(false);
                return;
            }

            // 加载界面还盖着时角色保持锁定，镜头先停在出场起点（后上方）。
            // 任务界面（大厅开始游戏进入）与战斗界面（任务界面传送进入）都有出场镜头；
            // 出场色幕只属于任务界面。
            _player.SetLoading(true);
            _cameraRig?.SetLookEnabled(false);
            _cameraRig?.HoldIntroStart();

            try
            {
                // 出场动作必须等加载界面真正撤掉之后才开始。原来在场景 Start 里立刻开始，
                // 新场景头几帧的卡顿与加载界面的最后几帧都会吃掉它 —— 玩家第一眼看到的
                // 已经是动作的后半段，看起来就是"角色突然出现"。
                await UniTask.WaitUntil(
                    () => _player == null || !_world.Current.IsTransitioning,
                    cancellationToken: _lifetime.Token);
                if (_player == null)
                {
                    return;
                }

                _player.SetLoading(false);
                _player.BeginSpawn(spawnAction);

                // 镜头与出场动作同长：镜头落定的那一刻正好交还操作。
                _cameraRig?.PlayIntro(SpawnSeconds(_player.Tuning, spawnAction));

                if (arrival == WorldArrival.LobbyToMap01)
                {
                    PlayEntryFade();
                }

                // 出场动画期间输入保持锁定；结束后由状态机自己回到 Idle 并解锁。
                await UniTask.WaitUntil(
                    () => _player == null || _player.State.Action != spawnAction,
                    cancellationToken: _lifetime.Token);
            }
            catch (OperationCanceledException)
            {
                // 取消也要把视角还回去，否则镜头会永远停在出场时的方位。
            }
            finally
            {
                _cameraRig?.SetLookEnabled(true);
            }
        }

        /// <summary>
        /// 任务界面的出场色幕。在加载界面撤掉的这一刻才出现，不会和加载界面叠在一起。
        /// 参数与出场镜头一起放在 ThirdPersonCameraSettings 里：放在本组件上会给两个正式场景文件
        /// 新增序列化字段，而场景归用户管。
        /// </summary>
        private void PlayEntryFade()
        {
            var settings = _cameraRig != null ? _cameraRig.Settings : null;
            EntryFade = settings != null
                ? EntryScreenFade.Play(
                    settings.IntroFadeColor, settings.IntroFadeStartAlpha, settings.IntroFadeSeconds)
                : EntryScreenFade.Play(Color.white, 0.6f, 1.5f);
        }

        private void OnPlayerDeathSequenceCompleted()
        {
            if (_deathHandled || !returnToMap01OnDeath)
            {
                return;
            }

            // 死亡返回只允许触发一次，重复的动画完成信号被这里挡住。
            _deathHandled = true;
            ReturnToMap01Async().Forget();
        }

        private async UniTaskVoid ReturnToMap01Async()
        {
            try
            {
                // 这里刻意不传本场景的 CancellationToken。这次切换会把本场景连带
                // 这个入口对象一起卸载，用它自己的 token 等于让切换在半途取消自己：
                // 场景已经加载成功，到达状态却被取消分支清掉，出场动画就再也不会播。
                // 切换的生命周期归持久化的 WorldFlowController 管。
                await _world.ReturnToMap01AfterDeathAsync(CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                Debug.LogError("死亡后返回地图一失败。");
                Debug.LogException(exception);
                _deathHandled = false;
            }
        }

        /// <summary>
        /// 给用户后续手工制作的“返回大厅”按钮绑定。这里只提供公开入口，不创建或修改任何UI。
        /// </summary>
        public void RequestReturnToLobby()
        {
            ReturnToLobbyAsync().Forget();
        }

        private async UniTaskVoid ReturnToLobbyAsync()
        {
            if (_world == null)
            {
                Debug.LogError("WorldSceneEntry 未注入 IWorldFlowController，不能返回大厅。", this);
                return;
            }

            try
            {
                await _world.ReturnToLobbyAsync(CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                Debug.LogError("结算远征并返回大厅失败。", this);
                Debug.LogException(exception);
            }
        }

        private Transform FindSpawn(WorldArrival arrival)
        {
            var points = FindObjectsOfType<PlayerSpawnPoint>();
            PlayerSpawnPoint entry = null;
            for (var i = 0; i < points.Length; i++)
            {
                var point = points[i];
                if (point.Matches(arrival))
                {
                    return point.transform;
                }

                if (point.Kind == PlayerSpawnPoint.SpawnKind.Entry)
                {
                    entry = point;
                }
            }

            if (entry != null)
            {
                return entry.transform;
            }

            if (fallbackSpawn == null)
            {
                Debug.LogWarning($"场景 {mapId} 没有出生点，玩家将生成在原点。", this);
            }

            return fallbackSpawn;
        }

        private static float SpawnSeconds(PlayerTuning tuning, ActionState spawnAction) =>
            spawnAction == ActionState.SpawnMap01ToMap02
                ? tuning.Reaction.SpawnMap01ToMap02.RealDurationSeconds
                : tuning.Reaction.SpawnLobbyToMap01.RealDurationSeconds;

        private static ActionState ToSpawnAction(WorldArrival arrival) => arrival switch
        {
            WorldArrival.LobbyToMap01 => ActionState.SpawnLobbyToMap01,
            WorldArrival.Map01ToMap02 => ActionState.SpawnMap01ToMap02,
            _ => ActionState.None
        };

        private void OnDestroy()
        {
            if (_player != null)
            {
                _player.DeathSequenceCompleted -= OnPlayerDeathSequenceCompleted;
            }

            _lifetime?.Cancel();
            _lifetime?.Dispose();
        }
    }
}
