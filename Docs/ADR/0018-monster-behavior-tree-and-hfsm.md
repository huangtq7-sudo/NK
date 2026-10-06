# ADR-0018：怪物行为树只选意图，怪物 HFSM 是当前动作的唯一真相

- 状态：已接受
- 日期：2026-09-29

## 背景

`NARAKA_开发规范.md` §8 写着一条容易被当成套话的约束：

> 怪物行为树负责选行动，怪物 HFSM 负责执行行动，两处不能同时保存"当前动作"真相。

这条约束在实现时非常容易被违反，而且违反之后的表现很难归因。最典型的写法是让行为树
在"追击"分支里直接调用 `PlaySkill()`，于是同一时刻有两个地方认为自己知道怪物在干什么：
行为树按 5–10Hz 重新选一次，动作状态机按帧推进。两者一旦不同步，玩家看到的是
"狼挥到一半突然转身跑了"或者"预警亮完却没有伤害"——而这两种现象在日志里都看不出原因。

P2.2 只有一只暮影妖狼，但这套边界必须现在就定死：等到 10 类怪物都写完再改，
等于把每一只怪的行为逻辑重写一遍。

## 决策

### 两层各自的职责

```text
行为树（Game.Features.AI.Model + Monster.Model 里的建树代码）
  只产出 MonsterIntent：Dormant / Patrol / Perceive / Chase /
  NormalAttack / SelectSkill / Recover / Dead
  它不写 MonsterActionState，也不调用任何"开始播放"的方法。

HFSM（MonsterCore 内的动作层）
  MonsterActionState：Idle / Move / Attack / Skill / HitStun / Knockdown / Death
  它是"怪物正在干什么"的唯一真相，每帧推进，按配置时间轴开关命中窗。
```

仲裁规则只有一条，但它就是整个决策的全部：

**动作层不空闲时，行为树的结果不生效。**

`MonsterCore.Tick` 在 `Attack` / `Skill` / `HitStun` / `Death` 状态下直接返回，
连行为树都不会 Tick。因此"正在挥出去的这一下必须打完"不是靠自觉，
而是因为根本没有第二条路径能打断它。EditMode 测试
`TheBehaviorTreeCannotOverrideAnActionInProgress` 直接断言这一点：
玩家瞬移到脱战距离之外，动作照常打完，且 `DecisionCount` 一次都没有增加。

### 行为树是自研的，不引入第三方 AI 包

`Game.Features.AI.Model` 提供 `BehaviorNode<TContext>`、`SelectorNode`、
`SequenceNode`、`ConditionNode`、`ActionNode` 与 `BehaviorTree<TContext>`，
整层 `noEngineReferences: true`，对上下文类型泛型，因此它不认识怪物、玩家或 Unity。

理由与 [ADR-0015](0015-p2-player-hfsm-and-animator-projection.md) 不引入 UnityHFSM 一致：

- 本阶段需要的能力就是"按优先级选一个意图"，一个 Selector 加几个 Condition 就够；
- 不引入新依赖，就不需要再做一次 ADR-0003 式的离线还原安排；
- 最关键的一条：整棵树可以在 EditMode 里直接 Tick 并断言，不需要场景、不需要 PlayMode。

节点在构造时建好一次，决策时只调用委托。行为树因此**不在决策时创建任何节点或集合**，
符合 `NARAKA_技术架构.md` §15 的稳定态 0 GC 目标。

### 决策频率是真的会变的

`BehaviorTree.SetDecisionInterval` 按休眠状态切换间隔：
正常 `1 / DecisionsPerSecond`（配置为 6Hz，校验强制落在 5–10Hz），
休眠固定 1 秒。"远离玩家就降低决策频率"因此是一条可断言的行为，
而不是注释里的承诺——`DormantLowersTheDecisionFrequency` 直接比较前后的间隔。

休眠不是完全停止：完全停下来的怪物在玩家走回来时永远醒不过来。

### 怪物数值进 CSV 配置管线

新增两张源表 `Config/Source/monsters.csv` 与 `monster_skills.csv`，
走 [ADR-0010](0010-csv-config-pipeline.md) 的同一条管线，生成物 `SchemaVersion` 提升为 `1.1.0`。

放进 CSV 而不是 ScriptableObject 的理由：怪物的生命、护甲、防御、攻击力与技能倍率
最终由**服务端权威判定**（掉落与战斗结算在 P3/P4），它们和价格、概率属于同一类数值。
[Q-019](../../NARAKA_待确认问题.md) 里"哪些留在客户端表现配置"的那一半——镜头阻尼、
转向角速度、播放速度——仍然留在 `PlayerTuningAsset`，不进 CSV。

校验在编译期完成，任何一条不成立就直接失败并指到行：

- 感知半径 ≤ 脱离半径 < 休眠距离；攻击距离 ≤ 感知半径；
- `DecisionsPerSecond` 落在 5–10；
- `PhaseHealthRatio` 落在 (0, 1)；
- 颜色标签与可反击标记必须一致：红色不得可反击、金色必须可反击、普通攻击不进技能表；
- `WarningSeconds > 0`，即预警必须真的排在伤害窗口之前。

最后一条特别重要：预警时长为 0 等于"没有预警"，而红色技能没有预警等于耍赖。

### 灰盒数值必须自己承认是灰盒

两张表都有一列 `BalanceStatus`，当前全部是 `P2Graybox`。
`GameConfigCatalogTests` 断言暮影妖狼的这一列不是 `Confirmed`。
这样"没确认的数值被当成已确认设计写进玩法文档"这件事会在测试里失败，
而不是等到有人照着它做平衡。

### 表现层是可替换的灰盒

正式狼模型（Polygonal Creatures Pack 的 Polygonal Wolf）尚未导入，
因此场景里的对象叫 `GrayboxWolf`，是一个方块加一片预警面片。
名字里带 Graybox 是刻意的：做一个"看起来像狼"的替身只会让人误以为美术已经接进来了。

`GrayboxWolfView` 只做三件事：把场景里的距离/角度翻译成 `MonsterSenses`、
把帧输出投影成移动与命中窗、把颜色与预警画出来。换成正式模型时它一行都不用改。

地面寻路使用 Unity 2021.3 **自带**的 NavMesh（`com.unity.modules.ai` 已在包清单里），
没有引入任何第三方 AI 包，也没有新增 `com.unity.ai.navigation`。
没有烘焙导航数据时退回直线推进：灰盒地面是一块平板，直线足够验证追击与脱战，
也不会因为缺少导航数据就整只怪不动。

## 结果

- 怪物的每条规则都有 EditMode 测试：阶段边界、吐息解锁条件、不可反击、
  技能距离/冷却/最近使用抑制、休眠降频、脱战、霸体只免硬直、死亡拒绝一切。
- 加第二只怪只需要加两行 CSV 和一份表现资源；行为树的形状可以按怪复用或另建。
- 代价：自研行为树没有可视化编辑器。等到怪物种类真的变多、策划需要自己调树的时候，
  再评估 Unity Behavior 或第三方方案——那是一次有数据支撑的决策，不是现在的猜测。
- `Knockdown` 状态已经在枚举里但暮影妖狼不会进入。它留给后续精英/首领，
  不是死代码，也不会被当成"已经实现了击倒"。

## 2026-10-05 修订：正式模型接入，表现层的边界经受了一次真实检验

本 ADR 正文写过一句可以被当成空话的承诺：

> `GrayboxWolfView` 只做三件事……换成正式模型时它一行都不用改。

正式暮影妖狼（Polygonal Creatures Pack 的 Polygonal Wolf，Black 外观）接入之后，
这句话基本成立，但不是字面成立 —— 值得把差异记下来。

### 业务路径确实一行都没改

意图选择、HFSM、技能调度、阶段门控、伤害、命中去重、脱战、休眠全部未改动。
`MonsterCore`、`MonsterController`、行为树与两张 CSV 一个字节都没动。
场景装配、生成点上限、HUD 接口也没动。

### 但表现层补了两件事，都是纯表现

1. **Animator 单向投影**。`MonsterFrameOutput.Animation` 这个字段
   从 P2.2 第一天就存在并由 HFSM 产出，只是方块替身没有 Animator、没人消费它。
   新增 `MonsterAnimatorProjector` 把它投到同名 State，形态与玩家的投影器完全一致：
   **7 个 State、0 个 Parameter、0 条 Transition**，默认状态 `Idle`。
   Parameter 与 Transition 是 Animator 自己做决定的入口，因此数量必须是 0 ——
   当前动作的唯一真相仍然在 HFSM。
   `TheAnimationProjectionDoesNotChangeTheHfsmOutput` 直接证明这条边界：
   强行往 Animator 投一个"死亡"动画，HFSM 的动作与意图都不变、狼也没死，
   下一帧投影自己纠回业务状态要求的动画。

2. **颜色反馈从单个 Renderer 改成全部 Renderer**。原来是
   `bodyRenderer = GetComponentInChildren<Renderer>()`，取的是第一个。
   正式模型当前只有 1 个 `SkinnedMeshRenderer`，所以现在看不出区别；
   但"只染第一个"这件事在模型有多个部位时会表现为"身子变红、尾巴没变"，
   而这正是换美术时最容易踩的一脚。改成数组并在 `Awake` 收集
   （排除预警面片，它被染色就看不出预警了），仍然用 `MaterialPropertyBlock`，
   不生成材质实例、颜色没变时整段跳过。

### 类名随之改掉

`GrayboxWolfView` → `DuskshadowWolfView`，`.cs.meta` 的 GUID
（`2df49a30f96d7e7408808ba379d27c05`）原样保留，因此两个 Prefab 的脚本引用都没断。
本 ADR 正文写的是"名字里带 Graybox 是刻意的：做一个看起来像狼的替身
只会让人误以为美术已经接进来了"。正式模型接进来之后，这个名字的刻意就反过来了。

方块替身 `GrayboxWolf.prefab` 仍然由装配工具生成，但只作为**开发回退资产**：
正式资源缺失时还能跑通战斗闭环。正式场景不引用它，并由
`TheSceneSpawnerPointsAtTheOfficialWolfNotTheGraybox` 守住。

### 顺带修掉一个一直是空操作的设置

装配工具里写了两处 `agent.updateRotation = false`，意图是"不让 NavMeshAgent
自己转向，免得和朝向投影打架"。实测 `updateRotation` 在 Unity 2021.3 里
**不是序列化字段**（Prefab 的 YAML 里没有 `m_UpdateRotation`），
所以那两行从来没有写进过资产 —— 它一直是空操作。

一直没出问题，是因为真正拦住 Agent 的是序列化的 `angularSpeed = 0`：
角速度为 0 的 Agent 根本转不动。现在把意图落到真正生效的地方：
`DuskshadowWolfView.Awake` 在运行期关闭 `updateRotation`，
装配工具只负责那个确实会被序列化的 `angularSpeed = 0`，
契约测试也改成断言后者 —— 在 Prefab 上断言前者只会断言 Unity 的运行期默认值。

### 不因为有动画就改规则

素材包里有 `@Breath Attack`，它对应的是**已经存在**的赤瘴吐息技能
（`Config/Source/monster_skills.csv` 里那一条红色、不可反击、半血门控的技能）。
没有因为"包里还有 @Howl / @Pound Attack / @Jump"就给狼加技能，
也没有因为有咬击动画就改普攻的前摇/命中/后摇。
暮影妖狼仍然只有普通攻击与红色吐息两种出手，仍然都不可反击。

动画播放速度一律保持 1。实测片段长度与配置动作时长差 10–14%
（咬击 1.167 vs 1.30、吐息 1.333 vs 1.55、受击 0.667 vs 0.60、死亡 2.000 vs 2.00）。
调速属于表现层适配、是允许的，但按 Q-020 立下的做法（"不擅自发明速度值"）
交人工验收决定，而不是我现在挑一个数。

资源来源、GUID、动画映射与材质适配的完整记录见
[暮影妖狼资源映射说明](../Monster/duskshadow-wolf-asset-mapping.md)。

## 2026-10-05 补充：追击必须停在"能出手"的距离上，而不是停在玩家身上

正式模型的视觉验收通过，但用户报了一个行为问题：

> 怪物攻击角色后，且处于攻击范围内，怪物会处于追击状态推着角色移动，
> 而不是在原地攻击角色，这个可能是碰撞体的问题。

不是碰撞体的问题。碰撞体只是让它看得见。成因是三件事串起来：

1. 普攻冷却是 **2 秒**，而 `IsInNormalAttackRange()` 同时要求"冷却结束"与"在攻击距离内"。
   冷却期间 `NormalAttack` 分支不成立，行为树按优先级落到下一个分支 **`Chase`**。
2. `Chase` 的移动目标是**玩家的坐标本身**，而 View 原来的到达判定用的是
   `arriveDistance = 0.6` —— 比两边胶囊半径之和（狼 0.33 + 玩家 0.32 ≈ 0.65）还小。
   因此"到达"永远判定不出来，狼会一直往玩家身上顶。
3. 玩家每帧都在用 `CharacterController.Move` 落重力，于是被穿透解算挤开。

### 为什么不能让 Model 按配置的攻击距离自己停

这是本次最值得记下来的一条。几个实测数字：

| 项 | 值 |
| --- | --- |
| 配置的攻击距离（`monsters.csv`） | **3.2** |
| 狼命中盒的实际触达 | `localOffset.z` 1.2 + `radius` 1.4 + 玩家胶囊半径 0.32 = **2.92** |
| 物理接触距离 | ≈ 0.65 |

也就是说**"在 3.2 出手"本身就打不到玩家**。原来之所以每次都能打到，
恰恰是因为狼先顶到了 0.65 —— 推人这个缺陷在掩盖另一个数值不自洽。

所以如果让 Model 按 `AttackRange` 停在 3.2，推人没了，但咬击也再也打不中。
停止距离必须 ≤ 2.92，本轮取 `攻击距离 − 0.8 = 2.4`（离接触 0.65 很远，
又稳稳落在命中盒触达之内）。

### 分工：几何在 View，决定仍在 Model

- **停多远是几何**：它取决于命中盒的偏移与半径，那是表现层的数据。
  因此由 View 算出来，通过 `MonsterSenses.IsWithinEngageRange` 交给 Model ——
  与 `DistanceFromHome` 走的是同一条"View 算几何、Model 收纯数据"的通道。
- **怎么执行仍然是 Model 的事**：`MonsterIntent.Chase` 在已经到位时产出
  `Idle` + `MoveTarget.None` + `FaceTarget`，而不是 `Move` + `Run`。
- **Intent 没有变**：行为树依然选 `Chase`。变的是动作层怎么执行这个意图 ——
  这正是本 ADR 的核心分工（动作层是"当前动作"的唯一真相）。

顺带修掉了第二个会被看见的毛病：如果只在 View 里停住、不动 Model，
动作层仍然是 `Move`、动画仍然是 `Run`，于是狼**在原地跑步**。

### 没有改动的东西

攻击距离 3.2、感知 14、脱离 22、休眠 40、普攻冷却 2.0、伤害倍率、
决策频率 6Hz 全部未动 —— 它们是已验收配置。新增的
`chaseStopMargin`（默认 0.8）是表现层字段，停止距离由它和配置的攻击距离算出，
配置改了停止距离会跟着改。

### 覆盖

- EditMode `AWolfAlreadyInEngageRangeWaitsInsteadOfRunningInPlace`：
  冷却期间意图仍是 `Chase`，但动作层是 `Idle`、动画是 `Idle`、移动目标是 `None`、
  仍然面向玩家。
- EditMode `AWolfStillOutOfEngageRangeKeepsChasing`：反面 —— 没到位就照常追，
  否则狼会在远处站着不动。
- PlayMode `TheWolfStopsShortInsteadOfPushingThePlayer`：跨过一整个
  "攻击 → 冷却期追击 → 再攻击"循环，断言玩家位移 < 0.5、狼最近距离 > 1.2，
  并且确认这段时间里行为树**确实**选过 `Chase`（否则测试没覆盖到目标场景）。

另外登记一笔：攻击距离 3.2 > 命中盒触达 2.92 这件事本身属于灰盒数值不自洽，
见 [Q-024](../../NARAKA_待确认问题.md)。当前停止距离保证了咬击必定落在触达之内，
因此不影响可玩性；正式平衡时两者应当一起定。
