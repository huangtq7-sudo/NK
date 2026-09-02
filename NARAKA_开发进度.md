# 《NARAKA》开发进度与续聊入口

版本：1.7
更新日期：2026-09-02
当前阶段：P0工程基础与Windows Server 2016云端开发环境闭环均已完成；下一候选项为正式登录界面View升级，尚未开始实现

## 1. 当前状态

- 游戏玩法已经形成v2 GDD，并补充了跨对话Markdown基线。
- 技术架构已经形成v2 TDD，顶层架构固定为模块化MVC。
- Unity版本冲突已经纠正：正式基线锁定Unity 2021.3 LTS + URP，不采用Unity 6作为首发基础。
- 断线规则冲突已经纠正：遵循用户明确要求，断网或闪退直接幂等结算，不采用5分钟战斗恢复窗口。
- 英雄、技能、武器、10类怪物、任务六章、物品、经济、宠物、天气和多人移动规则已经具备首版数值基线。
- 地图美术和正式空间布局暂不设计，等待用户提供地图素材。
- 正式Unity工程位于`E:\NK项目\NK`，版本锁定为`2021.3.45f2c1`；P0客户端和服务端骨架已经建立。
- 当前已完成MySQL/SqlSugar、Argon2id账号业务、真实LegacyNetworkV1 Socket、Unity Account模块化MVC、MessagePipe/R3实现、登录前`ConfigVersion`检查、真实客户端登录冒烟、空大厅和客户端/服务端基础CI实现；P0最终本机验收、服务端GitHub工作流和Unity自托管冷启动工作流均已通过，测试Artifact正常上传，P0已经正式关闭。阿里云Windows Server 2016 Host与同机MySQL开发环境也已部署并通过手动SSH隧道的客户端闭环验收；P1与正式UI实现均尚未开始。

## 2. 已有成果

### 设计文件

- `outputs/naraka_design_v2/NARAKA_GDD_游戏设计文档_v2.0.docx`
- `outputs/naraka_design_v2/NARAKA_TDD_技术设计文档_MVC_v2.0.docx`
- `outputs/naraka_design_v2/NARAKA_开发任务与模块依赖_v2.0.xlsx`
- `NARAKA_完整玩法设计.md`
- `NARAKA_技术架构.md`
- `NARAKA_开发规范.md`
- `NARAKA_开发进度.md`
- `NARAKA_待确认问题.md`

### UI与背景资源

- UI资源根目录：`E:\新素材\NARAKA_UI_Full_v1`。
- 登录/大厅稳定环境动画与加载图：`E:\新素材\NARAKA_UI_Full_v1\Backgrounds_v4_StableEnvironment`。
- 登录动画只变化雾气和光影；大厅动画只变化雾气、光影及布料遮罩区域；主体和镜头保持固定。
- v4视频参数：1920×1080、30FPS、30秒、H.264 High、CRF14。
- UI图片已通过用户初步验收，但尚未导入Unity、切片、设置Sprite元数据或绑定界面Prefab。

### 怪物资源

- 用户提供的来源描述：E盘素材文件夹中的`30 Unity Asset Polygonal - Creatures Pack v1.0`。
- 已完成10类怪物的玩法映射和首发数值基线。
- 尚未在Unity工程中核对实际Prefab、骨骼、Animator、材质、碰撞体和动画Clip名称。

## 3. 路线图状态

### P0 基础：已完成

已完成：

- 以`E:\NK项目`作为单仓库根目录，建立Git忽略规则、属性规则和无密钥环境变量模板。
- 在正式Unity工程中锁定URP 12.1.15、VContainer 1.18.0、UniTask 2.5.11、MessagePipe 1.8.2和R3 1.3.1；关键第三方包以项目内嵌包或固定DLL保存，避免Git网络不可用时无法还原。
- 建立`Game.Core.Domain`、`Game.Core.Application`、`Game.Infrastructure.Network`、`Game.Boot`和Editor/Test程序集边界。
- 建立MVC接口、`INetworkFacade`、`MockNetworkFacade`和`LegacyNetworkAdapter`边界；MessagePipe/R3实现位于Infrastructure/Application接缝后，未改变既有业务接口或网络传输层。
- 建立VContainer Composition Root、URP自动配置工具并写入启动场景。
- 建立.NET 10 LTS模块化单体服务端Solution、健康端点、模块清单、LegacyNetworkV1边界和架构测试。
- Unity批处理配置返回码0；从不含`Library`的隔离检出执行独立导入/编译预热后，Unity EditMode回归14项通过、0失败、1项真实联网测试按环境条件默认跳过，PlayMode启动场景测试1/1通过；单独启用Unity→Host→MySQL真实注册登录冒烟后EditMode 15/15通过，并确认删除1行随机测试账号。
- 服务端Release测试构建通过，架构测试4/4、Application测试7/7、LegacyNetworkV1测试21/21、Infrastructure测试10/10通过，共42项、0失败；NuGet直接和传递依赖漏洞审计为0。
- 已建立GitHub Actions基础CI：服务端使用GitHub托管Windows Runner执行固定.NET 10.0.400 SDK、NuGet漏洞门禁、Release构建和四个测试程序集；Unity客户端使用安装并激活`2021.3.45f2c1`的受信任Windows自托管Runner执行EditMode和PlayMode，并拒绝在外部Fork PR上运行。
- 已建立`Tools/CI/Invoke-ServerTests.ps1`和`Tools/CI/Invoke-UnityTests.ps1`作为本机与CI统一入口；Unity入口先以独立进程完成首次导入/编译，检查退出码和C#编译日志，再以单独进程运行测试，避免干净`Library`首次导入时测试发现为0。本机实跑结果为服务端42/42、Unity EditMode 14通过/0失败/1按环境跳过、PlayMode 1/1。基础CI不读取`.env`、不连接MySQL，也不启用真实登录冒烟。
- 已配置仓库级Git提交者身份与GitHub `origin`，P0可验证基线已推送至远程`main`分支。
- 已完成旧客户端与SimpleServer只读审计，保存源文件SHA-256清单、线格式说明和5条旧可执行文件生成的Golden向量。
- 已建立.NET 10字节兼容的旧AES与组帧/拆帧实现，确认响应协议目录漂移、心跳间隔冲突和公网会话安全阻断项。
- 已接入环境变量连接串、MySqlConnector真实健康探针和Infrastructure内部SqlSugar工厂；依赖漏洞门禁保持启用。
- 已为本机MySQL 5.7.26建立非破坏性P0身份迁移、账号Repository和无密钥迁移工具；目标开发库为`NK`、用户为`NK`。
- 已安全执行`0001_p0_identity.sql`，实际核对3张P0表和迁移版本；Host存活检查为200，数据库探针返回`MySQL reachable`。
- 已实现Argon2id注册登录、连接级认证会话、客户端`accountId`防伪守卫和三个响应协议的客户端兼容别名。
- 已在本机`NK`库完成注册、正确密码登录、错误密码拒绝冒烟，并自动删除测试账号。
- 已将protobuf DTO显式白名单、每连接随机会话密钥、粘包/半包、16MiB帧上限、循环完整发送、注册、登录、断连清理和心跳接入真实`Socket.Select`分发。
- 已保持旧客户端和SimpleServer源码只读；旧客户端300秒心跳与旧服务端120秒判死的冲突在适配边界以360秒超时显式处理。
- 已用本机MySQL启动正式Host；`/health/ready`返回200、数据库状态为`MySQL reachable`，`127.0.0.1:8011`实际TCP连接成功。
- 已建立`Game.Features.Account.Model/Controller/View`和`Game.Features.Lobby.Model/Controller/View`六个独立程序集；View只读取PresentationState，Controller只通过`IAccountGateway`调用网络，旧DTO、AES、protobuf和Socket只存在Infrastructure。
- 已将启动组合根从`MockNetworkFacade`切换到真实`LegacyNetworkAdapter`，接入每连接密钥握手、白名单反序列化、16MiB帧上限、连续收发、300秒心跳、请求超时与取消。
- 已在启动场景生成1920×1080缩放基准的UI Toolkit登录界面和空大厅；这是P0功能界面，尚未导入和绑定已验收的正式UI美术资源。
- 已接入官方MessagePipe 1.8.2与R3 1.3.1：跨模块认证完成事件使用MessagePipe，Account/Lobby/Bootstrap的连续展示状态使用R3；View仍只取得现有`IReadOnlyState<T>`接口。
- 已建立Bootstrap模块化MVC、`UnityConfigVersionGateway`和服务端`GET /bootstrap/config-version`；客户端启动后先核对ClientVersion、ConfigVersion与ProtocolVersion，未通过时禁止注册和登录，并提供重试提示。
- 本机Host的版本端点已返回`p0-config-1`、客户端范围`0.1`至`0.1`及`LegacyNetworkV1`，真实HTTP响应已完成冒烟核对。
- 2026-09-01已完成P0最终本机联合验收：服务端Release构建0警告/0错误、自动化42/42；数据库迁移dry-run与幂等应用通过，核对3张P0表和唯一`0001`记录；`/health/live`与`/health/ready`均返回200，数据库为`MySQL reachable`，8011端口实际监听；版本端点四项匹配；Unity真实Socket注册登录通过且测试账号已清理；验收Host随后停止并释放5222/8011端口。
- 2026-09-01最终远端门禁通过：GitHub Server CI运行`33459456358`成功；Unity自托管运行`33461381291`在干净`Library`上完成独立预热、EditMode 14通过/1跳过/0失败和PlayMode 1/1，预热与测试步骤成功，`unity-test-results` Artifact正常上传。

待完成：

- 无。P0已关闭；开始P1前必须先取得用户许可。

退出条件：客户端能够启动、检查版本、登录并进入空大厅；服务端和MySQL完成健康检查。以上本机与远端退出条件均已满足。

### 阿里云开发环境：已完成Windows Server 2016重建与在线验收

- 新加坡Windows Server 2016 Datacenter轻量主机已运行MySQL 5.7.26和.NET 10自包含Host；未安装Visual Studio、容器、Redis、MQ或其他非必要服务。
- MySQL服务`NarakaMySQL57`、OpenSSH服务`sshd`与Host计划任务`NarakaServerHost`均已配置云端开机启动。Windows Server 2016重装后的最终重启复验由用户明确免除，不能描述为已实际执行。
- MySQL、Bootstrap HTTP和LegacyNetworkV1分别只监听`127.0.0.1:3306`、`127.0.0.1:5222`和`127.0.0.1:8011`，没有直接开放数据库或游戏端口。
- Windows防火墙仅允许公网TCP 3389和22；SSH只允许专用公钥，禁止密码与交互Shell，并把端口转发双重限制为云端loopback的5222与8011。
- 本地`NarakaCloudTunnel`已改为无触发器、无自动重试的按需任务，通过桌面快捷方式手动启动和停止。本地正式Unity工程通过该隧道完成版本预检、真实注册、真实登录与空大厅跳转；健康检查返回`MySQL reachable`，版本值保持`p0-config-1`、`0.1`至`0.1`和`LegacyNetworkV1`。
- 迁移`0001_p0_identity.sql`的dry-run、首次应用和重复应用均成功，核对3张P0表与迁移记录。
- 云端安装HeidiSQL 12.21 Portable供RDP会话中手动查看数据库，PowerShell ISE与HeidiSQL均不自动启动；用完必须关闭GUI并注销RDP以释放2 GiB主机内存。
- 当前只作为单开发者环境，不代表生产可用；阿里云侧防火墙规则、正式TLS/域名、多环境隔离、备份监控和容量方案留到发布准备阶段。
- 无敏感信息运维说明见`Docs/Deployment/aliyun-windows-development.md`，部署决策见ADR-0006。
- 已建立DeepSeek草案、Claude仓库复核和Codex集成守门的三模型流程，见`Docs/AI/three-model-collaboration.md`。

### P1 战斗垂直切片：未开始

- 顾沉岳、长剑、暮影妖狼和战斗HUD。
- 移动、镜头、体力、闪避、三段攻击、蓄力、伤害、护甲、受击和死亡。
- 先使用灰盒场景和占位资源验证完整循环。

退出条件：完整战斗循环通过自动化与性能验收。

### P2 远征闭环：未开始

- 地图一/地图二抽象测试场景。
- 固定传送门、异步加载、临时背包、死亡清理、返回大厅结算、断网闪退结算和幂等。

退出条件：正常、死亡、重复请求、断网和服务器异常测试全部通过。

### P3 内容系统：未开始

- 两名英雄、两把武器、十类怪物。
- 六章主线、支线、物品、魂玉、护甲、消耗品和锻造。

退出条件：所有内容可由配置驱动，并能在灰盒地图完整跑通。

### P4 大厅经济：未开始

- 英雄、兵器、仓库、商店、抽奖、签到、账号等级奖励、红点和宠物。
- 导入和绑定已生成UI资源。

退出条件：经济守恒、20抽保底、重复请求和UI流程测试通过。

### P5 联网展示与热更新：未开始

- 最多10人移动同步、共享时间天气。
- HybridCLR、Addressables、Luban、资源CDN和A/B回滚。

退出条件：多人移动长稳、热更新和回滚演练通过。

### P6 打磨发布：未开始

- 正式地图素材绑定、任务空间布局、音频、特效、性能、可访问性和发布。

退出条件：候选版本通过功能、性能、安全、兼容和回滚门禁。

## 4. 下一步

P0与云端开发环境闭环均已完成。下一步等待用户明确选择并许可：

1. 按路线图进入P1战斗垂直切片；或
2. 先执行正式登录界面View升级：DeepSeek按`Docs/AI/DEEPSEEK_UI_PROMPT.md`只提供第一阶段草案和逐步手工挂载说明，Claude按`Docs/AI/CLAUDE_PROJECT_PROMPT.md`读取真实仓库后复核、实现和运行验证，Codex执行架构与交付门禁。该任务只能替换P0功能登录页的表现，不得重写Account/Bootstrap业务、网络、服务端或数据库；正式大厅经济UI仍属于P4。

在取得许可前不继续开发、数据清理或云端变更。

## 5. 新对话续接提示词

在新的Codex对话中打开同一个项目目录，然后发送：

> 请先完整读取项目根目录五份`NARAKA_*.md`权威文档、`Docs/ADR/`全部已接受ADR和`Server/README.md`。以用户指定的Markdown 2.1纠错规则和权威Markdown纠错结论为最高优先级；只有本轮涉及玩法、完整架构或任务表冲突时才读取`outputs/naraka_design_v2/`中的GDD、TDD和任务表，冲突内容失效。继续开发Unity 2021.3.45f2c1 LTS + URP 12.1.15的《NARAKA》，客户端业务严格采用模块化MVC，LegacyNetworkV1保持冻结。开始前先汇总当前Git状态、阶段、已完成内容、本轮范围和冲突，不要重新设计已确认玩法。

如果只想继续某个模块，在上述文字后追加：

> 本轮只继续【模块名称】，完成代码、Unity配置、测试和进度文档更新；不要扩大到其他阶段。

Claude与DeepSeek的完整提示词分别见`Docs/AI/CLAUDE_PROJECT_PROMPT.md`和`Docs/AI/DEEPSEEK_UI_PROMPT.md`。

## 6. 每轮结束要求

- 更新本文件中的当前阶段和完成项。
- 新增或关闭`NARAKA_待确认问题.md`中的问题。
- 若修改玩法，同步更新`NARAKA_完整玩法设计.md`。
- 若修改架构，同步更新`NARAKA_技术架构.md`和对应ADR。
- 给出实际执行过的编译、测试、渲染或构建结果，不用“应该可以”代替验证。
