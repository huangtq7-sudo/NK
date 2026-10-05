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

## 2026-09-28 修订：动作分主体与后摇两档播放速度

[Q-020](../../NARAKA_待确认问题.md) 当时留了一个选择题：动作按实测片段长度播完整之后，
连招明显偏慢（第一段要 1.07 秒才进连段窗），是给每个动作加播放速度，还是接受这套动画的固有节奏。
2026-09-28 用户实机操作后给出结论："把攻击动画加播放速度，让连招流畅起来……
三段攻击、F 技能和 V 技能，进入战斗场景的动画的后摇比较久，帮我加快后摇速度。"

因此引入**片段时间与真实时间分离**的模型，`AttackActionTuning` 与 `DashTuning`
合并为统一的 `TimedActionTuning`，内含 `ActionPlayback`：

```text
ClipSeconds           动画片段长度，仍然由 Rebuild Player Animator 从 AnimationClip 同步
MainSpeed             主体段播放倍率
RecoverySpeed         后摇段播放倍率
RecoveryStartSeconds  后摇起点（片段时间）
RealDurationSeconds   状态机实际占用的秒数 = 主体/MainSpeed + 后摇/RecoverySpeed
```

关键约束：**所有窗口都用片段时间表达**（命中窗、连段窗、位移窗）。
如果窗口写成真实时间，调一次速度就会把窗口甩到动作之外——那正是"改个手感数值
结果连招打不出来"这类缺陷的来源。`ClipTimeAt(realElapsed)` 负责两者之间的换算，
`SpeedAt(clipTime)` 把当前倍率投影给 Animator（`Animator.speed`，不引入 Animator 参数）。

同一轮还修正了三件由实机暴露的问题：

1. **攻击不再改变坐标**。三段普通攻击、蓄力与 F 技能的 `ForwardDisplacement` 全部设为 0，
   攻击前后坐标不变。位移只保留给 `Move_F`（12.276）与 V 技能（17.838）。
   注意：蓄力 `Attack10` 与 F 技能 `AM_Skill01` 的动画本身烘焙了 +8.47 / −2.59 的根位移，
   把它们置 0 是"攻击应保持坐标不变"这条要求的忠实读法，不是用户逐条点名的结果。
2. **冲刺与 V 技能改成瞬间爆发**。位移集中在片段时间的前 `DisplacementClipSeconds`
   内完成（冲刺 0.45 秒、V 技能 0.9 秒），而不是铺满整段动作缓慢滑行。
   总位移与速度无关：`ForwardSpeedAt` 在换算到真实时间时乘上当前倍率。
3. **出场动画加速**。`Burst02` 主体 1.0 倍、后摇 2.5 倍；`Burst01` 主体 1.5 倍、后摇 3.0 倍。

**哪些是数据、哪些是手感值**：`ClipSeconds` 与停止距离全部来自实测动画片段；
`MainSpeed`、`RecoverySpeed`、`RecoveryStartSeconds` 与两个 `DisplacementClipSeconds`
是没有数据来源的手感起始值。2026-09-29 用户已人工验收这一版。

## 2026-09-29 修订：Animator State 数量、反击与处决的表现缺位

- ADR 正文里的"20 个 State"是 [ADR-0017](0017-camera-relative-locomotion.md) 之前的数字。
  取消后退与原地转身之后 `Walk_B` / `Run_B` 不再被引用，Animator 现在是
  **18 个 State、0 个 Parameter、0 条 Transition**。
- `ActionState` 新增 `Counter` 与 `Execute`（见
  [ADR-0019](0019-authoritative-damage-counter-execute.md)），`PlayerAnimation` 相应新增两项。
  长离这套动画里没有经过确认的反击/处决动作，因此这两项**不映射任何 AnimationClip**：
  `PlayerAnimatorProjector` 遇到没有 State 的动画时保持上一个姿态，不报错也不回退到 Idle。
  这是有意的表现缺位，登记为 [Q-023](../../NARAKA_待确认问题.md)。
- 正文里 `AttackActionTuning.ForwardDisplacement` 与 `DashTuning.Distance` 两个类型名
  已被上一节的 `TimedActionTuning` 取代；`Damage` 字段改名为 `SkillMultiplier`，
  伤害改由权威公式推导，见 [ADR-0019](0019-authoritative-damage-counter-execute.md)。

## 2026-10-04 修订：换成带头发飘动的新动画集

用户重做了全部动作（旧动画没有头发自然飘动），新文件在
`E:\素材\新版本AS\艾斯3d建模-鸣潮 长离-标准版\新动作`。本轮按"有新版就换、没有就留旧版"替换。

### 替换方式：覆盖内容，保留旧文件名

新文件名没有 `AM` 前缀，旧文件名有一部分带。替换时**保留工程里的旧文件名**，
只换文件内容：

| 新文件 | 工程内文件 |
| --- | --- |
| `Stand1_Action03` | `AM_Stand1_Action03`（Idle） |
| `Stand1_Action03_SEQ1` | `AM_Stand1_Action03_SEQ1`（待机动作） |
| `Summon` | `AM_Summon`（第二段普攻） |
| `Death` | `AM_Death` |
| `AirAttack01`–`05` | `AM_AirAttack01`–`05` |
| 其余 19 个同名文件 | 同名 |

这样做的理由只有一个但足够：Animator Controller、`.meta`（里面存着
`AM_Stand1_Action03` / `Walk_F` / `Run_F` 的 `loopTime`）与 `PlayerTuning.asset`
全部按 GUID 与路径引用资产。换名字就要重建这些引用，换内容不换名字则是零引用风险、
零 GUID 变动，而且 `git` 上是一条干净的二进制内容变更，可以直接还原。

**6 个没有新版的动画保持旧版不动**：`AM_Skill01`（F 技能）、`AM_QTE`、
`AM_Stand1_Action01_SEQ1`、`AM_Stand1_Action02_SEQ1`、`Manipulate_Release_F`、
`Manipulate_Release_F_02`。其中 `AM_Skill01` 是 18 个状态里唯一还在用旧动画的，
因此 **F 技能没有头发飘动**，见 [Q-026](../../NARAKA_待确认问题.md)。

### 骨骼：新动画完全驱动现有正式模型，模型不用换

新动作文件夹里还有一个 `·长离_标准T姿势.fbx`。为了确认骨骼能不能对上，
把它临时导入做了一次逐路径比对，结论是**不需要换模型**：

- 两边的骨骼层级完全相同（`Root/Bip001/...`，含 57 个 `Bone_Hair*` / `Bangs` 节点）；
- 差别只在网格节点：正式模型是 1 个合并的 `R2T1ChangLiMd10011_LOD0`，
  标准版是 8 个拆开的 `mesh_0`–`mesh_7`，外加一个淘宝水印节点；
- 新动画的**骨骼曲线路径 100% 匹配正式模型**，材质仍然是
  `Changli/Materials` 下那 8 个 `MI_*`（`.meta` 里的 `externalObjects` 重映射没动）。

因此保留正式模型：它是已验收基线，只有 1 个 SkinnedMeshRenderer（标准版是 8 个，
多 7 次绘制调用），而且不带水印节点。临时导入的标准版 T 姿势已删除。

### 导出残留节点：570 条曲线被忽略，这是正常的

新动画里有一批曲线指向模型上不存在的节点，必须和"骨骼对不上"区分开：

- 26 个片段各有 8 条指向 `mesh_0`–`mesh_7`（或 `0000_mesh_0` 这种带序号的变体）；
- `Stand1_Action03` 与 `Stand1_Action03_SEQ1` 里另有一整份带 `W0_` 前缀的
  **重复骨架**（`W0_Root/W0_Bip001/...`），两个片段的曲线路径数因此是 491 条而不是 274 条。

Unity 会直接忽略路径不存在的曲线，所以它们不影响骨骼运动。但
`ValidateClipPaths` 原来只报"有多少条对不上"，换完动画之后会变成 570 条噪音，
真有骨骼对不上的时候反而没人注意。因此改成按路径首段判断：
骨骼全在 `Root/` 之下，首段不是 `Root` 的一律记为导出残留并只报数量。

重复骨架让这两个片段的曲线数翻倍（4727 vs ~2600），而 Idle 是播放最频繁的动画。
这不是缺陷，但值得重新导出一次，已登记为 [Q-026](../../NARAKA_待确认问题.md)。

### 根位移通道换了轴，`RootMotionCanceller` 必须跟着改

这是本轮唯一的真缺陷，而且只能靠实测发现：

旧导出把整体位移烘焙在 `Root.localPosition.z`，那是世界水平方向
（冲刺 +12.276、V 技能 +17.838、停止走路 +1.439）。
新导出把它放在 `Root.localPosition.y`，而 `Root` 的父节点是单位旋转，
**那是世界垂直方向**。把片段采样到模型上读世界位移，结果非常明确：

| 片段 | 根节点世界位移 | 骨盆水平位移 |
| --- | --- | --- |
| `Move_F`（冲刺） | (0, **−4.833**, 0) | 0.230 |
| `Attack04_1`（V 技能） | (0, **−7.022**, 0) | 0.016 |
| `Attack10`（蓄力） | (0, **−3.335**, 0.024) | 0.122 |
| `Run_Turnback` | (0, **+4.561**, 0) | 0.046 |
| `Stop_Walk_R` | (0, **−0.566**, 0) | 0.030 |

两件事同时成立：**新动画本来就是原地动作**（水平位移 0.02～0.23，可以忽略），
而那条垃圾通道全挂在垂直轴上。旧的 `RootMotionCanceller` 刻意"抵消水平、保留垂直"，
于是新动画下它一条都抵消不掉 —— 角色每做一个动作就会沉下去或飞起来几个单位。

数值上还有一条线索：垂直值恰好等于旧的水平值除以 **2.54**（12.276/4.833、
17.838/7.022、8.470/3.335 全部等于 2.540），也就是英寸与厘米的换算比。
这说明新导出的单位/轴向设置与旧的不一致，不是作者故意的设计。

**决策：`RootMotionCanceller` 改为锁住根节点的整条位移通道（三个轴）。**

垂直姿态不会因此丢失，因为它不在 `Root` 上：实测 `Attack10` 的骨盆垂直位移与
根节点完全相等（骨盆自己没加任何垂直姿态），而旧的 `AM_Skill01` 骨盆相对根节点
上升 1.043 —— 那一部分在 `Bip001` 及以下，本来就不在抵消范围内。
`preserveVertical` 保留了旧行为以备不时之需，默认关闭。

这一条取代本 ADR 正文里"垂直 Y 保留"的写法。

### 动作位移与停止距离从此是设计值，工具不再推导

冲刺 12.276、V 技能 17.838、停止距离 1.439 / 1.417 原本是从旧片段烘焙的位移量出来的。
新片段的水平位移≈0，这些量**已经没有可测量的来源**。
让工具继续"同步"只会把它们写成 0，顺带毁掉停止动作的指数衰减
（`DecayingStopSpeed` 在距离为 0 时退回线性衰减，脚又会打滑）。

因此 `SyncTuningFromClips` 不再推导位移与停止距离，这四个数值保留用户已验收的取值，
并在代码注释里写明它们现在是纯手感设计值。

> **这一节的结论在 2026-10-04 第三次修订里被推翻。**
> "已经没有可测量的来源"是错的：来源一直在，只是被根通道的轴向错位藏住了。
> 把位移通道一起还原之后，实测值与配置值分毫不差
> （冲刺 12.276、V 技能 17.837、停止 1.439 / 1.417）。
> 工具仍然不推导这四个数值，但现在由契约测试断言"配置 == 实测"。

### 片段长度变了的动作，窗口按比例缩放

28 个新片段里只有两条长度变了，其余逐帧一致：

| 字段 | 旧 | 新 |
| --- | --- | --- |
| `idleVariationDurationSeconds`（待机动作） | 15.067 | **8.000** |
| `combo2.clipSeconds`（第二段普攻 `Summon`） | 1.833 | **2.667** |

"第 0.55 秒开始命中"这种绝对秒数在换一条更长的动画之后就指向了另一个动作阶段，
甚至可能落到片段之外（`AttackWindowsStayInsideTheActionDuration` 正是为此存在）。
因此 `SyncTuningFromClips` 在片段长度变化时把该动作的命中窗、连段窗、后摇起点与
位移窗**按同一比例缩放**：第二段按 ×1.4548 缩放，前摇/命中/后摇的相对节奏不变。

这恢复了本 ADR 正文原本的做法（"命中窗按比例缩放"）。2026-09-28 那一轮把它收窄成
"只同步长度"，当时没有任何片段会变长度，现在有了。

`PlayerTuning.CreateBaseline()` 与 `PlayerTuningAsset` 的默认值已同步到新数值，
因此 EditMode 期望值、兜底值与资产三者一致。

## 2026-10-04 第二次修订：换版引入的两个缺陷（用户在 Unity 里报告后定位）

上一节记下了换版本身。用户随后在 Unity 里实际播放，报告了两件事：

> idle 状态、run 状态、idle 五秒后的状态、v 技能状态，冲刺闪避状态，
> 第二段攻击和第三段攻击状态，受击状态都是贴在地上的，方向轴不对，
> 并且走路、idle 会出现卡顿的效果。

这两条是**两个独立缺陷**，都来自新导出的设置变化，都不是上一节已经处理过的根位移。
两者都靠测量定位，不靠推断。

### 缺陷一：`Root` 节点上多了一个 90° 旋转和一个 1/2.54 缩放

直接读 FBX 的节点属性，新旧差异只有两行：

| `Root` 节点 | 旧动画 / 正式模型 | 新动画 |
| --- | --- | --- |
| `Lcl Rotation` | 缺省（0） | **(+90, 0, 0)** |
| `Lcl Scaling` | 缺省（1） | **0.3937 = 1/2.54** |
| `PreRotation` | (−90, 0, 0) | (−90, 0, 0)（相同） |
| `Bip001` 以下的身体动画 | — | 与旧版几乎逐位相同 |

最后一行很重要：`Move_F` 的 `Bip001` 在旧版是
`Lcl Translation=(1.6000, 0, 66.0718)`、`Lcl Rotation=(6.0889, 11.8674, −81.5530)`，
新版是 `(1.5995, 0, 66.0737)`、`(6.0900, 11.8713, −81.5534)`。
**身体动画没变，变的只有 `Root` 这一个节点。**

`PreRotation(−90,0,0)` 是 3ds Max（Z 轴朝上）导出 FBX 时的标准补偿，每个文件都有，
Unity 会把它折进 `Root` 的静止 `localRotation` —— 实测正式模型的
`Root.localEuler = (270, 0, 0)`、缩放 1。新片段把 `Root` 驱动到
`Rx(−90)·Rx(+90) = 0`，于是整具骨架相对静止姿态**绕世界 X 轴转了 +90°**：
角色躺在地上、头朝前；同时整体缩到 39.37%。

把片段采样到正式模型上逐个量，结论干净：

| | Root 旋转偏差 | Root 缩放 | 对应用户的描述 |
| --- | --- | --- | --- |
| 15 个新片段 | **+90°**（全程恒定） | 0.3937 | 贴在地上 |
| `Walk_F` | +6.87° | 0.3937 | 只是前倾，用户没说它躺平 |
| `Attack01` | −3.14° | 0.3937 | 同上 |
| 旧片段 `AM_Skill01` | 0° | 1.0 | 正常（对照组） |

用户点名的 8 个状态全部落在 +90° 那一组，而唯一还在用旧动画的 F 技能
（`AM_Skill01`）正是他没有点名的 —— 症状与测量完全对应。

#### 为什么在导入期修，而不是在 `RootMotionCanceller` 里修

运行期分不清当前播的是新片段还是旧片段。同一个补偿量加给旧片段就会把旧片段
转错 90°，而交叉淡入期间两者混在一起，补偿量根本没有定义。
导入期是**逐片段**的，因此不存在这个问题。

#### 补偿量不是写死的 90°

`PlayerAnimationRootFixup`（`AssetPostprocessor.OnPostprocessAnimation`）按

```
补偿 = 正式模型的 Root 静止姿态 × 该文件 Root 静止姿态的逆
```

算出每个片段自己的补偿量：它把这个文件里的站立姿态映射到正式模型的站立姿态，
两边约定一致时**天然是单位四元数**，所以 6 个旧片段一个字节都不会被改
（实测日志里它们不出现）。`Walk_F` 算出 6.87°、`Attack01` 算出 3.14°，
与实测偏差一致，这也反过来验证了公式。

左乘常量四元数在 R⁴ 上是**线性映射**，所以关键帧的值与切线可以用同一个乘法变换，
曲线形状逐帧保留，不需要重新平滑、也不会引入抖动。缩放同理（乘标量）。

`Run_Turnback` 的 180° 净转身被完整保留：修正之后它的 Root 偏差是
`(0, 0, −180)`，X 轴归零而转身还在。这正是"按静止姿态重定基"而不是"锁死旋转"的原因。

`AssetPostprocessor.GetVersion()` 必须跟着改动递增，否则 Unity 不会重新导入，
工程里留着的还是旧结果。

位移通道刻意不动：新导出的 `Root` 位移仍然是一条误导出的垃圾通道（见上一节），
运行期由 `RootMotionCanceller` 整条锁回静止值。在两处各改一半只会互相掩盖。

### 缺陷二：FBX 时间模式从 30fps 改成了 60fps，把按帧号写死的裁剪范围腰斩

| | 旧动画 | 新动画 |
| --- | --- | --- |
| FBX 版本 | 7500 | 7700 |
| `TimeMode` | **6 = 30 fps** | **3 = 60 fps** |

工程里只有三个片段带显式裁剪范围，正好就是三个循环动画。范围是按**帧号**存的，
于是同一个帧号在新帧率下只截到一半：

| 片段 | `.meta` 范围 | 旧（30fps） | 新（60fps） | 首末帧最大骨骼差 |
| --- | --- | --- | --- | --- |
| `Walk_F` | 0–38 | 1.267s＝整段 | 0.633s＝前一半 | 0.00° → **42.80°** |
| `Run_F` | 0–22 | 0.733s＝整段 | 0.367s＝前一半 | — → **69.13°** |
| `AM_Stand1_Action03` | 0–80 | 2.667s＝整段 | 1.333s＝前一半 | 0.17° → **52.26°** |

走路因此只播半个步幅就硬接回起点，每 0.633 秒跳一次（片段内正常的单帧步进只有 2.9°，
相差 15 倍）。这就是"走路、idle 卡顿"。

**修法**：`ApplyImportSettings` 不再沿用 `.meta` 里存着的帧号，每次都从
`importer.defaultClipAnimations` 重新取完整 Take 范围，只覆盖 `firstFrame`/`lastFrame`/
`loopTime`，其余导入标记保留（直接套用默认条目会把 `loopBlendPositionY` 这类
本轮不打算改的标记一起冲掉）。这样换帧率不会再截断循环。

修完三个片段的时长回到 **1.267 / 0.733 / 2.667 秒**，与用户 2026-09-29 验收的基线
完全一致。上一节报告里写的"`Walk_F` 0.633 秒、`Run_F` 0.367 秒"是这个缺陷的症状，
不是新动画的设计长度，据此提出的"脚打滑"担心同样不成立。

### 遗留：新 idle 本身不是一个循环动作

裁剪范围修好之后，走路与奔跑的首末帧差降到 **0.29°／0.00°**（无缝），
但 idle 还剩 **41.93°**，而且：

- 发梢在 1.0 秒处偏离首帧 **55.75°**，到末尾只回落到 41.93°，从不回到起点；
- 身体（裙摆）也差 **12.34°**；
- 逐帧扫过全部 141 个候选结束点，最接近的也有 **30.87°**；
- Unity 的 Loop Pose（`loopPose`）对 Generic + `NoAvatar` 的片段实测**完全无效**
  （开与不开都是 41.93°）；
- 对照：旧 idle 在同样 2.667 秒下只有 **0.17°**。

也就是说新 `Stand1_Action03` 烘焙的头发模拟是"从静止开始、逐渐摆动"的一次性动作，
不是循环。引擎里没有无损修法，必须重新导出 —— 已登记为 Q-026。
`LoopingAnimationsActuallyLoopSeamlessly` 把这一条登记在 `KnownBrokenLoops` 表里，
只保证它不再变坏；重新导出闭合之后测试会要求把它删掉，门槛自动收回 5°。
