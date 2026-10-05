# 战斗 HUD 手工制作与挂载说明（P2.2）

适用版本：Unity 2021.3.45f2c1 + URP 12.1.15
对应脚本：`NK/Assets/Game/Features/CombatHud/View/CombatHudView.cs`
对应只读状态：`NK/Assets/Game/Features/CombatHud/Controller/CombatHudPresentationState.cs`

## 0. 你需要做什么、不需要做什么

**你来做**：Canvas 层级、锚点、图片、字体、配色、动效与所有视觉决定，以及把做好的对象拖进
`CombatHudView` 的 Inspector 字段。

**你不需要做**：不需要绑定任何 Controller 或 Model。`CombatHudController` 由
`AppRootLifetimeScope` 自动注册，`CombatHudView` 由场景里的 `WorldSceneLifetimeScope`
自动注入。你只要把 `CombatHudView` 挂在 Canvas 上就行。

**重要**：`CombatHudView` 的每一个字段都是可选的。只绑一条血条也能正常运行，
没绑的部分会被安静跳过。你可以分几次把 HUD 做完，不会因为少绑一个就报空引用。

战斗 HUD 按 `NARAKA_技术架构.md` §7 使用 **uGUI**（不是 UI Toolkit）。
文本控件使用 **TextMeshPro**（`TMP_Text`），工程里已经有 TextMeshPro 3.0.6。

## 1. 建议的 Canvas 层级

在 `Map02_CombatGraybox`（以及以后需要 HUD 的战斗场景）里新建：

```text
CombatHudCanvas                      Canvas + CanvasScaler + GraphicRaycaster + CombatHudView
├── PlayerVitals                     左下：头像、生命、护甲、体力
│   ├── PlayerHealthBar              Slider（或 Image: Filled）
│   │   └── Fill                     Image  → playerHealthFill
│   ├── PlayerHealthLabel            TextMeshPro - Text (UI)
│   ├── PlayerArmorBar
│   │   └── Fill                     Image  → playerArmorFill
│   ├── PlayerArmorLabel             TextMeshPro - Text (UI)
│   ├── PlayerStaminaBar
│   │   └── Fill                     Image  → playerStaminaFill
│   └── PlayerStaminaLabel           TextMeshPro - Text (UI)
├── SkillSlots                       底部中央：F / V
│   ├── SkillFSlot
│   │   ├── SkillFIcon               Image
│   │   ├── SkillFCooldownMask       Image（Type = Filled）→ skillFCooldownMask
│   │   └── SkillFCooldownLabel      TextMeshPro - Text (UI)
│   └── SkillVSlot
│       ├── SkillVIcon               Image
│       ├── SkillVCooldownMask       Image（Type = Filled）→ skillVCooldownMask
│       └── SkillVCooldownLabel      TextMeshPro - Text (UI)
├── RejectionLabel                   TextMeshPro - Text (UI) → rejectionLabel
├── CounterWindowRoot                任意可见提示，默认取消勾选 Active
├── PlayerHitFlashRoot               全屏红边 Image，默认取消勾选 Active
├── DeathRoot                        死亡提示，默认取消勾选 Active
└── TargetPanel                      顶部中央：怪物血条，默认取消勾选 Active
    ├── TargetNameLabel              TextMeshPro - Text (UI)
    ├── TargetHealthBar
    │   └── Fill                     Image → targetHealthFill
    ├── TargetArmorBar
    │   └── Fill                     Image → targetArmorFill
    ├── TargetEnragedRoot            半血阶段提示，默认取消勾选 Active
    ├── TargetExecutableRoot         可处决提示，默认取消勾选 Active
    └── WarningRoot                  技能预警根，默认取消勾选 Active
        └── WarningImage             Image → warningImage
```

Canvas 设置建议：

| 组件 | 设置 |
| --- | --- |
| Canvas | Render Mode = Screen Space - Overlay |
| Canvas Scaler | UI Scale Mode = Scale With Screen Size，Reference Resolution = 1920 × 1080，Match = 0.5 |
| Graphic Raycaster | 保持默认；HUD 不接收点击，但 Canvas 需要它才完整 |

## 2. Inspector 字段对照表

把 `CombatHudView` 挂到 `CombatHudCanvas` 上，然后按下表拖引用。
**Slider 与 Image 二选一即可**：两个都绑也可以，脚本会同时写。

| Inspector 字段 | 拖什么进去 | 说明 |
| --- | --- | --- |
| `playerHealthSlider` | `PlayerHealthBar` 的 Slider | 值域 0–1 |
| `playerHealthFill` | `PlayerHealthBar/Fill` 的 Image | Image Type 必须是 Filled |
| `playerHealthLabel` | `PlayerHealthLabel` | 显示 `当前/上限` |
| `playerArmorSlider` / `playerArmorFill` / `playerArmorLabel` | 同上，护甲 | |
| `playerStaminaSlider` / `playerStaminaFill` / `playerStaminaLabel` | 同上，体力 | 上限固定 20 |
| `skillFCooldownMask` | `SkillFCooldownMask` | 1 = 完全冷却，0 = 可用 |
| `skillFCooldownLabel` | `SkillFCooldownLabel` | 冷却剩余秒数，可用时为空串 |
| `skillVCooldownMask` / `skillVCooldownLabel` | 同上，V 技能 | |
| `rejectionLabel` | `RejectionLabel` | 显示"体力不足""F 技能冷却中"等 |
| `deathRoot` | `DeathRoot` | 玩家死亡时自动显示 |
| `counterWindowRoot` | `CounterWindowRoot` | 反击 0.2 秒判定窗期间显示 |
| `targetRoot` | `TargetPanel` | 没有目标时整体隐藏 |
| `targetNameLabel` | `TargetNameLabel` | 例如"暮影妖狼" |
| `targetHealthSlider` / `targetHealthFill` | 怪物生命条 | |
| `targetArmorSlider` / `targetArmorFill` | 怪物护甲条 | |
| `targetEnragedRoot` | `TargetEnragedRoot` | 怪物生命 ≤ 50% 时显示 |
| `targetExecutableRoot` | `TargetExecutableRoot` | 反击成功后的 1.5 秒处决窗口内显示 |
| `warningRoot` | `WarningRoot` | 有技能预警时显示 |
| `warningImage` | `WarningImage` | 颜色由脚本按标签写入 |
| `goldWarningColor` | 颜色 | 金色 = 可反击 |
| `redWarningColor` | 颜色 | 红色 = 不可反击 |
| `playerHitFlashRoot` | `PlayerHitFlashRoot` | 玩家受击时闪一下 |
| `hitFlashSeconds` | 数值 | 闪烁时长，默认 0.15 |

## 3. 血条 / 护甲条 / 体力条怎么配

两种做法都支持，按你习惯选：

**做法 A：Slider**

1. 右键 Canvas → UI → Slider。
2. 删掉 `Handle Slide Area`（HUD 不需要拖动）。
3. Interactable 取消勾选。
4. Min Value = 0，Max Value = 1。
5. 把 Slider 拖到对应的 `*Slider` 字段。

**做法 B：Filled Image（推荐，美术自由度更高）**

1. 底图一个 Image 作为槽，前景一个 Image 作为填充。
2. 前景 Image：Image Type = **Filled**，Fill Method = Horizontal，Fill Origin = Left。
3. 把前景 Image 拖到对应的 `*Fill` 字段。

两种做法脚本写的都是 0–1 的比例，不需要你自己换算。

## 4. 冷却遮罩怎么配

1. 技能图标 Image 正常放好。
2. 在图标**之上**再叠一个半透明黑色 Image。
3. 这个遮罩 Image：Image Type = **Filled**，Fill Method = **Radial 360**，
   Fill Origin = Top，Clockwise 勾选。
4. 拖到 `skillFCooldownMask` / `skillVCooldownMask`。

脚本写入的 `fillAmount` 是**剩余冷却比例**：1 表示刚放完技能（遮罩满），0 表示可用（遮罩空）。

## 5. 怪物血条的显示与隐藏

不需要你写任何显隐逻辑：

- 没有目标、或目标已经死亡时，脚本会把 `targetRoot` 整个 `SetActive(false)`。
- 玩家打到某只怪、或某只怪进入战斗时，它会自动成为当前目标。
- 场上有多只怪时，最近一次交战的那只是当前目标。

因此 `TargetPanel` 在编辑器里**默认取消勾选 Active** 就行，运行时由脚本接管。

## 6. 红色技能预警怎么绑

预警有两处，互相独立，都不需要你写逻辑：

**世界空间预警（怪物脚下的锥形红光）**
由 `NARAKA/Setup/Apply P2.2 Combat Setup` 自动生成在 `GrayboxWolf` 预制体的
`Warning` 子物体上（`MonsterWarningView`）。正式狼模型接入后，把这个子物体换成正式特效、
并把新的 Renderer 拖回 `MonsterWarningView.warningRenderer` 即可。

**屏幕预警（HUD 上的提示条）**
绑 `warningRoot` 与 `warningImage`。脚本按颜色标签写颜色：

| 标签 | 含义 | 颜色字段 |
| --- | --- | --- |
| `Gold` | 可反击技能，可以按 Space 反击 | `goldWarningColor` |
| `Red` | 不可反击技能，只能躲开 | `redWarningColor` |
| `None` | 没有预警，`warningRoot` 隐藏 | — |

**注意**：暮影妖狼在设计上只有普通攻击和红色吐息，两者都不可反击，
所以你在游戏里看到的预警只会是红色。金色路径要用
`CounterTrainingTarget_DevOnly`（开发用反击训练靶）验证，它不是正式怪物能力。

## 7. 哪些东西必须放在场景里、哪些自动创建

| 对象 | 谁负责 |
| --- | --- |
| `CombatHudCanvas` 与它的全部子对象 | **你手工创建**（本文档） |
| `CombatHudView` 组件 | **你手工挂到 Canvas 上** |
| `GrayboxWolf` 预制体、生成点、训练靶、导航区域 | 菜单 `NARAKA/Setup/Apply P2.2 Combat Setup` 自动生成 |
| `CombatHudController`、怪物登记表、命中结算、配置目录 | `AppRootLifetimeScope` 自动注册，无需挂载 |
| `CombatHudView` 的依赖注入 | `WorldSceneLifetimeScope` 自动完成，**不要手工拖 Controller** |

## 8. 验收步骤

1. 执行菜单 `NARAKA/Setup/Apply P2 Scene Setup`（如果还没执行过）。
2. 执行菜单 `NARAKA/Setup/Apply P2.2 Combat Setup`。
3. 按本文档在 `Map02_CombatGraybox` 里做出 HUD 并挂上 `CombatHudView`。
4. 从 `SampleScene` 进入 Play Mode，登录 → 大厅 → 开始游戏 → 走到传送门进入地图二。
5. 逐项确认：
   - 生命、护甲、体力三条数值随战斗变化；
   - 体力不足时 `RejectionLabel` 显示"体力不足"；
   - F/V 用掉之后遮罩转一圈回来；
   - 走近暮影妖狼时怪物血条出现，走远后消失；
   - 把狼打到半血以下，红色预警先亮、随后才挨打；
   - 站在 `CounterTrainingTarget_DevOnly` 前，金色预警亮起时按 Space，
     成功后训练靶变青色，`TargetExecutableRoot` 亮起 1.5 秒。

## 9. 已知缺口

- **反击与处决没有动画**。长离这套动画里没有经过确认的反击/处决动作，
  按"不随意复用其他动画"的约束，这两个状态目前保持上一个姿态。
  规则本身完整可用并有自动化测试覆盖。见 `NARAKA_待确认问题.md` 的 Q-023。
- **正式狼模型尚未导入**，场上是明确命名的 `GrayboxWolf` 方块替身，不是最终美术。
