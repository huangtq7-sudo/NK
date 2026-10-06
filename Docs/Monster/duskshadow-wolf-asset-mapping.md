# 暮影妖狼资源映射说明

对应阶段：P2.2 正式怪物表现层接入
日期：2026-10-05
素材发行方：Meshtint Studio（`Polygonal - Creatures Pack v1.0`）

这份文档回答一个很具体的问题：**工程里这只狼的每一个零件是从哪来的、哪些是第三方的、哪些是项目自己的。**
半年后要升级素材包、要换外观、或者要照着做第二只怪时，看这一份就够。

## 1. 素材包身份

| 项 | 值 |
| --- | --- |
| 路径 | `E:\素材\30 Unity Asset Polygonal - Creatures Pack v1.0\Unity Asset Polygonal - Creatures Pack v1.0.unitypackage` |
| 字节数 | 38,110,997 |
| SHA-256 | `70b6b6a423bac0080227f816c19c94b3133eb90b90b8b860528c7284a0412f18` |
| 包内条目 | 524（10 个生物目录） |
| 暮影妖狼目录 | `Assets/Polygonal Creatures Pack/Polygonal Wolf/`（50 条） |

`unitypackage` 本身**不进仓库**。导入由
[`Tools/import_polygonal_wolf.py`](../../Tools/import_polygonal_wolf.py) 执行，
白名单写在脚本里，运行时先校验包的字节数与 SHA-256，对不上直接拒绝导入 ——
换了一份包就必须重新审计，不能默默导入别的东西。

```bash
python Tools/import_polygonal_wolf.py            # 预演，只打印不写盘
python Tools/import_polygonal_wolf.py --apply    # 真正写盘
```

## 2. 实际导入了什么（12 个资产 + 4 个目录 meta）

全部落在第三方自己的原始目录 `Assets/Polygonal Creatures Pack/Polygonal Wolf/`，
**没有搬进 `Assets/Game/`**，`.meta` 原样写出因此 GUID 与包内一致。

| 相对路径 | 字节 | 原始 GUID |
| --- | --- | --- |
| `FBX/Polygonal Wolf.FBX` | 193,696 | `1121f0d75e60c4b48a4a1c121cf566f2` |
| `FBX/Base.FBX` | 16,784 | `e6ab38c8ab28e884fb2280129c46ecad` |
| `FBX/Polygonal Wolf@Idle.FBX` | 668,544 | `39cab350446ac2846a859beebd6987e2` |
| `FBX/Polygonal Wolf@Walk Forward WO Root.FBX` | 640,272 | `02dfba9c732d34a489e1deb9a9ccfa59` |
| `FBX/Polygonal Wolf@Run Forward WO Root.FBX` | 552,384 | `14afbfa1c13338f4d85524575d43ead9` |
| `FBX/Polygonal Wolf@Bite Attack.FBX` | 640,576 | `98bc450d350401244a960f14d26d2590` |
| `FBX/Polygonal Wolf@Breath Attack.FBX` | 668,128 | `3eb551d3591331f44a3a52cae5644ba2` |
| `FBX/Polygonal Wolf@Take Damage.FBX` | 552,704 | `854c38ee9e000184c925e9daa696784d` |
| `FBX/Polygonal Wolf@Die.FBX` | 773,008 | `d9380c5c27824f14cae9429b9fb0d6fa` |
| `Materials/Polygonal Wolf Black.mat` | 2,127 | `18002e3ccb50c9e4eb0294f7d29e8cdd` |
| `Textures/Polygonal Wolf Black.png` | 10,404 | `44d8a772cd8a76d46b15cb2c5ebab163` |
| `Textures/Polygonal Wolf Black Glow.png` | 8,522 | `d4638ebb2bf11634e8e0184108be8d90` |

全工程 2,184 个 `.meta` / 2,184 个唯一 GUID，**重复 GUID 为 0**。

## 3. 明确排除了什么

| 排除项 | 理由 |
| --- | --- |
| 其余 9 类怪物目录 | Dragon / Giant Bee / Golem / King Cobra / Magma / One Eyed Bat / Spiderling Venom / Treant / Treasure Chest 属于 P4 |
| `Scenes/Demo Scene.unity`、`Scenes/Turntable Scene.unity` | 演示场景 |
| `Animators/` 三个 controller | 演示用；本项目自建单向投影控制器 |
| `FBX/Rotate.anim`、`PPP/Post Processing.asset`、`Materials/Demo Ground.mat` | 演示工具与后处理 |
| Brown / White 全部材质、贴图 | 暮影妖狼默认 Black 外观，Black 资源不依赖它们 |
| `@Walk/@Run/@Jump/@Pound Attack` 的 **W Root** 版本 | 移动由 `NavMeshAgent` 驱动，带 Root 的动画会再推一次世界坐标 → 双倍位移 |
| `@Walk Backward`、`@Jump`、`@Pound Attack`、`@Howl`、`@Eating`、`@Resting`、`@Look Around` | 本轮七个状态之外 |
| `Prefabs/Polygonal Wolf Black.prefab` | 见下 |
| `Read me.txt`、`www.meshtint.com.txt` | 留在原包 |

**为什么连 Black 的 Prefab 也不导入**：实测它除 FBX 之外只依赖两样东西 ——
内建 `Standard` 材质（URP 下是粉色，必须换）与
`Animators/Polygonal Wolf.controller`（演示控制器，已排除）。
导入它只会留下一个必然缺失的 Controller 引用。
而它能提供的东西（53 节点骨架 + 单个 `SkinnedMeshRenderer` + Avatar）
直接实例化 FBX 就有，并且它自己**没有任何碰撞体**，
因此也不构成"项目自有碰撞体"的来源。

## 4. 模型实测数据

由 `NARAKA/Diag/Duskshadow Wolf Asset Audit` 量出，不是估计值。

| 项 | 值 |
| --- | --- |
| `animationType` | Generic（与玩家模型同约定） |
| `avatarSetup` | `CreateFromThisModel`（Avatar 名 `Polygonal WolfAvatar`） |
| `rootMotionBoneName` | 空 —— 没有 Root Motion 节点 |
| `materialImportMode` | None（材质靠外部指定） |
| Transform / 蒙皮骨骼 | 53 / 37 |
| `SkinnedMeshRenderer` | 1 |
| 第三方自带 Collider | **0** |
| Idle 姿态：脚底 | y ≈ **−0.072**（基本贴地，不需要补偿） |
| Idle 姿态：肩高 | ≈ 0.693 |
| Idle 姿态：体长 Z / 体宽 X | ≈ 1.539 / 0.497 |
| 嘴部骨骼 `RigJaw` | (0, 0.396, 0.697) |
| 头部骨骼 `RigHead` | (0, 0.567, 0.691) |
| 朝向 | 体长沿 **+Z**，与 Unity 正前方一致，不需要旋转补偿 |

> 绑定姿态的 `SkinnedMeshRenderer.bounds` 报的是脚底 −0.427，那是作者烘焙的绑定姿态包围盒，
> 不能用来摆碰撞体。上表取的是**把 Idle 采样到模型之后**的骨骼世界坐标。

## 5. 动画映射

业务动作由 `MonsterCore` 的动作层产出（`MonsterFrameOutput.Animation`），
`MonsterAnimatorProjector` 只负责把它投到同名 Animator State。
映射表只有一份，在
[`DuskshadowWolfAssets.AnimationToFbx`](../../NK/Assets/Game/EditorTools/DuskshadowWolfAssets.cs)，
装配工具与契约测试共用。

| 业务动作 | Animator State | 第三方 FBX | 片段名 | 长度 | 循环 | 配置动作时长 |
| --- | --- | --- | --- | --- | --- | --- |
| `Idle` | `Idle` | `@Idle` | `Idle` | 1.333 | 是 | — |
| `Walk`（巡逻/回家） | `Walk` | `@Walk Forward WO Root` | `Walk Forward WO Root` | 1.167 | 是 | — |
| `Run`（追击） | `Run` | `@Run Forward WO Root` | `Run Forward WO Root` | 0.667 | 是 | — |
| `Attack`（普攻） | `Attack` | `@Bite Attack` | `Bite Attack` | 1.167 | 否 | 1.30（0.45/0.25/0.60） |
| `Skill`（赤瘴吐息） | `Skill` | `@Breath Attack` | `Breath Attack` | 1.333 | 否 | 1.55（0.35/0.30/0.90） |
| `HitStun`（受击） | `HitStun` | `@Take Damage` | `Take Damage` | 0.667 | 否 | 0.60 |
| `Death`（死亡） | `Death` | `@Die` | `Die` | 2.000 | 否 | 2.00 |

- 7 个片段共 510–520 条曲线，**曲线路径 100% 匹配模型骨架（0 条对不上）**。
- 根位移净变化：6 个为 `(0,0,0)`；`@Die` 为 `(−0.328, 0.064, −0.046)`
  —— 死亡时躯体自然倾倒，不移动 GameObject，因此不产生双倍位移。
- `@Breath Attack` 对应的是**已经存在**的赤瘴吐息技能
  （`Config/Source/monster_skills.csv`），不因为有这条动画就新增技能或改技能规则。
- **播放速度一律保持 1**。片段长度与配置动作时长的差在 10–14%
  （咬击短 10%、吐息短 14%、受击长 11%、死亡正好一致）。
  要不要调速是观感取舍，按既定做法（Q-020 的"不擅自发明速度值"）交人工验收决定。

Animator Controller：`Assets/Game/Settings/Monster/DuskshadowWolf.controller`，
**7 个 State、0 个 Parameter、0 条 Transition**，默认状态 `Idle`。
Parameter 与 Transition 是 Animator 自己做决定的入口，因此数量必须是 0 ——
当前动作的唯一真相在 `MonsterCore`（[ADR-0018](../ADR/0018-monster-behavior-tree-and-hfsm.md)）。

## 6. 材质适配层

第三方 `Polygonal Wolf Black.mat` 用的是内建 **`Standard`** Shader，URP 下渲染为粉色。
按开发规范 §11，只**新建**项目自有的 URP 材质并引用同样的贴图，
**不修改第三方源材质**（素材包升级时才能直接对比）。

| 项 | 第三方源材质 | 项目自有适配层 |
| --- | --- | --- |
| 路径 | `.../Materials/Polygonal Wolf Black.mat` | `Assets/Game/Settings/Monster/DuskshadowWolfBody.mat` |
| Shader | `Standard` | `Universal Render Pipeline/Lit` |
| 基础贴图 | `_MainTex` = `Polygonal Wolf Black` | `_BaseMap` / `_MainTex` = 同一张 |
| 自发光 | `_EmissionMap` = `Polygonal Wolf Black Glow` | `_EmissionMap` = 同一张，`_EMISSION` 关键字开启 |
| 基础色 | — | `_BaseColor` = 白（不染色） |

颜色反馈（正常／半血狂暴／可处决／死亡）由 `DuskshadowWolfView` 用
`MaterialPropertyBlock` 写到**全部**身体 Renderer 上，不生成材质实例、不每帧写入。
正式模型的"正常"一档是**白色（不染色）**，否则方块替身那套灰蓝会把贴图压暗。

## 7. Prefab 结构与归属边界

```text
DuskshadowWolf.prefab                        项目自有   layer = Enemy
├── [DuskshadowWolfView]                     项目自有   感知→Model、帧输出→移动/命中窗/动画
├── [CapsuleCollider]                         项目自有   direction=Z, height 1.5, radius 0.33, center (0,0.42,0)
├── [NavMeshAgent]                            项目自有   radius 0.35, height 0.9, updateRotation=false
├── [MonsterAnimatorProjector]                项目自有   单向投影
├── Model                                     第三方     Polygonal Wolf.FBX 的嵌套 Prefab 实例
│   └── [Animator]                            项目自有的 Controller，applyRootMotion = false
│       └── 53 节点骨架 + 1 个 SkinnedMeshRenderer（材质指向项目自有适配层）
├── Hitbox  [MeleeHitbox]                     项目自有   localOffset (0, 0.5, 1.2), radius 1.4
└── Warning [MonsterWarningView]              项目自有   Quad，不依赖第三方资源
```

- 第三方模型自带 0 个碰撞体，因此命中、阻挡与导航**只能**用项目自有对象 ——
  "不依赖第三方模型自带碰撞体"这条要求天然成立，不需要额外剥离。
- 命中盒的**前向偏移 1.2 与半径 1.4 与方块替身完全一致**：这两项决定咬击能不能打到玩家，
  动它们等于动已验收的战斗手感。只把高度从 0.8 降到 0.5，
  让判定球对齐实测的嘴部高度（`RigJaw` y≈0.40、`RigHead` y≈0.57）。
- 碰撞体与 `NavMeshAgent` 的尺寸按实测体型重新给过，**没有照抄**方块替身那套
  1.6 高的立方体尺寸（对真狼来说又高又窄）。

## 8. 方块替身的去向

`Assets/Game/Settings/Monster/GrayboxWolf.prefab` 仍然由装配工具生成，
但只作为**开发回退资产**：正式资源万一缺失时还能跑通战斗闭环。
**正式场景不引用它** —— `Map02_CombatGraybox` 的 `GrayboxWolfSpawner`
指向 `DuskshadowWolf.prefab`，并由契约测试
`TheSceneSpawnerPointsAtTheOfficialWolfNotTheGraybox` 守住。

脚本 `GrayboxWolfView` 已随正式模型接入改名为 `DuskshadowWolfView`
（`.cs.meta` 的 GUID `2df49a30f96d7e7408808ba379d27c05` 原样保留，
因此两个 Prefab 的脚本引用都没有断）。
继续叫 Graybox 会让人以为美术还没进来，而 [ADR-0018](../ADR/0018-monster-behavior-tree-and-hfsm.md)
当初写明"名字里带 Graybox 是刻意的"。

## 9. 授权

包内只有 `Read me.txt` 与 `www.meshtint.com.txt`，**没有 LICENSE 文件**。
用户已于 2026-10-05 明确确认该素材包可用于本项目并授权导入暮影妖狼所需资源
（见 [Q-007](../../NARAKA_待确认问题.md)）。正式发行前仍需向 Meshtint Studio
确认分发条款，这一项不因本轮导入而关闭。
