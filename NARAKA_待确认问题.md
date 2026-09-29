# 《NARAKA》待确认问题与决策记录

版本：1.13
更新日期：2026-09-28
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

- 状态：明确延期。
- 已确认：当前不进行正式地图设计，等待用户提供素材。
- 后续需要：地图包、许可范围、地形尺寸、场景拆分能力、NavMesh限制和目标性能。
- 当前处理：只建立地图一/地图二灰盒测试场景和抽象ID。

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

- 状态：路径已确认，文件级导入核对待P1执行。
- 来源：`E:\素材\30 Unity Asset Polygonal - Creatures Pack v1.0\Unity Asset Polygonal - Creatures Pack v1.0.unitypackage`。
- 当前处理：十类怪物玩法映射已经完成，资产导入规则待核对后生成。

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

- 状态：待确认，本轮发现，未修复。
- 现象：以`-buildWindows64Player`构建Windows独立播放器失败，报`The Assembly System.Runtime.CompilerServices.Unsafe is referenced by R3 ('Assets/Plugins/R3/R3.dll'). But the dll is not allowed to be included or could not be found.`，构建结果为Failure、退出码1。
- 范围：与本轮登录界面无关；Editor导入、EditMode与PlayMode均正常，只有独立播放器构建路径受影响。P0从未把独立播放器构建列入退出条件，因此不属于回归。
- 影响：在修复前无法用已构建客户端做人工验收或发布验证，人工Play Mode验收只能在Unity编辑器内进行。
- 后续需要：确认按ADR-0003的内嵌包基线补齐`System.Runtime.CompilerServices.Unsafe`托管DLL及其导入平台设置，或改用其他受支持的引入方式；修复必须单独提交并重新执行Unity编译与测试。
- 附带记录：该次失败的构建尝试曾把`NK/ProjectSettings/UnityConnectSettings.asset`的`m_Enabled`改为1，已在本轮还原为0。

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

- 状态：待确认，本轮新增。
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
  可在EditMode逐帧断言。Animator Controller为20个State、0个Parameter、0条Transition，
  业务层从不读取Animator的State名、Trigger或normalizedTime，也不使用Root Motion。
- 记录：见 [ADR-0015](Docs/ADR/0015-p2-player-hfsm-and-animator-projection.md)。

### D-016 持久化App Root、真实异步进度与Addressables暂缓

- 决策：已关闭。新增持久化`AppRootLifetimeScope`只承载跨场景能力，`GameLifetimeScope`成为其子Scope；
  加载进度接入真实`AsyncOperation`并把0–0.9归一化为0–1，未就绪时封顶99%；
  Addressables本阶段明确暂缓，`ISceneLoader`即后续迁移边界。
  原属P3的两图灰盒、传送门、异步加载与死亡返回提前到P2第一阶段。
- 附带规则：发起场景切换的场景级对象不得持有该切换的取消权——这次切换会把它自己卸载。
- 记录：见 [ADR-0016](Docs/ADR/0016-p2-persistent-app-root-and-real-scene-progress.md)。

## 5. 当前待确认

- **P1界面视觉人工验收**：十个界面的自动化测试已全部通过，但布局、字号、素材位置与动画节奏需要用户在Unity Play Mode人工确认。
- **P1云端批量部署授权**：本地实现已完成，`Docs/Deployment/p1-cloud-batched-release-checklist.md`列出了部署前需要完成的步骤。部署本身需要用户单独授权。
- **真实Host端到端冒烟**：需要用户启动Host与MySQL并授权后才能执行。
- **抽奖红色卡背素材**：暂用玄夜卡背，等待正式素材。
- **角色模型**：英雄界面目前只有`HeroModelAnchor`，正式模型到位后再接入。大厅英雄立绘与P2战斗角色是两件事。
- **P2.1人工验收**：自动化已通过（EditMode 363通过/1跳过、PlayMode 15/15），
  第一轮人工查看已提出移动模型与镜头问题并按ADR-0017修复，需要再看一轮。
  待确认项：长离模型表现、18个动画观感与循环接缝、镜头手感与指针锁定、
  相机相对移动的转向角速度（当前720°/秒）与`Run_Turnback`角度阈值（当前135°）、
  Shift点按边界、连招与蓄力节奏、灰盒摆位、两次切图之间加载界面的连续性。
- **转向角速度与反向阈值**：720°/秒与135°是初始手感值，不是测量结果，需要实际试过再定。
- **P2.1提交范围**：当前未提交内容包含P2.1新增代码与场景、用户2026-09-10手工调整的大厅UXML/USS、
  用户的角色资源与Blender导入工具三类，应分别审查后再决定提交范围。
