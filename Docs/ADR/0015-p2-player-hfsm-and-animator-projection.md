# ADR-0015：玩家分层状态机使用自研轻量 HFSM，Animator 只作为单向投影

- 状态：已接受
- 日期：2026-09-26

## 背景

`NARAKA_技术架构.md` 原文写的是"UnityHFSM 或自研 HFSM 适配层"。到了真要实现玩家状态机时，
这个"或"必须落地成一个选择。

同时有一条比选型更重要的约束：`NARAKA_开发规范.md` §1 要求"同一个业务状态只能有一个权威来源，
Animator 不能成为第二份真相"。这条约束很容易在实现时被悄悄违反——一旦代码里出现
`animator.GetCurrentAnimatorStateInfo(0).normalizedTime >= 0.8f` 这样的判断，
业务状态就事实上搬进了 Animator，而 Animator 的过渡时间、打断规则和插值都不是可测试的业务规则。

## 决策

### 不引入 UnityHFSM

P2 使用项目自有的轻量状态机层 `Game.Features.Character.Model.Hfsm`：

- `StateMachine<TState>`，`TState` 是枚举，提供 `Enter` / `Tick` / `Exit` 生命周期、
  `TimeInState` 与带原因的 `TryChangeTo(next, StateChangeReason)`。
- 分层由三台这样的机器加一个集中仲裁组成，不是一棵嵌套状态树。

理由：

- 本阶段需要的能力就是"三层独立 + 集中优先级 + 可测试的时间轴"，UnityHFSM 的嵌套、
  转换 DSL 与 Trigger 机制在这个规模上是净负担；
- 它不引入新依赖，因此不需要为一个第三方包再做一次 ADR-0003 式的离线还原安排；
- 最关键的一条：状态机整体位于 `noEngineReferences: true` 的 Model 程序集里，
  可以在 EditMode 中逐帧驱动并断言，不需要场景、不需要 Animator、不需要 PlayMode。

后续怪物动作状态机如果确实需要嵌套与并行，可以另立 ADR 引入 UnityHFSM，
但玩家状态机的可测试性不能为此让位。

### 状态结构与优先级

```
Locomotion  Idle / IdleVariation / WalkForward / WalkBackward / RunForward /
            RunBackward / RunTurnback / StopWalk / StopRun
Action      None / SpawnLobbyToMap01 / SpawnMap01ToMap02 / Dash /
            AttackCombo1 / AttackCombo2 / AttackCombo3 / Charge / SkillF / SkillV
Reaction    None / HitStun / Death
Overlay     InputLocked / SuperArmor / SpawnProtection / Loading / Grounded
```

优先级固定为 **Death → HitStun → 强制场景/出场状态 → Action → Locomotion**，
全部在 `PlayerCore.Tick` 里仲裁，外部不直接写状态：

- Death 一旦进入不可被普通状态打断，且只能进入一次；
- HitStun 不能打断 Death；
- SuperArmor 只阻止普通受击硬直，不阻止伤害与死亡；
- 出场状态（Burst02 / Burst01）期间输入锁定，Action 输入全部被忽略。

Overlay 是与主状态并行的标签，不创建互相冲突的平行主状态。

### Animator 只接收单向投影

- Animator Controller 由 `P2AnimationSetup` 生成：**20 个 State、0 个 Parameter、0 条 Transition**。
  每个动画各占一个独立 State，状态之间没有任何 Animator 侧的转换条件。
- 状态机输出 `PlayerAnimation` 枚举，`PlayerAnimatorProjector` 把它映射到预先缓存好的
  State Hash 并调用 `CrossFadeInFixedTime`。运行期没有任何按名字的字符串查找，
  也没有每帧的哈希计算或字符串分配。
- 业务层**从不读取** Animator 的 State 名、Trigger 或 `normalizedTime`。
  因此 Animator 不可能自己跳状态，也不可能被反查成状态来源。
- `applyRootMotion = false`。动作位移来自配置（`AttackActionTuning.ForwardDisplacement`
  与 `DashTuning.Distance`），Root Motion 不是位移真相。
- 动画事件可以用来对齐表现时间点，但命中窗的真相是 `AttackActionTuning` 的
  `HitWindowStart` / `HitWindowEnd`，由 Controller/命中层校验。

### 动作时长的真相是动画片段，不是配置里的估计值

初版把每个动作时长都写成了估计值。实测之后发现几乎每一条都错，而且错得很多：

| 动作 | 初版估计 | 实测片段长度 |
| --- | --- | --- |
| 待机动作 `AM_Stand1_Action03_SEQ1` | 3.0 秒 | **15.067 秒** |
| 出场 `Burst02`（进地图一） | 2.0 秒 | **5.500 秒** |
| 出场 `Burst01`（进地图二） | 2.0 秒 | **7.233 秒** |
| `Attack01` | 0.85 秒 | **3.033 秒** |
| `Attack02` | 1.3 秒 | **4.167 秒** |
| `AM_Skill01`（F） | 1.5 秒 | **4.600 秒** |
| `Attack04_1`（V） | 2.2 秒 | **5.567 秒** |
| `Move_F` 冲刺 | 0.5 秒 | **1.333 秒** |
| `Stop_Walk_R` | 0.45 秒 | **1.500 秒** |
| `Behit_B_L` | 0.6 秒 | **2.033 秒** |

状态机按配置时长结束动作，所以**每个比配置长的动画都被拦腰截断**：
待机动作只播了 20%，`Burst02` 只播了 36%，`Burst01` 只播了 28%。

决策：动作时长只有一个真相 —— 动画片段本身。

- `NARAKA/Setup/Rebuild Player Animator` 从每个 `AnimationClip` 读取长度写进
  `PlayerTuning.asset`，命中窗与连段窗按新旧时长的比例缩放以保持原来设计的相对节奏。
- 动作位移同样取自片段在根骨骼上烘焙的水平位移，这样代码驱动的位移与动画作者的
  意图一致，脚不会打滑。
- EditMode 测试 `PlayerAnimationContractTests` 守住这条契约：配置时长必须等于片段长度，
  命中窗必须落在时长之内。换动画之后测试立刻失败，而不是等有人在游戏里看见动作被砍掉。
- `PlayerTuning.CreateBaseline()` 里的数值也改成实测值，它是测试期望值与没有绑定
  `PlayerTuningAsset` 时的兜底值。

### 动画烘焙的根位移必须在运行期抵消

这些动画在根骨骼（`Root`）上烘焙了可观的水平位移：`Move_F` +12.28、
`Attack04_1` +17.84、`Attack10` +8.47、`Attack01` +3.17、`Stop_Walk_R` +1.44、
`AM_Skill01` **−2.59**、`Run_Turnback` −11.59（并带 180° 净旋转）。

不处理的后果有两层：动作播放时整个骨架相对 GameObject 前移，与代码驱动的位移叠加成
双倍移动；动作结束交叉淡入回 `Idle` 时，骨架被插值从"前移后的位置"拉回原点 ——
**看上去就是角色向后退一小步**。移动、攻击、技能之后都会出现，因为它们都要回到 Idle。

本该用 Unity 的 Root Motion 提取解决（在导入器上设 `motionNodeName`），但实测行不通：
正式角色模型与全部动画都是 Generic 且 `avatarSetup = NoAvatar`，**没有 Avatar 就没有
Root Motion 节点**，设了那个字段也是空转（已实测：设置后曲线的根位移一点没变）。
要走那条路必须给角色生成 Avatar 并重新导入全部动画，那是对用户美术资源的大改动。

因此改由 `RootMotionCanceller` 在 `LateUpdate`（Animator 写完姿态之后）把根骨骼的
**水平**局部位置锁回静止值。抵消范围刻意只限水平：

- **水平 XZ**：抵消。位移只由代码驱动。
- **垂直 Y**：保留。蹲伏、起跳这类垂直姿态属于动画表现。
- **旋转**：默认保留，因为 `Attack02` 这类"转身再转身"靠它才有观感。
  `Run_Turnback` 的 180° 净旋转会与代码转向叠加，需要时可以单独打开 `cancelRotation`。

代价：动作内部的"前冲"不再由骨架表现，而是由代码匀速推动整个角色。总位移一致，
只有段内的加减速曲线不同。这符合 ADR-0015 的原则：Root Motion 不是位移真相。

### 动画资源约束

- 正式角色模型 `Changli_TPose.fbx` 保持 **Generic Rig**，不改为 Humanoid。
- 动画 FBX 即使包含模型与材质，也只取其中的 `AnimationClip`：不实例化重复模型，
  不让动画 FBX 的材质覆盖 `Changli/Materials` 下的正式材质。
- 对动画 `.meta` 的唯一改动是给 3 个确实需要循环的片段打开 `loopTime`：
  `AM_Stand1_Action03`、`Walk_F`、`Run_F`。
  其余动画的 `.meta` 一个字都不改（`clipAnimations` 保持为空）。
  尝试过设置 `motionNodeName` 来提取 Root Motion，实测无效后已全部回退。
- `P2AnimationSetup.ValidateClipPaths` 会把每个动画的曲线路径与正式角色模型的
  Transform 路径逐条比对，不匹配直接报出来，而不是等运行时表现为"角色一动不动"。
  当前 20 个动画全部匹配。

### 配置边界

所有手感数值通过 `PlayerTuningAsset`（ScriptableObject）转成
`PlayerTuning` 纯数据快照传入 Model。Model 不引用 ScriptableObject。

这些是客户端手感与灰盒验证数值，不是服务端权威数值，因此本阶段不进入 ADR-0010 的
CSV 配置管线。快照边界已经就位：将来要让 CSV 供数只需替换快照来源，
Model 与 Controller 不用改。

## 结果

- 玩家状态机的每条规则都有 EditMode 测试：体力、Shift 边界、待机计时、连招顺序、
  输入缓存、连招重置、蓄力互斥、F/V 冷却、Death 优先级、霸体语义。
- Animator 只要"能播这 20 个 State"就够了。换动画、改过渡时长都不会改变任何业务行为。
- 不引入 UnityHFSM 的代价是：将来怪物需要嵌套状态时要么扩展自研层，要么另立 ADR。
  这笔债是明确的，而不是隐含的。
