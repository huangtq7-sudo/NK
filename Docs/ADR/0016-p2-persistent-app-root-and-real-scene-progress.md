# ADR-0016：持久化 App Root、真实异步场景进度，以及 P2 暂缓 Addressables

- 状态：已接受
- 日期：2026-09-26

## 背景

P1 结束时客户端只有一个组合根：`GameLifetimeScope`，挂在 Bootstrap 场景
（`Assets/Scenes/SampleScene.unity`）上，没有 `DontDestroyOnLoad`。
`LoadingView` 和大厅的十几个面板共用同一个 `UIDocument`，挂在 `P0ClientShell` 上。

这套结构在"只有一个场景"的时候没问题，进入 P2 立刻不成立：

- 用 `LoadSceneMode.Single` 加载地图会连带销毁 `GameLifetimeScope` 与 `LoadingView`。
  于是加载界面在"卸载大厅"和"地图已就绪"之间消失，玩家看到的是黑屏而不是进度条；
- `LoadingController` 的进度完全由"经过时间 ÷ 最短时长"算出来，与真实
  `AsyncOperation` 无关。它看起来像进度条，实际上是个计时器；
- `ILobbySceneGateway` 直接 `SceneManager.LoadSceneAsync` 并让 `allowSceneActivation = true`，
  因此没有任何地方能"等资源就绪但先别切场景"；
- 大厅注入的目标场景名是 `"Map1"`，一个并不存在的场景。P1 把"加载地图失败，请重试。"
  记录为保留的兜底行为，这其实是它唯一能走到的分支。

## 决策

### 独立的持久化 App Root，而不是把整个 GameLifetimeScope 持久化

新增 `AppRootLifetimeScope`（`DontDestroyOnLoad`，`[DefaultExecutionOrder(-10000)]`），
只承载真正需要活过场景切换的能力：

```
IGameClock / ISceneLoader / LoadingController / WorldFlowController /
IHitResolver / IPlayerController / IPlayerInputSource / ThirdPersonCameraRig / LoadingView
```

`GameLifetimeScope` 成为它的**子 Scope**（`FindParent()` 返回它），继续承载账号与大厅的
全部控制器，并随 Bootstrap 场景卸载。

为什么不直接给 `GameLifetimeScope` 加 `DontDestroyOnLoad`：它注册了 14 个大厅控制器与
12 个可选 View。把它整体带进战斗场景会同时违反两条要求——"不重复创建 Composition Root"
和"场景切换必须释放场景级订阅"——而且那些用 `RegisterComponentInHierarchy` 注册的 View
在切场景后会变成已销毁引用。

地图场景各有一个 `WorldSceneLifetimeScope`，同样是 App Root 的子 Scope，
只注册本场景的入口与传送门，场景卸载时只销毁本层。

`AppRootLifetimeScope` 带单例守卫：重复加载 Bootstrap 时后来的那个先停用再销毁自己
（`FindObjectOfType` 不返回停用对象，因此子 Scope 不会在同一帧挂到一个正在销毁的父容器上）。
生产流程不会重复加载 Bootstrap，但"场景切换后不得出现重复组合根"本来就是硬要求，
而 PlayMode 测试会真的这么做。

`LoadingView` 从 `P0ClientShell` 移到 App Root 下，使用自己的 `UIDocument`
（共用 `P0PanelSettings`，`sortingOrder = 100`）。`LoadingScreen.uxml` / `.uss` 不变，
只做了状态接入。

### 真实异步进度

新增 `Naraka.Core.Application.Scenes` 抽象：

```csharp
interface ISceneLoadOperation { float Progress; bool IsReadyToActivate; void Activate();
                                UniTask WaitForCompletionAsync(CancellationToken ct); }
interface ISceneLoader        { ISceneLoadOperation BeginLoad(string sceneName); }
```

`UnitySceneLoader`（Infrastructure）实现它：`allowSceneActivation = false`，
并把 Unity 的 0–0.9 归一化成 0–1（`progress / 0.9`），因此进度条不会永远停在 90%。

`LoadingController.LoadSceneAsync` 负责编排：

1. 显示加载界面；
2. 显示进度取"真实进度"与"时间进度"的**较小值**；
3. 资源未就绪、或最短显示时长（2 秒）未走完时，进度封顶 **99%**；
4. 两个条件都满足才 `Activate()`，然后等真正切换完成，最后隐藏界面；
5. 结果做单调处理，真实进度的抖动不让进度条往回跳。

第 2 条初版写的是**较大值**，那是个错误。灰盒场景的
`AsyncOperation.progress` 几乎瞬间就到 0.9（归一化后即 1），取较大值会让进度条
一开机就顶到 99%，然后干等两秒 —— 那不是加载条，是个占位图。
取较小值之后：秒加载完的场景由时间驱动，进度条在最短显示时长内平滑地从 0 爬到 100%；
真的加载很久的场景由真实进度驱动；任何情况下都不会在真实加载完成之前显示 100%。

失败时把界面收回、写入 `HasError` 与明确文案、进度归零，并由 Infrastructure 记录异常日志。
卡在 99% 的加载条等于告诉玩家"再等等"，而实际上不会再有任何进展。

View 永远不接触 `AsyncOperation`；`LoadingController` 只读它暴露的进度与就绪标记。

### 大厅按钮不需要知道加载界面存在

`WorldFlowController` 同时实现 `IWorldFlowController` 与既有的 `ILobbySceneGateway`。
`LobbyController.StartGameAsync` 与它的测试因此一行都不用改：它继续调用
`_sceneGateway.LoadMapAsync(sceneName, ct)`，而实现已经换成"显示加载界面 + 真实进度 +
出场动画调度"。注入的场景名由失效的 `"Map1"` 纠正为 `"Map01_Task"`。

重复触发防护有两层：`WorldPresentationState.IsTransitioning` 对外可见，
内部 `_isTransitioning` 保证即使同一帧连续调用也只创建一个切换任务。

### 场景切换的 CancellationToken 归持久层所有

这是实现过程中由 PlayMode 测试暴露出来的一个真实缺陷，值得写进决策：

`MapPortal` 与 `WorldSceneEntry` 最初把**自己场景的** `CancellationToken` 传给
`EnterMap02Async` / `ReturnToMap01AfterDeathAsync`。但这次切换会把它们所在的场景一起卸载，
于是它们在 `OnDestroy` 里取消了正在进行的切换：场景已经加载成功，
`_pendingArrival` 却被取消分支清掉，结果出场动画永远不播。

**规则：发起场景切换的场景级对象不得持有该切换的取消权。** 切换的生命周期归持久化的
`WorldFlowController`（它有自己的 `_lifetime`）。场景级 CTS 只用于场景级的等待。

同理，`LoadingController`、`WorldFlowController` 与 `PlayerController` 在释放之后都不再写
状态源：容器在切换途中被销毁（例如退出应用）属于正常情况，继续回调一个已释放的
`ReactiveState` 只会抛 `ObjectDisposedException`。`PlayerCharacterView` 也会在
`_controller.IsDisposed` 时停掉自己。

### Addressables 明确暂缓

P2 不引入 Addressables。场景继续通过 `ISceneLoader` 抽象后的
`SceneManager.LoadSceneAsync` 加载。

`ISceneLoader` / `ISceneLoadOperation` 这两个接口**就是**后续迁移边界：
Addressables 的 `SceneInstance` 同样能提供"进度 + 就绪 + 激活 + 等待完成"这四件事，
届时只需要换一个 Infrastructure 实现，Controller 与 View 不用改。

理由是本阶段没有远端资源、没有 CDN、也没有 A/B 回滚需求，
提前引入一整套资源管线只会让场景加载多一层无法用现有测试覆盖的间接。
Addressables 仍按 `NARAKA_技术架构.md` 的规划留在 P5。

### P2 第一阶段的范围调整

原路线图里两图灰盒、固定传送门、异步加载与死亡返回属于 P3。本阶段把它们提前，
因为移动、镜头与状态机如果不在真实场景流转下验收，"相机重新绑定到新玩家"、
"传送门只触发一次"、"死亡后只返回一次"这三类缺陷根本暴露不出来
（事实上这次就暴露了上面那个 CancellationToken 缺陷）。

本阶段仍然不做：远征结算、临时掉落、数据库写入、异常结算与幂等。
那些依赖服务端权威，留在 P3。

## 结果

- 加载界面在 Bootstrap、地图一、地图二之间连续可见，既不丢失也不重复。
- 进度条反映真实 `AsyncOperation`，且在真实完成之前不会显示 100%。
- 最短显示时长（2 秒）与真实加载时长取较大者，不闪屏也不提前切场景。
- 大厅业务代码与它的测试没有因为这套改动被重写。
- 代价：多了一个组合根层级。为此 PlayMode 测试专门断言了
  "只有一个 App Root""只有一个相机""每张地图只有一个场景 Scope"。
