# ADR-0019：权威伤害公式落地，反击与处决进入领域层

- 状态：已接受
- 日期：2026-09-29

## 背景

P2 第一阶段的灰盒伤害是写死的绝对数值：第一段 100、第二段 120、第三段 160、
蓄力 300、F 200、V 400。它们能跑通"打到假人会掉血"，但和
`NARAKA_完整玩法设计.md` §8 的权威公式没有任何关系：

```text
RawDamage    = FinalAttack × SkillMultiplier
AfterDefense = RawDamage × 100 / (100 + Defense)
```

写死绝对数值有两个必然后果。第一，换武器、换英雄之后每一段攻击都要重填一遍，
而"长剑 1 级 120、20 级 272"意味着同一段攻击的伤害要有 20 个版本。
第二，防御根本无处安放：防御是**受击方**的属性，把它算在攻击方身上，
同一刀打到防御 20 的狼和防御 80 的玩家会得到相同伤害。

同时，反击与处决在 P2 第一阶段完全不存在。它们不是表现功能：
0.2 秒判定窗、0.5 秒失败后摇、1.5 秒处决窗口、2 秒霸体、`FinalAttack × 1.5`
每一条都是可测试的领域规则，必须先进 Model 再谈动画。

## 决策

### 公式只有一份实现，倍率取代绝对伤害

`Game.Features.Combat.Model.DamageFormula` 是唯一实现，玩家与怪物共用：

```csharp
Raw(finalAttack, skillMultiplier)          // FinalAttack × SkillMultiplier
AfterDefense(rawDamage, defense)           // × 100 / (100 + Defense)
ArmorAbsorption.Absorb(amount, armor, health)  // 先扣护甲，溢出扣生命
```

`TimedActionTuning.Damage` 改名为 `SkillMultiplier`。灰盒倍率：
第一段 1.0、第二段 1.2、第三段 1.6、蓄力 3.0、F 1.2、V 2.5。
F 与 V 的倍率刻意与 `Config/Source/hero_skills.csv` 里顾沉岳的
「震岳斩 1.2」「破军镇狱 2.5」对齐，而不是另编一套。

### 防御在受击方那一侧扣

`HitRequest.Damage` 改名为 `RawDamage`，语义明确为"扣防御之前"。
`PlayerVitals.ApplyRawDamage` 与 `MonsterVitals.ApplyRawDamage` 各自用**自己的**
防御把它换算成最终伤害，再走护甲吸收。命中层 `HitResolver` 完全不碰数值：
它只负责 HitId 去重、阵营过滤和转发。

`VitalsTuning` 因此新增 `Defense`，玩家灰盒值取 80（与顾沉岳一致）。
防御为 0 是公式的边界情况，不需要特例分支，测试直接断言这一点。

### FinalAttack 的合成规则是灰盒假设，不是已确认设计

P2 垂直切片取 `FinalAttack = 英雄攻击力 + 武器攻击力 = 100 + 120 = 220`。

**这条合成规则没有权威文档来源。** GDD 给了英雄攻击力 100 和长剑 1 级攻击 120，
但没有说两者如何合成（相加？武器为主英雄为修正？还是乘算？）。
本阶段需要一个能跑通闭环的数，因此取了最直白的相加，并登记为
[Q-022](../../NARAKA_待确认问题.md)。正式数值最终由服务端权威判定。

### 反击与处决是 Model 规则，不是动画功能

新增两个动作状态 `ActionState.Counter` 与 `ActionState.Execute`，全部规则在 `PlayerCore`：

| 规则 | 取值 | 出处 |
| --- | --- | --- |
| 反击判定窗 | 0.2 秒 | 玩法文档 §7 |
| 反击体力消耗 | 0 | 玩法文档 §7 |
| 反击冷却 | 无 | 玩法文档 §7 |
| 反击失败后摇 | 0.5 秒 | 玩法文档 §7 |
| 只接受 | `CounterableSkill`（金色） | 玩法文档 §7 |
| 成功后目标处决窗口 | 1.5 秒 | 玩法文档 §7 |
| 成功后玩家霸体 | 2 秒 | 玩法文档 §7 |
| 处决触发 | 普通攻击 | 玩法文档 §7 |
| 处决伤害 | `FinalAttack × 1.5` | 玩法文档 §7 |
| 处决动作时长 | **1.0 秒（灰盒占位）** | 无权威来源 |

判定顺序固定为：**无敌（处决）→ 重生保护 → 反击 → 扣防御 → 扣护甲 → 扣生命**。
霸体排在最后且只影响硬直：它不免伤害、不免护甲扣减，也不阻止死亡。

"普通攻击不能反击"这条规则在三个地方各钉了一次，任何一处都够：
`HitRequest` 构造函数、`IncomingAttack` 构造函数、配置编译器的颜色/可反击一致性校验。
重复不是冗余——它保证没有任何一条调用路径能绕过这条规则。

### 新增 Invulnerable 标签，并且只给处决用

[ADR-0015](0015-p2-player-hfsm-and-animator-projection.md) 当时写的是
"P2 的玩家状态里没有 Invulnerable 标签"，理由是 `Move_F` 取消了无敌帧。
处决要求"过程全程无敌"，因此现在必须有这个标签。

范围严格限定：**只有 `ActionState.Execute` 会置位 `PlayerOverlayFlags.Invulnerable`。**
`Move_F` 依然没有无敌帧（[ADR-0013](0013-p2-player-action-baseline.md) 不变），
重生保护继续用自己的 `SpawnProtection` 标签。要给别的动作加无敌，
必须回到这份 ADR 说明理由，而不是在状态机里悄悄多加一个 `||`。

### 反击与处决目前没有动画

长离这套动画里没有经过确认的反击/处决动作。按"不随意复用其他动画"的约束
（与右键纵击同一条原则），这两个状态**不映射任何 AnimationClip**：
`PlayerAnimatorProjector` 遇到没有 State 的动画时保持上一个姿态，
而不是拿一段不相干的动画顶替，也不是回退到 Idle。

这是有意的表现缺位，不是失败路径，因此不报错。登记为
[Q-023](../../NARAKA_待确认问题.md)，等美术到位后补两个 State 即可，业务规则不用改。

### 反击成功的结果是同步返回的

攻击者怎么知道自己被反击了？答案是命中结算**同步返回**：
`HitResolver.Resolve` 返回 `HitRejection.Countered`，`MeleeHitbox` 据此触发
`Countered` 事件，怪物 View 立刻开自己的处决窗口。

没有全局广播，没有"下一帧去查一张表"，因此不存在查询时机差一帧就漏掉的窗口。
玩家侧只需要知道"附近有没有可处决目标"，那是一个纯展示问题，
由 `IExecutionTargetRegistry` 回答：目标自己在进出处决窗口时登记，
登记表为空时玩家整段跳过查询，不做每帧物理查询。

## 结果

- 伤害公式、防御、护甲溢出、霸体、重生保护、死亡拒绝伤害各有 EditMode 断言，
  且同一份实现同时守住玩家与怪物两侧。
- 反击与处决的每一个数字都有测试，因此改数值必须同时改测试与玩法文档，
  三者不一致会在本机立刻失败。
- 代价：`HitRequest`、`DamageApplication`、`ApplyDamageDelegate` 与
  `DamageReceiver.Apply` 的签名都变了，命中链上的调用方需要一次性跟进。
  这次改动已经一并完成，没有留兼容重载——留一个"旧的也能用"的重载
  等于允许新代码继续走没有防御的老路径。
- `TrainingDummy` 也换成了同一条路径，因此灰盒假人现在也能配护甲与防御，
  而不是只有一条生命值。
