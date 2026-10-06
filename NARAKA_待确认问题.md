# 《NARAKA》待确认问题与决策记录

版本：1.21
更新日期：2026-10-06
说明：本文件只保存尚未确认、需要外部素材/权限或会影响实施的事项。已确认玩法不得重新列为问题。

## 1. 开始P0前需要确认

### Q-001 Unity工程目录

- 状态：已关闭。
- 决策：正式Unity工程为`E:\NK项目\NK`，仓库根目录为`E:\NK项目`。

### Q-002 Unity具体版本与可执行文件

- 状态：已关闭。
- 决策：使用`2021.3.45f2c1`，可执行文件为`E:\2021.3.45f2c1\Editor\Unity.exe`。

### Q-003 既有网络代码

- 状态：已关闭；旧源码保持只读，服务端兼容接入已完成。
- 客户端来源：`E:\ClientProject\Assets\Scripts`。
- 服务端来源：`D:\培训项目\7.21net\SimpleServer`，原项目目标框架为.NET Framework 4.6.1。
- 决策：协议和运行行为冻结；新服务端使用.NET 10 LTS，通过Golden Files验证后接入`LegacyNetworkV1`适配边界。
- 执行结果：Host已监听`127.0.0.1:8011`，已完成真实Socket握手、注册、登录与心跳自动化验证；Unity Account MVC也已对本机Host/MySQL完成真实注册登录冒烟。

### Q-004 仓库结构

- 状态：已关闭。
- 决策：使用本地单Git仓库并备份到已有GitHub仓库；Unity客户端保留目录名`NK/`，服务端使用`Server/`，共享生成物和工具使用`Shared/`、`Tools/`。
- 执行结果：已配置仓库级Git提交者身份与GitHub `origin`，并完成P0可验证基线的首次提交和`main`分支推送。

## 2. 素材相关

### Q-005 地图素材

- 状态：**地图一与地图二已关闭**（2026-10-06 完成正式场景绑定）；
  其余地图仍延期。
- 已绑定的素材（按实测，不是转述）：

  | 用途 | 源场景 | 地形尺寸 | 体积 |
  | --- | --- | --- | --- |
  | 任务地图 | `Assets/Aquarius Fantasy - High Elves/Demo Scenes/High Elves Sanctuary/` | 1000×1000 | 2.27 MB，750 个 Prefab 实例 |
  | 战斗地图 | `Assets/PureNature/Scenes/Scene_Demo/` | 1000×1000 | 38.9 MB，10,909 个 Prefab 实例，外加 61 MB 地形 |

- 场景拆分能力：两个源场景几乎全由 Prefab 实例组成，因此派生场景引用的是
  同一批第三方 Prefab，**没有复制网格或贴图**。
- NavMesh 限制：使用 Unity 2021.3 自带的场景烘焙（不引入 `com.unity.ai.navigation`）。
  只把地形标为导航静止，植被不标。实测烘焙耗时 **256 秒**，
  产物 `NavMesh.asset` **32.7 MB**。
- URP 兼容：两个素材包**已经是 URP 原生**，全工程 201 个第三方材质里
  没有任何内建 `Standard` shader（High Elves 自带 URP Compatibility shader，
  PureNature 用 URP `Lit`/`TerrainLit` 加自有 URP shader），
  因此**本轮没有创建任何 URP 适配材质** —— 不需要的适配层不应该存在。
- 授权范围：仍需确认。见 Q-029。
- 后续需要：其余地图的素材、任务空间布局与场景分区。
- 记录：[ADR-0020](Docs/ADR/0020-p2-formal-world-scene-binding.md)。

### Q-029 两个正式环境素材包的分发条款

- 状态：**待确认**，与 [Q-007](#q-007-怪物素材实际路径和授权) 同类问题。
- 背景：本轮把两个第三方环境包接进了正式运行流程。它们的原始资产
  （FBX、贴图、材质、地形）已经在仓库里，而仓库会备份到 GitHub。
- 需要确认：Aquarius Fantasy（High Elves）与 PureNature 的许可是否允许
  把原始资产推送到**可公开访问**的仓库。如果不允许，
  需要在正式发布前改成私有仓库或单独的资产分发渠道。
- 本轮不阻塞开发：素材已在本地仓库中，本轮没有推送、没有部署。

### Q-030 派生场景与 NavMesh 带来的仓库体积

- 状态：**待用户决定是否需要优化**。功能上已经正常。
- 实测体积增量：

  | 资产 | 体积 | 存储 |
  | --- | --- | --- |
  | `Map02_Combat.unity`（派生战斗场景） | 38.9 MB | Git LFS |
  | `Map02_Combat/NavMesh.asset` | 32.7 MB | Git LFS |
  | `Map01_Task.unity`（派生任务场景） | 2.28 MB | 普通文本 |

  合计约 **74 MB**，且战斗场景与它 38.9 MB 的源场景**并存**。
- 为什么是这个代价：要保留烘焙光照、反射探针与一万多个 Prefab 实例的修改列表，
  必须整份派生场景；唯一能避开它的做法是运行时附加加载源场景，
  而那要求把第三方 Demo 场景加进 Build Settings，是本轮明确禁止的。
  详见 [ADR-0020](Docs/ADR/0020-p2-formal-world-scene-binding.md)。
- NavMesh 的 32.7 MB 是因为整张 1000×1000 地形都被烘了，
  而实际战斗只发生在生成点附近一小片区域。
  Unity 2021.3 自带的场景烘焙**没有范围参数**，要限定区域就得引入
  `com.unity.ai.navigation` 的 `NavMeshSurface`，而本轮禁止新增第三方包。
- 可选的优化方向（都需授权）：缩小地形、把战斗区域拆成独立场景、
  或者引入 AI Navigation 包用 `NavMeshSurface` 限定烘焙体积。

### Q-006 英雄、武器和NPC素材

- 状态：部分到位。P2灰盒角色已确定，正式英雄与武器仍待提供。
- 已确认：两名男性英雄顾沉岳、裴行舟；武器为长剑和太刀。
- P2灰盒角色：`Assets/Game/Art/Characters/Changli/Changli_TPose.fbx`（长离），**Generic Rig，不改为Humanoid**。
  已验证268个Transform、227根蒙皮骨骼、无空骨骼、根骨骼`Bip001Pelvis`，
  且`Assets/Game/Player_Animation`下20个动画的曲线路径全部匹配该模型。
  正式材质位于`Changli/Materials`，动画FBX只取AnimationClip，不实例化重复模型、不覆盖正式材质。
- 仍需要：顾沉岳与裴行舟的最终模型、骨骼、动画、服装、长剑/太刀Prefab、NPC模型及授权信息。
- 待确认：长离是P2的临时灰盒角色，还是要替换首发英雄设定。当前按"临时灰盒角色"处理，
  玩法文档里的英雄设定未改动。

### Q-007 怪物素材实际路径和授权

- 状态：**导入部分已关闭**（2026-10-05 用户确认授权并完成受控导入）；
  **正式发行前的分发条款仍待确认**。
- 来源（已逐字节核对，不是转述）：
  `E:\素材\30 Unity Asset Polygonal - Creatures Pack v1.0\Unity Asset Polygonal - Creatures Pack v1.0.unitypackage`，
  **38,110,997 字节**，SHA-256
  `70b6b6a423bac0080227f816c19c94b3133eb90b90b8b860528c7284a0412f18`，共 524 个条目。
  包内 10 个生物目录与玩法文档的十类怪物一一对应；暮影妖狼对应
  `Assets/Polygonal Creatures Pack/Polygonal Wolf/`（50 条，真实路径与用户清单一致）。
- 本轮只导入 **12 个资产**（模型 + `Base.FBX` + 7 个动画 + Black 材质与两张贴图），
  保留第三方原始目录与原始 `.meta`／GUID，全工程 2,184 个 GUID **零重复**。
  导入由 `Tools/import_polygonal_wolf.py` 执行：白名单写在代码里，
  运行时先校验包的字节数与 SHA-256，对不上直接拒绝。`.unitypackage` 本身不进仓库。
- 明确排除：其余 9 类怪物（P4）、2 个演示场景、3 个演示 Animator、
  `Rotate.anim`／`Post Processing.asset`／`Demo Ground.mat`、Brown/White 变体、
  全部 `W Root` 位移动画、本轮七个状态之外的动画，以及 Black 的第三方 Prefab
  （它引用已排除的演示 Controller，而本项目自建 Prefab）。
  完整清单与理由见 [暮影妖狼资源映射说明](Docs/Monster/duskshadow-wolf-asset-mapping.md)。
- 发行方 Meshtint Studio。包内只有 `Read me.txt` 与 `www.meshtint.com.txt`，
  **没有 LICENSE 文件**。用户已确认可用于本项目并授权导入，
  但**正式发行前仍需向 Meshtint Studio 确认分发条款** —— 这一项不因本轮导入而关闭。
- 记录：[ADR-0003 的 2026-10-05 补充](Docs/ADR/0003-embedded-unity-packages.md)
  把"第三方美术素材也要固定来源、版本与 SHA-256"写成规则。

### Q-008 音频素材与Wwise许可

- 状态：待确认。
- 需要：是否已有Wwise项目、音效库、音乐和商业许可。
- 默认建议：先通过`IAudioService`接入Wwise；若授权或安装暂缺，使用Unity Audio占位实现，后续无需修改业务代码即可替换。

## 3. 发布与基础设施

### Q-009 首发平台

- 状态：待最终确认。
- 已知基线：Windows、键鼠、1920×1080、30/45/60FPS。
- 需要：是否只发布Windows x64，以及是否计划Steam或独立启动器。
- 影响：输入、打包、签名、更新器、成就和崩溃上报。
- 默认建议：首版只支持Windows x64独立客户端。

### Q-010 阿里云环境

- 状态：单开发者基础云端环境已完成部署与验收；生产发布能力仍延期。
- 当前情况：新加坡Windows Server 2016 Datacenter轻量主机运行MySQL 5.7.26和.NET 10自包含Host；数据库、Bootstrap和LegacyNetworkV1只监听loopback，本地Unity通过手动SSH隧道使用。MySQL、Host、OpenSSH、防火墙、公钥认证、端口限制、健康检查和真实注册登录均已在线验收；重装后的最终云端/本地重启复验由用户明确免除。
- 已确认范围：只供当前开发者本地使用，不向其他玩家或公网客户端开放；当前不安装Redis/Tair、MQ、容器、OSS/CDN或集中监控。
- 当前管理边界：Windows防火墙只允许TCP 3389和22；为避免动态公网地址再次导致锁死，两条规则暂时允许任意来源。SSH禁止密码和交互Shell，专用公钥只能转发至loopback的5222与8011。本地自动隧道已取消，使用无触发器按需任务和桌面快捷方式手动启动/停止。
- 后续需要：恢复阿里云控制台访问后收敛并复核云平台侧防火墙规则；正式发布前再确认受支持Windows/OpenSSH版本、域名、TLS证书、独立数据库、备份、SLS/ARMS、环境隔离和预算。
- 安全规则：账号、AccessKey、数据库密码和证书不能写入仓库或文档，只通过密钥管理和部署环境注入。
- 当前决策：接受ADR-0006的开发期单机与SSH隧道拓扑；不得据此宣称生产可用。

### Q-011 账号与测试环境

- 状态：部分确认。
- 已确认：自动化和人工冒烟账号使用`p0_`前缀，密码不进入日志或文档；删除测试数据必须限定明确账号并获得用户许可。
- 当前待办：本轮云端Unity验收账号仍保留在`NK`库，是否清理由用户另行授权。
- 后续需要：GM权限以及Dev、Staging、Production配置和数据库隔离在进入正式发布准备前确认。
- 默认建议：至少建立Dev、Staging、Production三套配置和数据库，禁止生产数据回流开发环境。

### Q-015 Unity CI自托管Runner

- 状态：已关闭。
- 已完成：受信任Runner`NARAKA-Unity-Windows`已使用`self-hosted`、`Windows`、`X64`和`unity-2021.3.45f2c1`标签注册，仓库变量`NARAKA_UNITY_EDITOR_PATH`已设置，并建立当前Unity授权用户登录时启动的计划任务；系统级PowerShell 7已安装并进入PATH。服务端最终GitHub工作流运行`33459456358`通过。
- Unity处理：首轮远端Unity检出曾因网络失败；再次运行后在干净`Library`中出现EditMode `total=0`。已为EditMode程序集补充`TestAssemblies`标记，并按既定条件在统一脚本中加入独立导入/编译预热；从新的干净本地检出验证为预热退出码0且无C#编译错误、EditMode 14通过/1跳过/0失败、PlayMode 1/1，XML和三类日志均正常生成。
- 远端结果：Unity运行`33461381291`在干净`Library`上完成预热与测试，EditMode 14通过/1跳过/0失败、PlayMode 1/1，`unity-test-results` Artifact成功上传。此前的网络连接失败和计划任务PATH缺少`pwsh`均属于Runner环境问题，已排除；`ambiguous HEAD`未在最终运行中复现。
- 安全规则：不在外部Fork PR上执行自托管任务，不向CI注入`.env`、MySQL连接串或真实登录测试凭据。

### Q-016 三模型协作边界

- 状态：已关闭。
- 决策：DeepSeek只提供代码草案和Unity手工UI/挂载说明；Claude读取实际仓库进行代码复核、功能实现、运行验证和Bug修复；Codex负责跨模块集成、架构门禁、CI、服务端、数据库迁移和部署一致性。
- 约束：同一时间只有一个模型修改同一工作区或同一组文件；模型输出不是项目事实，只有进入当前仓库并通过实际验证的结果才能记为完成。
- 执行入口：`Docs/AI/three-model-collaboration.md`及其中链接的两份可复制提示词。

### Q-018 长离与首发英雄设定的关系

- 状态：待确认，本轮新增。
- 现象：P2第一阶段使用长离作为可操作角色，而玩法文档的首发英雄是顾沉岳与裴行舟两名男性英雄。
- 当前处理：按"P2临时灰盒角色"处理，不修改玩法文档的英雄设定，也不为长离编写技能数值。
  F/V技能使用可配置的灰盒伤害与范围，不冒充任何一名正式英雄的技能。
- 需要确认：长离是否进入正式英雄阵容；若进入，需要补技能、数值与定位，并同步玩法文档。

### Q-019 灰盒手感数值是否进入CSV配置管线

- 状态：待确认，本轮新增。
- 现象：P2的移动速度、转向速度、重力、镜头距离/俯仰/灵敏度/阻尼、体力、连招时间轴、
  蓄力阈值、F/V冷却与灰盒伤害目前由`PlayerTuningAsset`与`ThirdPersonCameraSettings`
  两个ScriptableObject承载，通过纯数据快照传入Model。
- 理由：这些是客户端手感与灰盒验证数值，不是服务端权威的经济数值；
  ADR-0010的CSV管线是为双端共享的权威配置建立的。快照边界已经就位，
  将来改由CSV供数不需要改Model与Controller。
- 需要确认：哪些项最终必须进入CSV并由服务端权威判定（例如正式伤害与体力），
  哪些项永久留在客户端表现配置里（例如镜头阻尼）。

### Q-017 Windows独立播放器构建被R3依赖阻断

- 状态：**已关闭**（2026-10-05 修复，并由一次真实的 Windows x64 独立构建验证）。
- 根因（此前已逐字节确认）：`NK/Assets/Plugins/R3/` 有 `R3.dll` 与三个依赖，
  **唯独缺** `System.Runtime.CompilerServices.Unsafe.dll`，而 `R3.dll` 的程序集
  引用表里确实有它。编辑器导入与 EditMode/PlayMode 一直正常，是因为 Editor 的
  Mono BCL 自带这个程序集；独立播放器构建才要求工程里真的有这份托管 DLL。
- 修法：从仓库内的
  `.tools/vendor-r3-1.3.1/src/R3.Unity/Assets/Packages/System.Runtime.CompilerServices.Unsafe.6.0.0/lib/netstandard2.0/`
  复制该 DLL（18,024 字节，SHA-256
  `01748200f2400c742aa689f1f5101bd6298efdfd92c00c18f4fa473847235ba9`）到
  `NK/Assets/Plugins/R3/`。
  **没有升级 R3**，**没有替换其他依赖**，**没有复制 XML 文档**。
  新 `.meta` 的 GUID 是 `01748200f2400c742aa689f1f5101bd6`（取该 DLL 自身 SHA-256
  的前 32 位，可复现、不复用任何现有 GUID，已核对与工程内 2,184 个 GUID 零冲突）；
  `PluginImporter` 设置与同目录三个依赖**除 guid 外完全一致**
  （Any 启用、Editor 禁用、Windows Store Apps 禁用、`validateReferences: 1`）。
- 来源无歧义的证据：三个既有依赖与同一个 vendor 目录下的对应文件**逐字节相同**
  （`Microsoft.Bcl.AsyncInterfaces` 16,000、`Microsoft.Bcl.TimeProvider` 32,416、
  `System.Threading.Channels` 75,952）。
- 构建验证（实际执行）：Unity 2021.3.45f2c1 以 `-buildWindows64Player` 构建，
  日志出现 `Dependency assembly - System.Runtime.CompilerServices.Unsafe.dll` 与
  `CopyFiles .../NARAKA_Data/Managed/System.Runtime.CompilerServices.Unsafe.dll`
  —— 它被解析并打进了播放器，产物里那份 DLL 与源**同一 SHA-256**。
  `Exiting batchmode successfully now!`、返回码 0、0 条 `error CS`、
  0 条 "not allowed to be included / could not be found"、0 条程序集冲突。
- 客户端启动验证（实际执行）：启动构建出的 `NARAKA.exe` 并运行 25 秒不退出，
  `MonoManager ReloadAssembly` 成功完成（这正是缺这个程序集时会失败的地方），
  日志中 Unsafe/R3 加载异常、Missing Script、Exception、crash **各 0 处**。
  仅有一条与本轮无关的既有告警：`WindowsVideoMedia error unhandled Color Standard: 0`
  —— 大厅背景视频 `Lobby_Animation.mp4` 的已知问题。
- SSH 隧道未开启，因此版本预检/登录不可用；按既定规则这不算失败，
  也**没有修改**服务器 IP、端口、协议或登录逻辑。
- 记录：[ADR-0003 的 2026-10-05 记录](Docs/ADR/0003-embedded-unity-packages.md)。

## 4. 玩法中暂未最终绑定的内容

### Q-012 正式任务对白和NPC表现

- 状态：任务结构已确定，文本和演出待设计。
- 已确认：六章主线目标、任务状态机和奖励方向。
- 待补：NPC姓名、对白、镜头、语音、动作、地图坐标和具体奖励数值。

### Q-013 正式数值平衡

- 状态：已有首版基线，需通过垂直切片验证。
- 待验证：怪物生命/伤害、武器倍率、技能范围、魂玉组合、掉落率、强化成本、商店价格和账号升级节奏。
- 原则：在P1/P2形成可玩数据后通过埋点和测试调整，不在没有实际战斗手感时继续堆叠公式。

### Q-014 UI资源映射表

- 状态：图片和背景动画已生成，Unity绑定未完成。
- 待做：确定每张图片对应的Screen/Popup/Atom，检查透明边缘、九宫格、Pixels Per Unit、压缩格式和Addressables Label。
- 当前处理：P0建立自动导入与校验工具；大厅相关界面按新路线图在P1逐模块绑定，Unity手工布局、素材与Inspector挂载由用户负责。
- 登录界面现状：正式登录界面已改为`AccountLogin.uxml`/`AccountLogin.uss`，登录静态背景已导入为`NK/Assets/Game/Art/UI/Backgrounds/AccountLoginBackground.png`，运行时代码不包含任何绝对素材路径。占位分层已删除。
- 登录背景待办：`AccountLoginBackground.png.meta`目前仍是Unity默认导入设置，需要在编辑器执行`NARAKA/Setup/Reimport UI Backgrounds`让`UiBackgroundTextureImporter`生效后复核；接入背景后的Unity编译与测试尚未运行。
- 登录标题：背景图顶部已烘焙NARAKA大标题，卡片内不再重复显示标题。`login_title_NARAKA_overlay.png`（1920×1080、带Alpha）暂未使用，若将来需要标题与背景分离再评估。
- 动态背景：`login_fog_light_only_loop_30s_1080p.mp4`（19.3 MB）与`lobby_fog_light_cloth_loop_30s_1080p.mp4`按当前决定不入库，`.gitignore`已排除`Assets/Game/Art/UI/Backgrounds/`下的`*.mp4`及其`*.meta`。是否接入动态背景、以及大体积二进制是否改用Git LFS，留待用户确认；P1大厅还涉及视频、加载图和大量UI切图，提交前必须统一决定许可与大文件策略。
- 大厅与加载图：相关素材已经出现在当前未提交工作区，不再按旧路线图笼统归为P4；最终绑定随P1大厅和后续P2/P3场景需求分别验收，且由用户完成Unity手工操作。

### Q-022 最终攻击力的合成规则

- 状态：待确认，2026-09-29 新增。
- 现象：权威伤害公式是 `RawDamage = FinalAttack × SkillMultiplier`，
  但没有任何文档说明 `FinalAttack` 怎么算出来。
  玩法文档给了英雄攻击力（顾沉岳 100）和武器攻击力（长剑 1 级 120、20 级 272），
  没有说两者是相加、武器为主英雄为修正，还是乘算。
- 当前处理：P2 垂直切片取**相加**，即 `FinalAttack = 100 + 120 = 220`，
  写在 `PlayerTuning.FinalAttack` 与 `PlayerTuningAsset.finalAttack` 里，
  并在代码注释与 [ADR-0019](Docs/ADR/0019-authoritative-damage-counter-execute.md) 中
  明确标注为灰盒假设。
- 需要确认：正式合成规则；魂玉、护甲与强化如何参与；
  以及最终由服务端权威判定时客户端这份数值的定位（展示还是预测）。

### Q-023 反击与处决没有动画

- 状态：待确认，2026-09-29 新增。
- 现象：反击（Space）与处决的规则已经完整实现并有 EditMode 覆盖，
  但长离这套动画里没有经过确认的反击/处决动作。
- 当前处理：按「不随意复用其他动画」的约束（与右键纵击同一条原则），
  `PlayerAnimation.Counter` 与 `PlayerAnimation.Execute` **不映射任何 AnimationClip**；
  投影层遇到没有 State 的动画时保持上一个姿态，不报错也不回退到 Idle。
  因此这两个动作目前只有逻辑效果，没有表现。
- 需要确认：使用哪两个动画片段，或者是否需要新制作。
  美术到位后只需在 Animator 里补两个 State 并在投影层登记，业务规则一行都不用改。

### Q-024 P2 灰盒战斗数值

- 状态：待确认，2026-09-29 新增。
- 现象：暮影妖狼的全部数值、玩家的技能倍率与防御、处决动作时长都是为了跑通
  战斗闭环而给的占位值，没有经过任何平衡验证。
- 当前处理：怪物数值集中在 `Config/Source/monsters.csv` 与 `monster_skills.csv`，
  两张表都有 `BalanceStatus` 列且当前全部为 `P2Graybox`；
  `GameConfigCatalogTests` 断言它不是 `Confirmed`，因此这些值不可能被静默当成已确认设计。
  完整数值表见开发进度文档。
- 需要确认：正式平衡值，以及哪些项最终由服务端权威判定（与 Q-019 合并考虑）。
- **2026-10-05 新发现的一处不自洽**：暮影妖狼的配置攻击距离是 **3.2**，
  而它命中盒的实际触达只有 **2.92**（`localOffset.z` 1.2 + `radius` 1.4 +
  玩家胶囊半径 0.32）。也就是说"在 3.2 出手"本身打不到玩家。
  这一点此前被另一个缺陷掩盖着：狼会一直顶到 0.65 才停，所以每次都打中。
  当前处理：追击停止距离取 `攻击距离 − 0.8 = 2.4`，稳稳落在触达之内，
  因此**不影响可玩性**。正式平衡时攻击距离与命中盒尺寸应当一起定。

### Q-027 蓄力动画想向前跃进 8.47 单位，玩法文档规定它不改变坐标

- 状态：待确认，2026-10-04 新增。当前按玩法文档执行（不位移），不影响可玩性。
- 背景：用户报告"蓄力动画没有飞上天空，而是在原地旋转"。腾空确实在片段里，
  已经修好（见 [ADR-0015 的第三次修订](Docs/ADR/0015-p2-player-hfsm-and-animator-projection.md)）：
  还原后是升到 +5.227、落回 +0.062 的弧线，垂直分量现在保留，蓄力会飞起来。
- 剩下的问题是**水平**那一半：同一段动画还带着 **8.470 单位的向前跃进**，
  而玩法文档第 7 节写的是"三段普通攻击、蓄力与 F 技能**不改变角色坐标**：
  攻击前后位置一致"，蓄力的配置位移因此是 0，动画里那段前冲被抵消掉。
  观感上就是"原地起跳"而不是"跃进"。
- 需要确认：
  1. 保持现状（蓄力原地起跳，符合已确认设计）；
  2. 或者给蓄力配一个前冲位移（需要改玩法文档第 7 节，
     并重新确认"攻击不改变坐标"这条 2.1 纠错规则的适用范围）。
- 相关：垂直方向不存在这个冲突 —— 弧线净变化是 +0.062，
  "攻击前后位置一致"仍然成立。

### Q-026 新动画集的导出端遗留问题

- 状态：待确认，2026-10-04 新增，当日根据用户在 Unity 里的实测报告改写。
- 背景：用户重做了全部动作（旧动画没有头发飘动）。换版本身与由此暴露的两个缺陷
  都已修复并通过自动化验收，见
  [ADR-0015 的两次 2026-10-04 修订](Docs/ADR/0015-p2-player-hfsm-and-animator-projection.md)。
  以下是**只能在导出端解决**的遗留项。

1. **新 idle 不是一个循环动作（唯一还有可见影响的一项）。**
   `Stand1_Action03` 烘焙的头发模拟是"从静止开始逐渐摆动"的一次性动作：
   发梢在 1.0 秒处偏离首帧 55.75°，到片段末尾只回落到 41.93°，从不回到起点；
   身体（裙摆）也差 12.34°。逐帧扫过全部 141 个候选结束点，最接近的也有 30.87°，
   所以**裁剪到任何更短的范围都救不回来**。Unity 的 Loop Pose（`loopPose`）
   对 Generic + `NoAvatar` 的片段实测完全无效（开与不开都是 41.93°）。
   对照：旧 idle 在同样 2.667 秒下只有 0.17°，说明原来的导出流程是对的。
   **影响**：idle 每 2.667 秒跳一次头发，这是用户报告的"idle 卡顿"里
   修掉裁剪范围之后剩下的那一半。走路与奔跑已经无缝（0.29°／0.00°）。
   需要确认：重新导出 `Stand1_Action03`，让头发模拟闭环
   （例如让模拟跑过几个周期后截取后面的一个周期）。
   重新导出后用 `NARAKA/Diag/P2.2 Idle Loop Scan` 复测即可；
   闭合之后 `PlayerAnimationContractTests.KnownBrokenLoops` 里的登记项要删掉。

2. **F 技能没有新动画。** 新动作文件夹里没有 `Skill01`，因此
   `AM_Skill01` 仍是旧版，是 18 个动画状态里唯一还在用旧动画的一个。
   按用户给的规则（"没有对应的新动画则还是采用旧版"）保持不动，
   但这意味着 **F 技能的头发不会飘**，与其他动作观感不一致。
   需要确认：是否补一条 `Skill01` 新动画。

3. **导出设置与旧版不一致（已在引擎里抹平，但建议从源头改回）。**
   三处：`Root` 节点多了 `Lcl Rotation=(+90,0,0)` 与 `Lcl Scaling=0.3937`；
   FBX 时间模式从 30fps 改成 60fps；`Root` 位移通道挂在垂直轴上且差 2.54 倍。
   当前处理：前者由 `PlayerAnimationRootFixup` 在导入期按"文件静止姿态 → 模型静止姿态"
   重定基抹平（旧片段算出单位四元数，不受影响）；
   中者由 `ApplyImportSettings` 每次从文件重取完整 Take 范围解决；
   后者由 `RootMotionCanceller` 整条锁回静止值。三者都有契约测试钉住，
   **运行时没有任何可见问题**。
   需要确认：是否在导出端改回与旧版一致的设置。改回之后上述三处修正
   都会自动变成空操作（补偿量算出来就是单位值），不需要改代码。

4. **`Stand1_Action03` 与 `Stand1_Action03_SEQ1` 里带了一份重复骨架。**
   这两个片段各含一整套 `W0_` 前缀的骨骼曲线（`W0_Root/W0_Bip001/...`），
   曲线路径数因此是 491 条而不是 274 条，曲线总数 4727 vs 其他片段的约 2600。
   Unity 会忽略这些路径，不影响表现；但 idle 是播放最频繁的动画，
   这份重复数据白占内存与每帧采样开销，文件也因此有 17–20 MB。
   需要确认：是否重新导出这两个片段，去掉重复骨架。
   （如果为了第 1 项重新导出 idle，这一项可以一起解决。）

- 另外记录（不是问题）：新动作文件夹里的 `·长离_标准T姿势.fbx` 也带着
  `Root.Lcl Scaling=0.3937`，而它的文件单位是 1.0、动画是 2.54，
  `Lcl Rotation` 是 1.23° 而不是 90° —— 新导出的那一套**自己也不自洽**，
  所以换成这个模型同样对不上，保留已验收的正式模型是正确选择。
  骨骼层级与曲线路径 100% 匹配正式模型这一点仍然成立。

### Q-025 Unity 编辑器许可证在本机失效

- 状态：**已关闭**。2026-10-04 用户重新激活许可证
  （`C:\ProgramData\Unity\Unity_lic.ulf` 已恢复），批处理编译、装配工具与
  EditMode/PlayMode 测试均可正常执行。
- 历史记录：2026-09-29 发现。
- 现象：2026-09-29 14:35（UTC）之前的批处理运行全部正常；14:36 之后
  每一次 `Unity.exe -batchmode` 都以退出码 1 结束，日志为
  `No valid Unity Editor license found. Please activate your license.`
  许可证客户端日志同时报 `No ULF license found.` 与
  `No license activation found for this computer.`，
  且 `C:\ProgramData\Unity\` 下已经没有 `Unity_lic.ulf`。
- 影响：本机无法再运行 Unity 编译、Editor 装配工具、EditMode/PlayMode 测试与独立播放器构建。
  这是环境问题，与本轮代码无关：同一份代码在 14:33 的运行里完成了 463 项 EditMode 测试。
- 需要用户处理：打开 Unity Hub → 登录账号 → Preferences → Licenses → Add →
  获取个人版许可证（或重新激活现有许可证），随后即可重新运行验证。
  **不代劳**：这一步涉及用户的 Unity 账号凭据。

## 5. 已关闭的重要冲突

### D-001 Unity版本

- 决策：锁定Unity 2021.3 LTS + URP。
- 说明：旧TDD中“建议Unity 6”的内容不再适用；Unity 6专属包必须替换为2021.3兼容实现。

### D-002 断网与闪退

- 决策：断网或闪退直接进行幂等远征结算，下次登录进入大厅查看摘要。
- 说明：旧GDD/TDD中“战斗断线保留5分钟恢复”的建议不再适用。

### D-003 地图二离开方式

- 决策：必须走到固定返回传送门，触碰后立即异步加载地图一；不能菜单返回，也不在地图一结算。

### D-004 死亡复活

- 决策：死亡不花金币，只丢失本次怪物掉落，随后回到地图一重生点。

### D-005 顶层架构

- 决策：客户端业务必须采用模块化MVC。
- 说明：R3、Atomic Design、ECS、行为树和事件总线都是MVC内部工具，不得改变顶层依赖方向。

### D-006 P0登录前版本检查

- 决策：客户端启动后通过独立Bootstrap端点核对ClientVersion、ConfigVersion和ProtocolVersion；检查成功前禁止注册和登录，失败后允许用户重试。
- 说明：该HTTP预检不修改冻结的`LegacyNetworkV1`传输层。本机开发只允许loopback HTTP，远端必须使用HTTPS；P5再补齐正式配置Manifest签名、哈希校验、A/B缓存与回滚。
- 执行结果：客户端Bootstrap模块化MVC、服务端`GET /bootstrap/config-version`、UI阻塞提示及自动化测试已经完成，无新增待确认项。

### D-007 大厅与账号长期系统前置到P1

- 决策：已关闭。2026-09-07用户确认把原P4大厅经济整体前置为新P1；原P1战斗垂直切片、原P2远征闭环、原P3内容系统依次顺延为P2、P3、P4，P5和P6保持不变。
- 范围：P1按P1.0至P1.9分批完成账号快照、货币、英雄/兵器/宠物选择、仓库、商店、锻造、抽奖、签到、账号等级奖励、红点、好友和聊天入口的真实功能，不能以静态占位或本地假数据宣称完成。
- 社交最小范围：好友申请、接受、拒绝、删除、屏蔽、在线状态和一对一文字聊天；世界频道、群聊、语音与文件传输不在P1。
- 边界：战斗仍从P2开始，远征/地图闭环仍在P3；P1的开始游戏按钮只负责验证进入后续场景的边界。
- 执行结果：`p1-lobby-systems-002`已完成云端集中部署、迁移`0001`至`0009`复验、版本与能力门禁检查以及真实客户端验收；2026-09-21用户确认没有问题，P1正式关闭。P2仍需单独授权后开始。

### D-008 P1业务消息扩展不解冻LegacyNetworkV1传输层

- 决策：已关闭。采用ADR-0007：保留握手、AES/KDF、帧、Protobuf编码机制、心跳、Socket和线程模型，只在客户端/服务端适配边界增加成对业务契约、类型注册与路由映射。
- 约束：既有协议名、协议号和字段不可修改或复用；写请求必须具备RequestId或OrderId，身份来自认证会话，账号资产和业务结果由服务端权威判定。
- 验证：每组新增消息必须有兼容、序列化、路由、认证、超时/取消、幂等和端到端测试。任何需要修改冻结传输机制的实现都必须暂停并另行取得授权。

### D-009 P1玩法基线修正

- 决策：已关闭。2026-09-08用户确认三种货币（铜币/幻丝/金币，各1000初始，"金砖"不得出现）、全部仓库物品统一堆叠（取消"实例物品"概念）、锻造材料足够必定成功且服务端零随机、抽奖五档品质白/蓝/紫/金/红且20抽保底出红、签到每周期一次补签、成就经验与账号经验完全独立。
- 记录：见 [ADR-0009](Docs/ADR/0009-p1-gameplay-baseline-corrections.md)，冲突条目已同步回`NARAKA_完整玩法设计.md`。

### D-010 配置管线改用CSV源表加本地编译器

- 决策：已关闭。Luban不纳入本项目；配置源为`Config/Source/`下的UTF-8 BOM CSV，由`Tools/Config/Naraka.ConfigCompiler`编译成双端共享的规范化JSON。
- 理由：Unity运行时不该读xlsx，云主机按ADR-0008不安装构建工具，双端必须消费同一份生成物。
- 记录：见 [ADR-0010](Docs/ADR/0010-csv-config-pipeline.md)。

### D-011 初始货币与已部署迁移的冲突

- 决策：已关闭。不修改已部署的`0001`/`0002`迁移，也不依赖列默认值；改为新增`account_grants`与`currency_ledger`，登录时在一个事务里幂等**累加**发放StarterGrant。
- 说明：规则是"这个账号有没有领过"，不是"余额是不是0"，因此花光了的老账号不会被补满，重复登录也不会重复发放。
- 记录：见 [ADR-0011](Docs/ADR/0011-starter-grant-and-currency-ledger.md)。

### D-012 旧云端兼容与红点聚合

- 决策：已关闭。Bootstrap新增可选`serverCapabilities`字段；当Bootstrap版本门禁匹配但Host不返回它时，客户端退回P1.1-A兼容集合，其余入口显示"服务器功能尚未升级"且不发送未知协议。完整P1的`p1-config-1`门禁会先拒绝旧`p0-config-1`云端。Host只声明自己真正实现的能力。
- 红点：路径前缀树 + `Version`/`SeenVersion`，业务模块经MessagePipe发布来源事件，界面只订阅只读状态；好友在线绿点/灰点不属于红点系统。
- 记录：见 [ADR-0012](Docs/ADR/0012-red-dot-prefix-tree-and-server-capabilities.md)。

### D-013 P2玩家动作基线修正

- 决策：已关闭。2026-09-26用户确认体力上限20、`Move_F`消耗10、死亡后体力恢复至20、
  `Move_F`取消无敌帧、普通攻击统一使用鼠标左键（右键纵击推迟到武器/怪物阶段）、
  蓄力只实现2秒满档（0.6/1.2秒仅作预留配置位，不产生行为差异）。
- 保留：每秒恢复5、动作结束0.75秒后开始恢复、受击后额外暂停0.5秒。
- 记录：见 [ADR-0013](Docs/ADR/0013-p2-player-action-baseline.md)，冲突条目已同步回`NARAKA_完整玩法设计.md`。

### D-018 动作时长必须来自动画片段，根位移由代码抵消

- 决策：已关闭。2026-09-28 第二轮人工验收暴露四个现象，根因两条：
  （1）所有动作时长都是估计值，比片段短的都把动画拦腰截断
  （待机动作 3 秒 vs 实际 15.067 秒、Burst02 2 秒 vs 5.5 秒、Burst01 2 秒 vs 7.233 秒）；
  （2）动画在根骨骼上烘焙了水平位移，动作结束回到 Idle 时骨架被插值拉回原点，
  表现为"回退一小步"。
- 修法：动作时长与动作位移由 `Rebuild Player Animator` 从 `AnimationClip` 同步，
  命中窗按比例缩放，并新增 EditMode 契约测试守住这条关系；
  根位移由 `RootMotionCanceller` 在运行期抵消水平分量。
  Unity 的 Root Motion 提取路线已实测无效并回退：模型与动画都是 Generic 且
  `avatarSetup = NoAvatar`，没有 Avatar 就没有 Root Motion 节点。
- 顺带修：冲刺进行中不再恢复体力；加载进度改取真实进度与时间进度的较小值；
  停止动作减速改为指数衰减以匹配动画烘焙距离。
- 记录：见 [ADR-0015](Docs/ADR/0015-p2-player-hfsm-and-animator-projection.md) 与
  [ADR-0016](Docs/ADR/0016-p2-persistent-app-root-and-real-scene-progress.md)。

### Q-020 攻击与技能动画偏长，连招节奏待定

- 状态：**已关闭**（2026-09-28 用户选择「加播放速度」，2026-09-29 人工验收通过）。
- 决策：为每个动作引入主体与后摇两档播放速度，业务时长 = 片段长度 / 速度；
  所有窗口改用片段时间表达，因此调速度不会让窗口跑到动作之外。
  记录见 [ADR-0015 的 2026-09-28 修订](Docs/ADR/0015-p2-player-hfsm-and-animator-projection.md)。
- 历史现象：
- 现象：按实测片段长度，`Attack01` 3.03 秒、`Attack02` 4.17 秒、`AM_Skill01` 4.60 秒、
  `Attack04_1` 5.57 秒。动作现在会完整播放（这是修好的部分），但连招因此偏慢：
  第一段要 1.07 秒才进入连段窗口。
- 可选方向：给每个动作加一个播放速度（`AnimatorState.speed`），业务时长 = 片段长度 / 速度；
  或者接受当前节奏，把它当作长离这套动画的固有手感。
- 当前处理：不擅自发明速度值。先让动作播完整，节奏由用户实机确认后再定。

### Q-021 CharacterController 尺寸与模型比例不符

- 状态：待确认，本轮发现。
- 现象：角色模型 SkinnedMeshRenderer 的 T-Pose 包围盒高约 4.04 单位，
  而 Prefab 上的 `CharacterController` 高 1.8、半径 0.32、中心 0.9 —— 胶囊只覆盖下半身。
- 影响：碰撞、台阶、坡度与地面检测都会按一个比模型小一半的胶囊计算。
  相机轴心高度已按用户要求调到 2.8，侧面印证模型确实接近 4 单位高。
- 当前处理：未擅自改动。用户确认模型的目标尺度（缩放模型还是放大胶囊）后再改。
- 相关：移动速度（走 5.0 / 跑 7.5）与冲刺距离 12.276 都是按这套单位来的，
  如果决定缩放模型，这些值需要一起重算。

### D-022 正式暮影妖狼表现层接入

- 决策：已关闭。2026-10-05 用户授权导入素材并选择「方案 A」（重命名表现层脚本）。
- 正式资源接入本身没有重写意图选择、HFSM、技能调度、阶段门控、伤害、命中去重、
  脱战、休眠、两张 CSV、生成点上限或 HUD 接口。视觉验收随后发现攻击冷却期间狼会继续
  往玩家胶囊上顶；同一提交因此增加 `MonsterSenses.IsWithinEngageRange` 这一项几何感知，
  让追击意图在已到达有效命中距离时停步等待冷却。攻击距离、命中盒、冷却和伤害数值均未改。
- 表现层补了两件纯表现的事：
  （1）`MonsterAnimatorProjector` 把 `MonsterFrameOutput.Animation`
  投到同名 Animator State（**7 State / 0 Parameter / 0 Transition**，默认 Idle）——
  这个字段从 P2.2 第一天就由 HFSM 产出，只是方块替身没有 Animator、没人消费它；
  （2）颜色反馈从单个 Renderer 改成全部身体 Renderer（排除预警面片），
  仍用 `MaterialPropertyBlock`，不生成材质实例。
- `GrayboxWolfView` → `DuskshadowWolfView`，`.cs.meta` 的 GUID 原样保留，
  两个 Prefab 的脚本引用都没断。方块替身 `GrayboxWolf.prefab` 保留为
  **开发回退资产**，正式场景不引用它，并有契约测试守住。
- 材质：第三方源材质是内建 `Standard`（URP 下粉色），
  只**新建**项目自有的 `Universal Render Pipeline/Lit` 适配材质引用同样的贴图，
  **不改第三方资产**。
- 顺带修掉一个一直是空操作的设置：装配工具里的 `agent.updateRotation = false`
  从来没写进过资产（Unity 2021.3 里它不是序列化字段），
  真正拦住 Agent 自己转向的是序列化的 `angularSpeed = 0`。
  现在把 `updateRotation` 放到 `DuskshadowWolfView.Awake` 里运行期关闭，
  契约测试改成断言那个确实会被序列化的字段。
- 不因为有动画就改规则：`@Breath Attack` 对应的是**已经存在**的赤瘴吐息技能；
  没有因为包里还有 `@Howl`／`@Pound Attack`／`@Jump` 就给狼加技能。
  动画播放速度一律保持 1，调速按 Q-020 的既定做法交人工验收决定。
- 记录：[ADR-0018 的 2026-10-05 修订](Docs/ADR/0018-monster-behavior-tree-and-hfsm.md)
  与 [资源映射说明](Docs/Monster/duskshadow-wolf-asset-mapping.md)。

### D-021 待机动作延迟 5 秒改为 2.4 秒（遮盖 idle 循环缝）

- 决策：已执行。2026-10-04 用户提出"把 5 秒改成 2.7 秒，这样就不会有 idle 突变的突兀感觉"。
  思路成立 —— 新 idle 的循环缝在 2.667 秒，只要在播到边界之前切进待机动作就看不到。
- 但 2.7 秒差一点点：切入要走 0.1 秒交叉淡入，淡入期间 Idle 还在推进，
  所以约束是「延迟 + 0.1 < 2.667」，即延迟 < 2.567。2.7 秒落在跳变之后 0.033 秒。
  取 **2.4 秒**，留 0.167 秒余量。
- 副作用：待机循环从 13 秒（5 + 8）变成 10.4 秒（2.4 + 8），
  待机动作占站立时间从 62% 升到 77%。观感取舍，由人工验收判断。
- 这是**遮盖**不是修复：片段本身仍不闭环（[Q-026](#q-026-新动画集的导出端遗留问题) 第 1 项）。
  重新导出闭合之后延迟可以调回 5 秒，契约测试会自动让路。
- 玩法文档没有规定过这个延迟（5 秒只是配置值，不是已确认设计），因此不需要改文档。

### D-020 换用带头发飘动的新动画集

- 决策：已关闭。2026-10-04 用户提供新动作并要求"有新版就换、没有就留旧版"。
  28 个动画按显式映射表替换（新名无 `AM` 前缀，旧名部分带，逐条对应），
  **保留工程内旧文件名、只换文件内容**，因此 Animator、`.meta` 的 `loopTime`
  与 `PlayerTuning.asset` 的引用零风险、GUID 零变动。
  6 个没有新版的动画保持旧版，其中 `AM_Skill01`（F 技能）是唯一还在用旧动画的状态。
- 骨骼与材质：新动画的骨骼曲线 100% 匹配正式模型 `Changli_TPose.fbx`，
  材质仍是 `Changli/Materials` 下的 8 个 `MI_*`（`.meta` 的 `externalObjects` 重映射未改），
  因此**不换模型**。
- 根位移：新导出的位移通道同样带有`+90°`轴向与`1/2.54`缩放差异；
  `PlayerAnimationRootFixup`在导入期统一还原。`RootMotionCanceller`最终只抵消代码已经驱动的水平分量，
  默认保留蓄力`Attack10`的垂直腾空；垂直净变化不为0的导出残留由导入后处理器压平。
- 片段长度：只有待机动作（15.067 → 8.000）与第二段普攻（1.833 → 2.667）变了，
  后者的命中窗/连段窗/后摇起点按 ×1.4548 等比缩放，相对节奏不变。
  工具不再自动重写动作位移与停止距离；契约测试会断言已验收配置与还原后的实测行程一致。
- 记录：见 [ADR-0015 的 2026-10-04 第二、第三次修订](Docs/ADR/0015-p2-player-hfsm-and-animator-projection.md)
  与 [Q-026](#q-026-新动画集的三处导出问题)。

### D-019 P2.2 怪物模块与权威伤害

- 决策：已关闭。怪物行为树只产出意图，怪物 HFSM 是「当前动作」的唯一真相，
  动作层不空闲时行为树的结果不生效；行为树自研、`noEngineReferences`、决策时零分配，
  决策频率由配置的 5–10Hz 决定，休眠时拉长到 1 秒。
  怪物数值进 ADR-0010 的 CSV 管线（新增 `monsters.csv` 与 `monster_skills.csv`，
  `SchemaVersion` 提升为 `1.1.0`），并带 `BalanceStatus` 列自证是灰盒值。
- 伤害：`DamageFormula` 是唯一实现，玩家与怪物共用；防御在受击方一侧扣；
  动作配置保存技能倍率而不是绝对伤害；反击与处决进入 Model 层并有完整 EditMode 覆盖；
  新增的 `Invulnerable` 标签只属于处决。
- 记录：见 [ADR-0018](Docs/ADR/0018-monster-behavior-tree-and-hfsm.md) 与
  [ADR-0019](Docs/ADR/0019-authoritative-damage-counter-execute.md)。

### D-017 移动改为相机相对

- 决策：已关闭。2026-09-28用户人工查看后确认：WASD四个方向都移动、方向相对摄像机、
  角色转向目标方向后只播前进动画、取消S后退与A/D原地转身；
  摄像机以角色为中心环绕，鼠标改变方位角与俯仰角，实现对标永劫无间。
- 影响：`Walk_B`/`Run_B`不再被引用（文件保留，`.meta`已恢复默认）；
  `LocomotionState`与`PlayerAnimation`的四个前后向枚举合并为`Walk`/`Run`；
  Animator从20个State减为18个。本决策取代ADR-0013引用的旧移动规则，
  ADR-0013关于体力、攻击键与蓄力的结论不变。
- 记录：见 [ADR-0017](Docs/ADR/0017-camera-relative-locomotion.md)。

### D-014 P2输入与镜头依赖定版

- 决策：已关闭。`com.unity.inputsystem` 1.7.0 与 `com.unity.cinemachine` 2.10.1，
  依据是Unity 2021.3.45f2c1编辑器自己的包清单声明；两者已解包存在于本机npm缓存，断网可还原。
  `activeInputHandler`设为2（Both），因为两个第三方演示脚本仍使用`Input.GetAxis`。
- 记录：见 [ADR-0014](Docs/ADR/0014-p2-input-system-and-cinemachine.md)。

### D-015 玩家状态机自研，Animator只作单向投影

- 决策：已关闭。不引入UnityHFSM；玩家分层状态机位于`noEngineReferences`的Model程序集，
  可在EditMode逐帧断言。经ADR-0017取消后退与原地转身状态后，Animator Controller为18个State、0个Parameter、0条Transition，
  业务层从不读取Animator的State名、Trigger或normalizedTime，也不使用Root Motion。
- 记录：见 [ADR-0015](Docs/ADR/0015-p2-player-hfsm-and-animator-projection.md)。

### D-016 持久化App Root、真实异步进度与Addressables暂缓

- 决策：已关闭。新增持久化`AppRootLifetimeScope`只承载跨场景能力，`GameLifetimeScope`成为其子Scope；
  加载进度接入真实`AsyncOperation`并把0–0.9归一化为0–1，未就绪时封顶99%；
  Addressables本阶段明确暂缓，`ISceneLoader`即后续迁移边界。
  原属P3的两图灰盒、传送门、异步加载与死亡返回提前到P2第一阶段。
- 附带规则：发起场景切换的场景级对象不得持有该切换的取消权——这次切换会把它自己卸载。
- 记录：见 [ADR-0016](Docs/ADR/0016-p2-persistent-app-root-and-real-scene-progress.md)。

### Q-028 超过休眠距离时，已交战怪物是回家还是原地休眠

- 状态：**已关闭**，2026-10-05 用户确认采用建议方案。
- 决策：已交战且远离出生点时，`Leash/Recover`优先于`Dormant`，怪物先脱战回家；
  只有未交战、也不处于回家过程中的怪物，才因超远玩家进入休眠并降低决策频率。
- 实现：行为树将`Dormant`移到脱战与持续回家分支之后；新增
  `AnEngagedWolfBeyondDormantDistanceReturnsHomeBeforeSleeping`边界回归测试。
- 验收：Unity EditMode **473项，472通过、0失败、1项环境跳过**；PlayMode **27/27通过**。

## 5. 当前待确认

- **第三方怪物素材的正式分发条款**：用户已确认素材可用于本项目并授权导入，但素材包没有
  LICENSE 文件。把原始 FBX、贴图和材质推送到可公开访问的 GitHub 仓库之前，仍需确认
  Meshtint Studio 的源码仓库分发条款，见 Q-007。
- **P2战斗HUD、换版动画与正式场景人工验收**：**已于 2026-10-06 通过**。
  用户从正常登录流程进入正式地图并确认 Unity 实际效果没有问题；HUD布局仍由用户手工维护，
  后续自动化或装配工具不得覆盖。
- **正式暮影妖狼视觉验收**：**已于 2026-10-05 通过**。验收中只报出一个行为问题
  （怪物在攻击冷却期间推着角色移动），已修复并有三条回归测试，见开发进度文档。
- **狼与玩家的体型比例**：正式狼肩高约 0.69、体长约 1.54，而玩家模型高约 4.04
  （[Q-021](#q-021-charactercontroller-尺寸与模型比例不符) 的模型比例问题仍未解决）。
  本轮**没有缩放狼**，也没有动攻击距离 3.2 与感知距离 14 —— 这两项属于已验收配置。
  需要用户看过实际画面后决定是否调整模型比例。
- **P2性能门禁**：GC 已有实测证据 —— 战斗稳定态相对空闲基线的每帧分配增量为 **0 B**
  （覆盖追击、普攻、吐息、受击四条路径）。**渲染线程、GPU 与总帧预算仍是"未验证"**：
  自动化以 `-nographics` 运行，GPU 是 Null Device、分辨率 640×480，这三项没有可信值，
  必须由人把 Profiler 连到 Development Build 上在 1920×1080 下采集。
  记录见 [P2 战斗性能验收记录](Docs/Performance/p2-combat-performance-record.md)。
- **P2.3 正式运行场景视觉验收**：**已于 2026-10-06 通过**。正式地图观感、移动、
  镜头、传送、战斗、HUD与加载进度由用户在 Unity 实机确认没有问题。清单见
  [P2.3 正式运行场景人工验收清单](Docs/Scenes/p23-formal-world-scene-manual-acceptance.md)。
- **P2.3 后的性能重测**：GC 已在正式环境重测，战斗相对空闲基线仍为 **0 B/帧**。
  渲染线程、GPU与1920×1080总帧预算仍必须由人把 Profiler 连到 Development Build 上采；
  这是当前 P2 唯一剩余的技术验收门禁。
- **Q-026新动画导出遗留**：Idle不闭环、F技能仍使用旧版、Idle与IdleVariation带重复`W0_`骨架曲线，
  等待美术重新导出。
- **抽奖红色卡背素材**：暂用玄夜卡背，等待正式素材。
