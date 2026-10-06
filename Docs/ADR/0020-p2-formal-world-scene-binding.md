# ADR-0020：正式运行场景绑定、第三方源场景只读，以及稳定 MapId 与 Unity 场景名分离

- 状态：已接受
- 日期：2026-10-06

## 背景

P2.2 结束时，游戏实际跑的是两个灰盒场景：

- `Map01_Task` 里是 `GrayboxGround` 一块灰色地板、两个出生点和一个传送门；
- `Map02_CombatGraybox` 里是灰色地板、三个训练假人和一只正式暮影妖狼。

这不是加载器故障，而是当时的正确结果：`NARAKA_开发规范.md` §11 写的是
"地图素材确定前只创建抽象测试场景，不固定正式坐标"，`NARAKA_完整玩法设计.md` §13
也写着"地图素材确定前不承诺最终地形、坐标和场景分区"。
[Q-005](../../NARAKA_待确认问题.md) 当时的状态就是"明确延期"。

素材现在到位了，工程里有两个可用的第三方环境包：

| 用途 | 源场景 | 体积 |
| --- | --- | --- |
| 任务地图 | `Assets/Aquarius Fantasy - High Elves/Demo Scenes/High Elves Sanctuary/High Elves Sanctuary.unity` | 2.27 MB，750 个 Prefab 实例 |
| 战斗地图 | `Assets/PureNature/Scenes/Scene_Demo/Scene_Demo.unity` | 38.9 MB，10,909 个 Prefab 实例，外加 61 MB 地形 |

## 为什么不能继续在灰盒场景上开发 P3

P3 要做远征闭环：远征实例、临时掉落、死亡清理、异常结算与幂等。
这些功能的正确性全都依赖"地图是什么样的"这件事：

1. **坐标与空间关系是远征的输入。** 掉落点、死亡位置、重生点、脱战距离、
   怪物回家点都要写进远征快照与数据库。在一块无限平坦的灰盒地板上定出来的坐标，
   换成真实地形之后一个都不成立 —— 真实地形有坡度、水面、悬崖和不可到达区域。
   先做 P3 等于先把一批注定要重算的数值写进数据库迁移。
2. **NavMesh 在灰盒上是退化的。** 灰盒地板上的寻路永远成功，因此"路径被地形阻断
   之后怪物怎么办""玩家脱战自动寻路走不通怎么办"这两类缺陷在灰盒上**不可能复现**。
   而它们正是 P3 要处理的异常分支。
3. **性能基线不具代表性。** [P2 性能记录](../Performance/p2-combat-performance-record.md)
   里渲染线程、GPU 与总帧预算本来就是"未验证"，而灰盒场景的 GPU 数据即使测出来也没有意义：
   真实环境有植被、阴影、反射探针与后处理。先按灰盒定预算，等于给 P3 一个假的余量。

因此顺序必须是：先把正式环境接进运行流程，再做 P3。

## 决策一：第三方源场景保持只读，业务跑在项目自有的派生场景上

第三方源场景与其原始资源**一行都不改**。理由不是洁癖：

- 素材包会升级。一旦在源场景里挂了 NARAKA 组件，下次更新素材包就会在
  "保留我们的改动"和"拿到新版本"之间二选一。
- 源场景是**演示**场景，它的职责是展示素材，带着演示相机、演示漫游脚本和自己的
  AudioListener。把业务挂进去等于让演示逻辑和业务逻辑共享一个文件。
- `Assets/Aquarius Fantasy - High Elves` 与 `Assets/PureNature` 在
  [ADR-0003](0003-embedded-unity-packages.md) 里已经是受保护路径。

所以运行场景是项目自有的两个文件：

| 业务地图 | Unity 场景 | 正式环境来源 |
| --- | --- | --- |
| `Map01` | `Assets/Game/Scenes/World/Map01_Task.unity` | High Elves Sanctuary |
| `Map02` | `Assets/Game/Scenes/World/Map02_Combat.unity` | Pure Nature Scene_Demo |

### 派生机制：用"另存为"，不用"搬对象"

`P23WorldSceneSetup` 的做法是：

```
OpenScene(第三方源场景, Single)   →  改内存里的副本  →  SaveScene(scene, 项目路径)
```

源 `.unity` 文件从头到尾没有被写过一次（工具在派生前后比对源文件的字节数与写入时间，
不一致就直接抛异常）。

**为什么不是"把环境对象搬进项目场景"。** 这是实现时验证过的一个关键差别：
两个源场景都带 `m_LightingDataAsset`（烘焙光照数据），而烘焙光照、反射探针与
光照探针的数据都是**场景级**的，按场景内对象引用建立索引。
`MoveGameObjectToScene` 把渲染器搬到另一个场景之后，这套绑定就断了 ——
环境会丢掉间接光与 AO，变得明显发平。
"另存为"保留整份场景数据，包括 LightingDataAsset 引用、4–6 个反射探针、
光照探针组，以及一万多个 Prefab 实例的修改列表。

**为什么不是"把环境做成 Prefab"。** 同上，外加 Terrain 与场景级光照设置
放进 Prefab 之后一样会丢绑定。

**代价要说清楚**：`Map02_Combat.unity` 是一份约 39 MB 的项目自有场景，
与同样 39 MB 的源场景并存，仓库因此多出约 40 MB（走 Git LFS）。
这是"派生场景"方案的固有成本。唯一能避免它的方案是运行时附加加载源场景，
但那要求把第三方 Demo 源场景加进 Build Settings，是本轮明确禁止的。

### 派生场景里剥掉什么

| 内容 | 处理 | 理由 |
| --- | --- | --- |
| 演示相机 | 移除 | 相机唯一归属是持久化 App Root 的 `CameraRig` |
| `AudioListener` | 移除 | 同一时刻只允许一个，否则 Unity 直接报警 |
| `FreeCamera`（漫游输入脚本） | 移除 | 演示控制逻辑不进 NARAKA |
| `EventSystem` | 移除 | 项目 UI 已有一个 |
| 场景自有 Missing Script | 移除 | 会刷错误日志，也会让场景契约测试失败 |
| `ElvenRotator`、`BobbingObject` | **保留** | 它们让法阵旋转、浮空物浮动，属于环境呈现本身，不碰任何业务状态；删掉只会让圣殿变成静物 |
| 后处理 `Volume` | **保留** | 是环境预期观感的一部分，不是演示控制逻辑 |

Prefab 实例内部的组件有一条额外规则：Unity **不允许**销毁 Prefab 实例内部的
GameObject，而演示相机恰好藏在一个 Prefab 实例里。拆包 Prefab 不是选项（会把引用
展开成内联数据，场景体积会爆），因此 Prefab 内部只移除**组件**（Unity 把它记作实例的
"已移除组件"覆写），普通场景对象才整个移除。两者对"运行时不存在第二台相机"等价。

### 幂等与失败安全

- 只有目标场景**还没有** `Environment` 根节点时才从源场景派生。已经派生过的场景
  只做业务层补齐，因此重复执行不会产生第二套摄像机、玩家、灯光、出生点、传送门或怪物，
  也不会冲掉用户手工调过的出生点位置。彻底重建是显式的 Rebuild 菜单项。
- 派生路径在"环境已就位 + 业务已装配"之后**一次性**写盘。中途任何一步抛异常，
  目标场景文件都还是上一次的样子。这条在实现过程中真的被触发过一次
  （销毁 Prefab 实例内部对象的异常），目标场景确实没有被改写。

## 决策二：稳定业务 MapId 与 Unity 场景名分离

P2.2 的 `WorldMapIds` 把两者混在一起：

```csharp
public const string Map01Task = "Map01_Task";
public const string Map02CombatGraybox = "Map02_CombatGraybox";
```

`WorldPresentationState.CurrentMapId` 存的就是这个值，也就是 Unity 场景文件名。
这在只有灰盒场景时没出问题，本轮立刻就不成立了：战斗场景从
`Map02_CombatGraybox` 换成 `Map02_Combat`，如果业务 ID 等于场景名，
**换一次美术素材就改变了地图的业务身份**。

而这个字段按路线图会流向 P3 的远征记录、服务端协议与数据库。
`expedition.map_id = 'Map02_CombatGraybox'` 这种行一旦写进去，
以后每次换场景文件名都要做一次数据迁移，而且历史数据会指向一个不存在的场景。

### 拆法

`Naraka.Core.Application.Scenes` 下三件东西：

```csharp
WorldMapIds      // 业务身份：Map01、Map02
WorldSceneNames  // Unity 资源：Map01_Task、Map02_Combat、Map02_CombatGraybox
IWorldSceneCatalog / WorldSceneCatalog.Default   // 只读映射
```

放在 `Game.Core.Application` 而不是 Infrastructure，有两个具体原因：
它 `noEngineReferences: true`，因此不存在 SceneManager 泄漏到 Model/Controller 的可能；
而且它已经被 World.Controller、Infrastructure.Scene、Boot 和三个测试程序集全部引用，
所以生产用的那份映射可以被测试**直接断言**，不需要测试自己再造一张表
——否则"Map01 映射到 Map01_Task"就会有两份真相。

### 翻译只发生一次

`WorldFlowController` 全程只持有 MapId，只在把请求交给 `ILoadingController` 的那一行
换成场景名：

```csharp
var sceneName = _sceneCatalog.ResolveSceneName(mapId);   // 唯一的翻译点
await _loading.LoadSceneAsync(sceneName, linked.Token);
SetState(new WorldPresentationState(mapId, arrival, false));   // 写进状态的是 MapId
```

解析刻意放在"把界面切进正在加载"**之前**：未登记的 MapId 必须在加载界面亮起来之前
就失败，否则玩家会看到一个为不存在的目标转圈的进度条。

既有边界一个没动：`ISceneLoader` 仍然只认场景名，`LoadingController` 仍然只负责
真实进度与最短显示时长，`ILobbySceneGateway` 仍然是大厅与世界之间那一个方法。
大厅只转发一个它从不解释的标识符，因此 `LobbyController` 与它的测试没有被改写。

## 决策三：正式场景与开发回退场景的边界

`Map02_CombatGraybox` 保留，但只能是**开发回退资产**。保证它不参与正常流程的手段
不是约定，而是三条机械约束：

1. 它不是任何 `WorldMapIds` 的解析结果 —— `WorldSceneCatalog.Default` 里根本没有它，
   所以 `WorldFlowController` 无论拿到什么 MapId 都到不了它。
2. 它在 Build Settings 里被**禁用**。禁用之后不计入
   `SceneManager.sceneCountInBuildSettings`，而 `UnitySceneLoader.BeginLoad`
   会先查这张表再加载，因此运行时根本找不到它。
3. 有 EditMode 契约测试守住以上两条。

它和正式战斗场景共用业务 MapId `Map02`：MapId 表达"这是地图二"，
哪个场景文件承载它由目录决定 —— 这正是本 ADR 第二条决策要的效果。

训练假人保留开发用途，但集中到明确的 DevOnly 根节点下并默认停用，
正式流程不依赖它。

## 决策四：仍然不引入 Addressables

[ADR-0016](0016-p2-persistent-app-root-and-real-scene-progress.md) 已经把
Addressables 推到 P5，本轮不改这个结论，而且本轮的情况进一步支持它：

- 正式环境是**本地**素材，没有远端资源、没有 CDN、没有 A/B 回滚需求。
- 场景体积变大（39 MB）不是引入资源管线的理由。真正需要 Addressables 的
  是按需下载与版本回滚，而不是"文件比较大"。
- `ISceneLoader` / `ISceneLoadOperation` 这层抽象仍然是迁移边界。
  Addressables 的 `SceneInstance` 同样能提供"进度 + 就绪 + 激活 + 等待完成"
  这四件事，届时只换一个 Infrastructure 实现，Controller 与 View 不用动。
- 本轮**新增**了一个迁移时的注意点：`UnitySceneLoader.IsSceneInBuild` 依赖
  Build Settings 这张表。换成 Addressables 之后"场景是否可加载"的判定方式会变，
  而灰盒回退场景的隔离目前正是靠这张表实现的，迁移时必须一并重做这条约束。

## 对 P3 远征协议与数据库的约束

本 ADR 对 P3 留下三条硬约束：

1. **远征、数据库与协议里的地图字段只能存 `WorldMapIds` 的值**（`Map01`、`Map02`），
   不得存 Unity 场景名、场景列表下标或场景 GUID。
2. **`WorldSceneNames` 不得被 Model、Controller 或任何领域对象引用。**
   它只属于场景加载边界、Editor 装配工具与场景契约测试。
3. 新增地图时先在 `WorldSceneCatalog.Default` 登记一对映射，再建场景文件。
   把场景文件名直接写进业务代码的做法本 ADR 之后不再允许。

## 补充：权威摆位优先于地形采样

初版把出生点、重生点与传送门按**地形归一化坐标**采样落到地表，并写明那是
"起点不是设计结论"。用户实测后的反馈是"看不到传送点"，于是规则改成：

**人确认过的绝对坐标是权威，优先于地形采样，并且每次装配都强制应用。**

地图一的三个位置因此写进 `P23WorldSceneSetup.AuthoredPlacements`
（入口与重生点 (510, 35, 587) 朝向 y = -90、传送门 (385.1, 36.1, 602.5)），
由 EditMode 契约测试盯着。这组值本身也迭代过一轮：第一次给的 y = -13 在地表以下，
由用户在 Unity 里看过之后更正 —— 这恰好说明为什么这类坐标必须是"人定的"而不是"工具猜的"。地图二的怪物生成点仍走地形采样，并保留
"只落还在世界原点附近的对象"这条规则，因此手工微调不会被冲掉。

两种规则并存是有意的：**有人定过的坐标，工具就不再猜；没人定过的，工具给个能用的起点。**
代价是地图一那三个位置不能在 Unity 里拖着改 —— 要改就改那张表，
否则下次装配会摆回去。这个取舍换来的是坐标有唯一权威来源。

顺带修掉的一个表现缺陷：传送门的灰盒标识方块以父级原点为中心、高 2.4，
而父级原点就在地表上，所以它一半埋在地下。现在沿本地 Y 抬半个身位站在地面上。

## 结果

- 大厅"开始游戏"→ `Map01` → High Elves Sanctuary；传送门 → `Map02` → Pure Nature。
  加载进度仍然来自真实 `AsyncOperation`，真实就绪前不显示 100%。
- 业务状态里不再出现任何 Unity 场景名。
- 灰盒场景从"正常流程目标"降级为"被两条机械约束隔离的开发回退资产"。
- 代价：仓库多出约 40 MB 派生场景数据；`SpawnerName` 常量仍叫
  `GrayboxWolfSpawner`（它现在生成的是正式狼），改名会断开既有场景对象与测试的查找，
  本轮不改，作为命名遗留登记。
- 本轮**不包含**：P3 远征、ExpeditionId、临时背包、掉落、死亡资产清理、
  返回大厅结算、断线结算、数据库迁移与服务端协议。
