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
