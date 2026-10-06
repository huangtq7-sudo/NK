# 《NARAKA》开发进度与续聊入口

版本：1.20
更新日期：2026-10-05
当前阶段：P1已关闭；P2.1已于2026-09-29通过用户人工验收；P2.2（单怪物战斗闭环、权威伤害规则、怪物AI与战斗HUD代码接口）已完成实现与自动化验收；2026-10-04按用户要求把全部玩家动画换成带头发飘动的新版本，并修掉新导出带来的四个缺陷（Root 旋转与缩放、裁剪范围被帧率腰斩、根位移轴向、垂直位移残留），蓄力的腾空已恢复；2026-10-05 完成正式暮影妖狼的资源受控导入与表现层接入，视觉验收已于 2026-10-05 通过并修掉验收中报出的"怪物推着角色移动"

## 1. 当前状态

- 游戏玩法已经形成v2 GDD，并补充了跨对话Markdown基线。
- 技术架构已经形成v2 TDD，顶层架构固定为模块化MVC。
- Unity版本冲突已经纠正：正式基线锁定Unity 2021.3 LTS + URP，不采用Unity 6作为首发基础。
- 断线规则冲突已经纠正：遵循用户明确要求，断网或闪退直接幂等结算，不采用5分钟战斗恢复窗口。
- 英雄、技能、武器、10类怪物、任务六章、物品、经济、宠物、天气和多人移动规则已经具备首版数值基线。
- 地图美术和正式空间布局暂不设计，等待用户提供地图素材。
- 正式Unity工程位于`E:\NK项目\NK`，版本锁定为`2021.3.45f2c1`；P0客户端和服务端骨架已经建立。
- 当前已完成P0基础以及P1大厅与账号长期系统。P1包含账号快照、三种货币、英雄/兵器选择、仓库、商店、锻造、抽奖、签到、账号等级奖励、成就、红点、好友和一对一聊天；服务端权威、幂等、持久化、配置驱动与模块化MVC边界均已落实。`p1-lobby-systems-002`已部署到阿里云Windows Server 2016开发环境，MySQL迁移`0001`至`0009`、27张表、12项能力声明、Bootstrap门禁和生成配置版本均已验收；本地通过手动SSH隧道完成真实客户端闭环。P2.1已通过人工验收，P2.2已完成实现与自动化验收；P2整体仍需战斗HUD手工视觉验收、Profiler性能证据与独立播放器构建门禁后才能关闭。

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
- 2026-09-07建立本地注释标签`p0-complete`，固定指向P0关闭提交`82c02c7debec9d399bd88df0c77c98cce491945d`（`docs: close p0 foundation`）。标签推送曾因GitHub 443连接失败而未完成；恢复网络后只需推送该标签，不把当前未提交的大厅/UI工作区混入P0归档。

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

### 正式登录界面View升级：已完成实现；接入正式背景后待重新验证

已完成：

- 新增`NK/Assets/Game/Features/Account/View/UI/AccountLogin.uxml`与`AccountLogin.uss`，登录界面改为UXML/USS描述，只使用Unity 2021.3支持的USS属性，选择器统一以`account-`前缀限定，不影响同一UIDocument下的`ConfigVersionOverlay`与`LobbyPanel`。
- `AccountView`改为通过序列化`VisualTreeAsset`实例化界面并按稳定元素名查询：`AccountScreen`、`AccountPanel`、`UsernameField`、`PasswordField`、`RegisterButton`、`LoginButton`、`AccountStatusLabel`；保留VContainer注入、`AccountController.RegisterAsync/LoginAsync`调用、View生命周期CancellationToken、按钮回调解除与订阅释放。
- 保留全部既有可观察行为：忙碌时账号/密码/两个按钮同时禁用、状态标签显示`state.Message`、`state.Username`回填、认证后隐藏登录界面并清空密码。认证后同时隐藏`AccountScreen`，避免全屏背景遮挡`LobbyPanel`。
- `ConfigVersionView`增加确定性层级处理：覆盖层可见时`BringToFront`，并在本帧所有`Start`完成后再确认一次，使覆盖层不再依赖组件添加顺序。`ConfigVersionController`与`IStartupReadiness`未改动，版本预检仍是唯一的注册/登录闸门。
- `P0ProjectSetup`增加可重复Editor装配：自动把`AccountLogin.uxml`写入`AccountView`的`loginLayout`字段，并把`ConfigVersionView`放到组件顺序最后。启动场景仍只有一个UIDocument和一个`GameLifetimeScope`。
- PlayMode测试从1项扩展到3项，新增登录界面稳定元素名/密码遮挡断言与覆盖层层级断言。

实测结果：

- Unity批处理导入退出码0，无C#编译错误；EditMode与PlayMode日志无Console异常。
- EditMode 15项：14通过、0失败、1项真实联网冒烟按环境条件跳过。PlayMode 3项：3通过、0失败。
- 使用临时夹具在1920×1080与1600×900两种面板尺寸下实测布局：两者逻辑坐标空间同为1920×1080，无越界、无垂直重叠，注册与登录按钮无水平重叠。该夹具为一次性验证工具，未提交仓库。
- 注意：上述几何实测针对的是接入正式背景之前的右侧卡片布局（`x 1280–1740`、`y 260–821`）。卡片改为居中偏下后尚未重新测量。

### 正式登录背景素材接入：已落盘并通过自动化，人工视觉待验收

已完成：

- `login_first_frame_NARAKA_1920x1080.png`已复制为`NK/Assets/Game/Art/UI/Backgrounds/AccountLoginBackground.png`（1920×1080、24bpp、2.9 MB），来源为`E:\新素材\NARAKA_UI_Full_v1\Backgrounds_v4_StableEnvironment`。
- 新增`NK/Assets/Game/EditorTools/UiBackgroundTextureImporter.cs`：对`Assets/Game/Art/UI/Backgrounds/`下的贴图强制统一导入设置（Default、sRGB、Alpha None、无Mipmap、Clamp、Bilinear、Max Size 2048、CompressedHQ、压缩质量Best），并提供菜单`NARAKA/Setup/Reimport UI Backgrounds`强制重新导入。整屏背景按1:1显示，天空与云雾的大面积渐变需要高质量压缩以避免色带。
- `AccountLogin.uss`的`.account-background`改为引用该底图并使用`-unity-background-scale-mode: scale-and-crop`，保留深色`background-color`作为加载失败兜底；占位分层与占位说明文本已删除。
- 背景顶部已烘焙NARAKA大标题，因此卡片内的标题Label已移除，“登录游戏”升为卡片主标题；卡片按左右对称构图改为居中偏下，不再使用右侧压暗层。
- 30秒循环背景视频`login_fog_light_only_loop_30s_1080p.mp4`（19.3 MB）按决定不入库；`.gitignore`已排除该目录下的`*.mp4`及其`*.meta`，避免提交出没有实际文件的引用。
- `NK/UIElementsSchema/`为`Assets > Update UIElements Schema`生成物，已加入`.gitignore`。

验证与待完成：

- `AccountLoginBackground.png.meta`现已落实无Mipmap、Default平台高质量压缩、Clamp与Bilinear设置；2026-09-07统一Unity验证完成首次导入/编译预热且无C#编译错误，EditMode 35通过/1跳过/0失败、PlayMode 3/3通过。
- 居中卡片的两分辨率几何仍未重新测量，自动化通过不能替代视觉验收。
- 人工Play Mode视觉验收（背景观感、配色、中文字形渲染、悬停/禁用反馈、覆盖层遮挡）尚未执行。
- 动态视频背景是否接入尚未决定。`Background.FromRenderTexture`在Unity 2021.3.45的`UnityEngine.UIElementsModule.dll`中确实存在，因此UI Toolkit可直接使用`VideoPlayer + RenderTexture`，不必按素材说明退回uGUI的`RawImage`。

### 大厅主界面（DeepSeek草案修复）：已修复并通过自动化，人工视觉待验收

来源：由DeepSeek生成草案后放入工作区，本轮按仓库实际内容逐项复核并修复。大厅入口已按2026-09-07确认的新路线图纳入P1；“开始游戏”到地图的正式场景与战斗内容仍分别在P3远征和P2战斗阶段实现。当前草案只是界面与导航外壳，不能视为P1业务完成。

已修复的框架破坏：

- `LobbyPresentationState`曾被移到无asmdef的`Features/Lobby/Presentation/`，落入`Assembly-CSharp`；已移回`Features/Lobby/Controller/`，恢复为实现`IPresentationState`的`readonly struct`。
- `Infrastructure/Scene/`缺少asmdef，同样落入`Assembly-CSharp`；已新增`Game.Infrastructure.Scene.asmdef`并加入`Game.Boot`引用。
- `LobbyController`曾引用`UnityEngine.Debug`，与其asmdef的`noEngineReferences: true`冲突；已移除全部引擎依赖，提示文案改用`switch`表达式，与`AccountController`保持一致，并恢复`IController`标记。
- `GameLifetimeScope`曾向容器注册裸`string`，会命中任何类型的`string`构造参数；已改为只对`LobbyController`使用`WithParameter("mapSceneName", ...)`。
- `LobbyView`曾使用无监管`async void`；已改为`UniTaskVoid` + `Forget()`，与`AccountView`一致。
- `GameLifetimeScope.cs`、`LobbyController.cs`、`UnityLobbySceneGateway.cs`、`LobbyMain.uss`、`LobbyControllerTests.cs`五个文件为GBK编码，Unity按UTF-8读取会产生乱码；已全部转为UTF-8。

已修复的编译与运行缺陷：

- 缺失`using System.Threading`、`Naraka.Core.Application.MVC`、`Naraka.Core.Application.Presentation`、`Naraka.Infrastructure.Scene`。
- 使用了不存在的`ReactiveState<T>.CurrentValue`（实际API为`Current`）。
- `Game.Features.Lobby.Controller`与`Game.Features.Lobby.View`两个asmdef缺少`UniTask`引用。
- `GameLifetimeScope`重复注册`LobbyModel`与`LobbyController`各两次。
- `LobbyView.OnDestroy`用新建lambda解除按钮订阅，实际未解除；已改为保存委托实例。
- `RequestFeature`连续两次基于陈旧快照`Set`，第一次写入被覆盖。
- UXML元素名与View查询名不一致（`GachaButton`对`DrawButton`；三个货币Label重名为`amount`）；已统一为稳定元素名并由脚本核对。
- 按钮同时带图标与`text="Button"`；已改为纯图标加`tooltip`。
- USS使用了Unity 2021.3不支持的`radial-gradient`、`@media`、`background-size`以及USS中的`picking-mode`；已全部移除，改用`-unity-background-scale-mode`，分辨率适配交给PanelSettings的Scale With Screen Size。
- USS定义的class大多未挂到UXML元素上；已改为UXML挂class、USS集中定义。
- `AccountControllerTests`中既有的`new LobbyController(new LobbyModel())`因构造函数变更而无法编译；已更新为注入伪造网关。
- `BootScenePlayModeTests`断言的`LobbyPanel`已更名为`LobbyScreen`；已同步。
- `LobbyControllerTests`使用`UnityEngine.Time`，与测试asmdef的`noEngineReferences: true`冲突；已重写为`UniTask.ToCoroutine`形式，覆盖进入大厅、功能提示、重复请求、加载失败与取消五种情况。
- `LobbyView.lobbyLayout`在场景中未赋值；`P0ProjectSetup`已扩展为同时装配`loginLayout`与`lobbyLayout`。

验证与待完成：

- 2026-09-07在当前工作区执行统一Unity脚本：独立导入/编译预热退出码0且无C#编译错误，EditMode共36项、35通过、1项真实联网测试按环境跳过、0失败，PlayMode 3/3通过。
- `Map1`场景不存在，"开始游戏"会走到`ILobbySceneGateway`的Build Settings检查并显示"加载地图失败，请重试。"，按钮恢复可用。这是保留的兜底行为，不是缺陷修复目标。
- 大厅正式静态底图未确定：当前`.lobby-background`暂用`async_loading_background_1920x1080.png`，正式大厅底图与30秒循环视频均未入库。
- 货币数值与账号等级为UXML中的静态占位`--`，没有接入任何服务端数据；真实业务现已列入P1.1。

### 大厅外观面板、游戏指针与视频背景：已实现，部分已由编辑器日志验证

按用户明确需求实现（四个决定点已确认：外观选择不持久化、视频不入库、硬件指针、Editor按前缀扫描目录）：

- 新增`LobbyAppearanceView`、`LobbyAppearance.uxml`/`.uss`与`LobbyAppearanceCatalog`（ScriptableObject，两个View共用一份贴图引用）。Controller只保存`SelectedAvatarIndex`/`SelectedFrameIndex`两个下标，贴图映射留在View，业务层不接触`Texture2D`。
- 掉落回弹使用`experimental.animation` + `Easing.OutBack`驱动`translate`，未使用USS的`ease-out-back`关键字（该关键字在本工程无法离线确认），不引入DOTween。
- 左下头像改为`PlayerAvatarButton`并叠加`PlayerAvatarFrame`；关闭按钮使用`00005.PNG`。
- 新增`GameCursor`：`Cursor.SetCursor`替换硬件指针，热点`(24, 5)`由`Mouse.png`的alpha计算得出；`OnDisable`还原默认指针。`Mouse.png`由Editor工具改为`TextureImporterType.Cursor`。
- 大厅背景接入`VideoPlayer` + `RenderTexture` + `Background.FromRenderTexture`，`isLooping`开启，仅在`prepareCompleted`后才切换背景，避免首帧显示未初始化画面；视频缺失时回退到`Lobby_Animate.png`静态图。
- `P0ProjectSetup`扩展为一并创建/刷新外观目录、装配新组件与资源，并新增菜单`NARAKA/Setup/Rescan Appearance Catalog`。

已由Unity编辑器日志验证的事实：

- 全部新增与修改的C#**编译零错误**（`Editor.log`中无任何`error CS`）。
- 运行期异常只有一类：`VContainerException: LobbyAppearanceView is not in this scene`，共5次。根因是`RegisterComponentInHierarchy`要求组件已存在于场景，而`Apply P0 Project Settings`尚未执行。已把该注册改为"缺失只报错、不阻断容器构建"，外观面板属于可选界面，不应拖垮登录与大厅。
- `LobbyMain.uss`第170行`margin-left: 12`缺少单位，Unity整条丢弃该声明并给出导入警告；已修正为`12px`。已对全部UI的USS做同类扫描，无其他缺单位的长度值。
- `Lobby_Animation.mp4`导入时Unity报告`Unexpected timestamp values detected`（该视频为H.264 High Profile）。这可能影响循环接缝的平滑度，尚未实测。

验证与待完成：

- 外观相关测试已包含在2026-09-07统一Unity验证结果中；EditMode整体35通过/1跳过/0失败，PlayMode 3/3通过。
- 用户需执行一次`NARAKA/Setup/Apply P0 Project Settings`补齐`LobbyAppearanceView`与`GameCursor`组件及资源绑定。
- 视频循环卡顿、掉落回弹观感、硬件指针实际显示均未做人工验收。

### P1 大厅与账号长期系统：已完成并关闭

- P1.0：冻结需求切片，定义共享业务契约、应用消息ID、错误码、RequestId/OrderId幂等规则和数据库迁移；按ADR-0007只扩展适配器，不修改LegacyNetworkV1传输机制。
- P1.1：账号快照、头像、账号等级、铜币/幻丝/金币余额及只读大厅展示。
- P1.2：英雄、兵器和宠物的拥有状态、选择与装备。
- P1.3：仓库、堆叠物品、装备方案和容量溢出处理。
- P1.4：商店目录、限购、购买事务、货币流水和失败/重复请求恢复。
- P1.5：锻造与武器强化的材料校验、消耗事务和结果回放。
- P1.6：抽奖订单、20抽保底、奖励发放、断线结果恢复和重复请求幂等。
- P1.7：签到、补签、连续奖励、账号等级奖励、成就框架与红点Version/SeenVersion。
- P1.8：最小好友与聊天范围——好友申请、接受、拒绝、删除、屏蔽、在线状态和一对一文字聊天；不包含世界频道、群聊、语音或文件传输。
- P1.9：十个大厅入口、账号信息、货币、异常提示、重登恢复和服务端权威联合验收；开始游戏按钮只验证进入后续场景的边界，不在P1实现战斗或远征内容。

#### P1.0 共享契约与迁移：已完成并通过自动化验证

- 按ADR-0007建立应用协议登记规则：既有`LegacyProtocolCatalog`（0–18）保持不变，P1消息改由新增的`ApplicationProtocolCatalog`登记，编号从19起。`LegacyWireGoldenTests`对`Client.Count == 12`与`Server.Count == 19`的断言原样保留，这正是"0–18未被改动"的证据。
- 稳定错误码扩展为15项（`Success`至`Conflict`），服务端Application枚举与线级枚举数值一一对应。
- 新增迁移`0002_p1_account_progression.sql`：只新建`account_progression`并回填既有账号，`accounts`/`player_profiles`与迁移`0001`一律不动。

#### P1.0-Config CSV配置管线：已完成并纳入CI门禁

- 18张UTF-8 BOM的CSV源表放在`Config/Source/`，用Excel直接编辑；`Tools/Config/Naraka.ConfigCompiler`把它们编译成规范化JSON，双端只读消费。见ADR-0010。
- 校验覆盖：ID唯一、外键存在、价格与数量非负、`StackLimit > 0`、概率权重合法、品质只允许白/蓝/紫/金/红、每个奖池权重和大于0、20抽保底品质必须可兑现、武器等级连续不重复、锻造配方材料存在、签到天数1–7、账号等级奖励等级合法、展示顺序确定。
- 输出确定性：按稳定ID排序，LF换行、无BOM，附SHA-256清单与`configVersion`；相同输入必定字节相同。任何校验失败返回非0退出码并指出源文件与行号。
- CI在跑测试前先执行`--check`：生成物与源表不一致就直接失败。

#### P1.1-A 账号等级与三种货币：已完成端到端

- 新增协议：`MsgLobbyAccountSummaryRequest`（19）与`MsgLobbyAccountSummaryResponse`（20）。请求体不含AccountId，服务端通过`LegacySessionAccountResolver`从已认证连接会话取得身份。
- 三层共同阻止非法数据：数据库`UNSIGNED`列、服务端`LobbyAccountSummary.TryCreate`、客户端`LobbyAccountSnapshot.TryCreate`。
- 加载失败只写状态消息，不覆盖上一次成功的余额，也不伪造0。

#### P1.1-B 头像、头像框与初始货币发放：已完成

- 协议21/22（账号资料）与23/24（设置外观）。头像与头像框使用配置里的稳定`AvatarId`/`AvatarFrameId`，不再是列表下标。
- 初始货币按新玩法基线为铜币/幻丝/金币各1000。这与已部署的`0001`/`0002`迁移冲突（它们把余额初始化为0），解决方式是**不改动已部署迁移**：新增`account_grants`与`currency_ledger`两张表，登录时的`EnsureProvisionedAsync`在一个事务里按配置发放StarterGrant，用`(account_id, grant_key)`主键保证每个账号只发一次。发放是**累加**而不是覆盖，因此已有账号的余额不会被抹平。见ADR-0011。

#### P1.2 英雄、兵器与宠物：已完成

- 协议25/26（设置出战方案）。英雄界面（左侧英雄列表、中间模型锚点、右侧技能）与兵器界面（长剑/太刀切换、等级、熟练度、击杀数）均已实现。
- 长剑与太刀不进仓库、不在商店出售，每个账号每种兵器恰好一把，等级/强化/熟练度/击杀数各自独立并由服务端持久化，默认兵器使用稳定`WeaponId`。

#### P1.3 仓库、堆叠与装备方案：已完成

- 协议27/28（读取仓库）与29/30（仓库变更：丢弃、出售、换位、整理、装备、卸下、扩容）。
- 全部仓库物品走堆叠数量模型，魂玉与护甲也按`ItemId`堆叠、无随机词条、无独立实例。装备方案引用`ItemId`。
- 已装备的魂玉/护甲至少保留一件：出售或丢弃到会失去所有权时服务端拒绝并提示先卸下。魂玉战斗装载6格且不允许重名，护甲1格。

#### P1.4 商店与购买事务：已完成

- 协议31/32（商店目录）与33/34（购买）。数量购买弹窗，购买在**一个事务**内完成扣费、货币流水、入库与限购计数；容量不足时整笔回滚。

#### P1.5 锻造与唯一武器强化：已完成

- 协议35/36（锻造面板）与37/38（强化）。材料足够时**必定成功**，服务端没有任何随机判定；界面显示"成功率100%"与"攻击力68 → 75"式的下一级预览。
- 消耗、武器升级与货币流水在一个事务内完成；重复请求只产生一次强化结果。

#### P1.6 抽奖：已完成

- 协议39/40（奖池状态）、41/42（抽奖）与43/44（确认已展示）。品质由低到高为白→蓝→紫→金→红。
- 服务端先在一个事务里完成扣费、随机、发奖与结果固化，客户端再播动画。动画可跳过，但**不改变结果**；未确认展示的订单在重新登录后会被重新取回继续展示。客户端不生成任何随机结果。
- 十连至少一个蓝及以上；20抽保底出红；提前出红重置保底计数；保底跨登录保留。

#### P1.7 签到、账号等级奖励、成就与红点：已完成

- 协议45/46（签到状态）、47/48（签到领取）、49/50（成就与账号等级）、51/52（领取）、53/54（红点读取）、55/56（红点已读）。
- 服务器日界为UTC+8的05:00，由`ServerDay`换算成单调递增的整数日号，因此跨月跨年都不会倒退。
- 七日循环、每周期一次补签（消耗配置化的补签卡物品）、连续签到节点3/5/7。**主进度与连续次数是两个独立字段**：补签能点亮格子，但不推进连续次数。领取全部需要手动点击且幂等。
- 成就使用`AchievementXp`与`AchievementLevel`，**绝不写入`AccountXp`**；账号经验只来自任务。四个分类（冒险历程、战斗大师、锻造大师、财富积累）各自独立配置。依赖P2战斗与P3远征的成就标记为`IsActiveInP1 = false`，进度恒为0，不显示任何编造的进度。
- 红点是**独立模块**：路径前缀树 + `Version`/`SeenVersion`（不是bool），叶子变脏只让祖先跟着脏，界面只订阅`RedDotPresentationState`。业务模块通过MessagePipe发布来源事件，与红点模块之间没有任何直接引用。好友在线绿点/灰点不属于红点系统。见ADR-0012。

#### P1.8 好友与一对一聊天：已完成

- 协议57/58（社交视图）、59/60（按名搜索）、61/62（好友动作）、63/64（聊天动作）。六个好友动作与三个聊天动作共享协议对，因为它们的响应完全一样——整份社交视图。
- 流程：搜索玩家 → 发送申请 → 接受或拒绝 → 成为好友。支持删除、屏蔽、在线状态、好友申请红点、一对一文字聊天、未读私聊红点、限流（10秒10条）、512字长度上限与输入校验。
- 好友关系**成对写入**，接受申请、删除好友、屏蔽都在一个事务里完成全部相关行，因此不会出现单向好友。屏蔽会同时清除既有好友关系与两个方向的申请。
- 若对方已经向自己发过申请，再次发起申请会直接成为好友，避免两条互相等待的申请永远挂着。
- 会话按(低账号, 高账号)规范化，一对一会话只有一行；已读位置按账号分行，一方读完不会清掉另一方的未读。
- 不包含世界频道、群聊、语音、文件传输与玩家交易。

#### P1.9 十个入口联合验收：已完成本地验收

- 十个大厅入口全部接入真实模块，占位文案"将在后续模块接入"已删除——每个入口都有专属面板，面板自己负责加载、空数据与失败状态。
- 旧云端兼容机制：Bootstrap新增**可选**`serverCapabilities`字段。当版本门禁匹配但Host不返回它时，客户端退回P1.1-A兼容集合，其余入口显示"服务器功能尚未升级"且**不发送任何未知协议**。本仓库Host只声明`NarakaServerCapabilities.Implemented`（本Host真正实现的11项+社交，共12项），而不是整份登记表。
- P1开发期曾临时保持`p0-config-1`以验证上述能力兼容。完整P1集中发布已把Bootstrap门禁同步提升为`p1-config-1`：当前P1客户端会在预检阶段拒绝旧`p0-config-1`云端，部署配套P1 Host后才能登录。生成配置继续使用独立版本号与哈希。

#### P1本地实测结果（本轮实际执行，非推断）

| 项目 | 结果 |
| --- | --- |
| 服务端Release构建 | 通过，0警告0错误 |
| `Tools/CI/Invoke-ServerTests.ps1` | **total=354，executed=354，passed=354，failed=0** |
| 配置`--check`一致性门禁 | 通过（生成物与`Config/Source`一致） |
| 迁移dry-run `0001`–`0009` | 全部校验通过（4/3/5/3/2/2/4/7/7条语句） |
| Unity导入预热 | 退出码0，零C#编译错误 |
| Unity EditMode | **total=282，passed=281，failed=0，skipped=1**（跳过项为需要真实Host的`LegacyClientLiveSmokeTests`） |
| Unity PlayMode | **total=3，passed=3，failed=0**，日志零异常 |

#### P1云端集中部署与关闭验收

- 完整P1源代码提交`20e55112a4bdee2d6ae2f7c8ff35718de4964350`与迁移器验证修复提交`e9d4ca7d5a69694850f285aa4554c33f267dc814`均已推送到`origin/main`。
- 2026-09-10首次使用`p1-lobby-systems-001`部署时，迁移`0001`–`0009`均执行成功，但迁移器最终验证SQL遗漏6张社交表并把9个版本误写成8；部署脚本在移动Host目录前安全停止并恢复旧Host。该问题只涉及验收清单，未改动既有迁移和业务数据。
- 修复版`p1-lobby-systems-002`随后完成幂等迁移、27张表与9条迁移记录验证、旧Host备份和新Host原子切换。部署清单绑定提交`e9d4ca7d5a69694850f285aa4554c33f267dc814`，回滚备份位于`C:\NarakaDeploy\backups\20260910-191428-p1-lobby-systems-002-previous`。
- 云端部署后只有1个Host进程；`live`与`ready`均通过，数据库返回`MySQL reachable`，Bootstrap返回`p1-config-1`、12项能力和`LegacyNetworkV1`，生成配置返回`p1-config-5e52cf730692`。3306、5222与8011继续只监听loopback。
- 低内存门禁通过：切换前可用内存515 MiB，切换后453 MiB；部署期间未启动HeidiSQL或PowerShell ISE，未在云端编译。
- 本地手动SSH隧道复验5222、8011、数据库、版本门禁、12项能力和生成配置全部通过。2026-09-21用户完成Unity真实客户端检查并明确确认没有问题。
- 用户后续手工调整的大厅UXML/USS，以及角色预览、Shader和Blender导入工具仍是当前未提交工作区内容；P1关闭提交不修改、不移动也不混入这些文件，进入P2前另行审查。

退出条件：所有大厅按钮均接入真实服务端权威数据；经济守恒、20抽保底、认证、权限、幂等、重登恢复、好友/聊天最小流程和UI业务绑定通过自动化与用户验收。**P1退出条件已经满足，阶段正式关闭。**

### P2 战斗垂直切片：第一阶段已完成实现与自动化验收，人工验收待执行

原计划：顾沉岳、长剑、暮影妖狼和战斗HUD；移动、镜头、体力、闪避、三段攻击、蓄力、伤害、护甲、受击和死亡。

#### P2.1 任务场景、灰盒战斗、玩家HFSM、移动与第三人称镜头：已完成实现

范围调整（见 [ADR-0016](Docs/ADR/0016-p2-persistent-app-root-and-real-scene-progress.md)）：
原属P3的两图灰盒、固定传送门、异步加载与死亡返回一并纳入本阶段，
因为移动、镜头与状态机不在真实场景流转下验收就暴露不出"相机重新绑定""传送门只触发一次""死亡只返回一次"这三类缺陷。
角色使用长离（`Assets/Game/Art/Characters/Changli/Changli_TPose.fbx`，Generic Rig）作为P2灰盒角色，
顾沉岳与裴行舟的正式模型、长剑/太刀与暮影妖狼仍未到位。

第二轮人工验收修正（2026-09-28 第二轮反馈，四个现象）：

1. **移动、攻击、技能之后角色都回退一小步**。根因是动画在根骨骼上烘焙了水平位移
   （`Move_F` +12.28、`Attack04_1` +17.84、`Attack01` +3.17 等），动作结束交叉淡入回 Idle
   时骨架被插值拉回原点。本该用 Root Motion 提取处理，但模型与动画都是 Generic 且
   `avatarSetup = NoAvatar`，没有 Avatar 就没有 Root Motion 节点，设 `motionNodeName` 无效
   （已实测并回退）。改由新增的 `RootMotionCanceller` 在 `LateUpdate` 把根骨骼水平位置
   锁回静止值，垂直与旋转保留。**这是动画数据问题，不是状态机问题。**
2. **待机动作只播一小段**。`AM_Stand1_Action03_SEQ1` 实测 15.067 秒，配置写的是 3 秒，
   被截断到 20%。
3. **出场动画没播完**。`Burst02` 实测 5.5 秒、`Burst01` 实测 7.233 秒，配置都写的 2 秒，
   分别被截断到 36% 与 28%。
4. **加载进度一开始就接近满**。进度取了"真实进度"与"时间进度"的较大值，
   灰盒场景的 `AsyncOperation.progress` 瞬间到 0.9，于是进度条一开机顶到 99% 干等两秒。
   改为取较小值并做单调处理。

2 和 3 是同一个系统性问题：**所有动作时长都是估计值，没有从实际 AnimationClip 读取**。
几乎每一条都错（见 [ADR-0015](Docs/ADR/0015-p2-player-hfsm-and-animator-projection.md) 的对照表）。
修法是让 `Rebuild Player Animator` 从片段同步时长与动作位移，命中窗按比例缩放，
并新增 EditMode 契约测试守住"配置时长必须等于片段长度"。

顺带修掉一个由真实时长暴露出来的体力缺陷：冲刺**进行中**体力照常恢复
（规则是"结束 0.75 秒后开始恢复"）。冲刺 0.5 秒时看不出来，
实测 1.333 秒之后一次冲刺自己回来 6.7 点，两次冲刺耗尽体力的设计失效。

同时把停止动作的减速曲线从线性改为指数衰减（时间常数 = 动画烘焙距离 / 入口速度）：
停止动画实测 1.3–1.5 秒，从 5 米每秒线性减速会走出 3.7 米，而动画本身只前移 1.4 米。

移动模型修正（见 [ADR-0017](Docs/ADR/0017-camera-relative-locomotion.md)）：
2026-09-28用户人工查看后否掉了原先的"坦克式"控制（W前进/S后退/A、D只转向），
改为**相机相对移动**：WASD四个方向都移动、方向相对摄像机、角色转向后只播前进动画、
取消后退与原地转身。`Walk_B`/`Run_B`不再被引用，其`.meta`已恢复默认设置，
因此本阶段改动过的动画`.meta`从5个减为3个。Animator从20个State减为18个。
同时补齐三件在实际操作中才暴露的问题：镜头首次绑定时从角色背后起步、
虚拟相机随目标启停（大厅不抢相机）、战斗场景锁定并隐藏鼠标指针
（指针自由时一旦移出画面镜头会莫名停转）。

玩法基线修正（见 [ADR-0013](Docs/ADR/0013-p2-player-action-baseline.md)）：
体力上限60→**20**、闪避消耗20→`Move_F`消耗**10**、死亡后体力恢复至**20**；
`Move_F`取消无敌帧；普通攻击统一鼠标左键，右键纵击推迟；蓄力只实现2秒满档，0.6/1.2秒作为预留配置位。
保留每秒恢复5、动作后0.75秒开始恢复、受击后额外暂停0.5秒。

新增依赖（见 [ADR-0014](Docs/ADR/0014-p2-input-system-and-cinemachine.md)）：
`com.unity.inputsystem` **1.7.0** 与 `com.unity.cinemachine` **2.10.1**，
版本依据是Unity 2021.3.45f2c1编辑器自己的包清单；两者均已解包存在于本机npm缓存，断网可还原。
`activeInputHandler`由0改为**2（Both）**，因为两个第三方演示脚本仍使用`Input.GetAxis`。
Addressables本阶段明确暂缓，场景继续走`ISceneLoader`抽象后的`SceneManager.LoadSceneAsync`，
该抽象即后续迁移边界。

新增程序集（10个）：
`Game.Features.Character.Model/Controller/View`、`Game.Features.Combat.Model/Controller/View`、
`Game.Features.World.Controller/View`、`Game.Infrastructure.Input`、`Game.Infrastructure.Camera`。
`Game.Infrastructure.Scene`为扩展而非新建。Model与Controller层全部`noEngineReferences: true`。

状态机（见 [ADR-0015](Docs/ADR/0015-p2-player-hfsm-and-animator-projection.md)）：
自研轻量HFSM，不引入UnityHFSM。三层独立、集中仲裁，优先级为
Death → HitStun → 强制出场状态 → Action → Locomotion。
Animator Controller为20个State、0个Parameter、0条Transition，业务层从不读取Animator状态。
对动画`.meta`的唯一改动是给5个循环片段打开`loopTime`：
`AM_Stand1_Action03`、`Walk_F`、`Walk_B`、`Run_F`、`Run_B`；其余15个动画的`.meta`未改动。

组合根：新增持久化`AppRootLifetimeScope`承载时钟、场景加载、加载界面、输入、相机、玩家状态与命中结算；
`GameLifetimeScope`成为其子Scope并随Bootstrap场景卸载；地图场景各有一个`WorldSceneLifetimeScope`。
`LoadingView`从`P0ClientShell`迁移到持久根的独立`UIDocument`，`LoadingScreen.uxml`/`.uss`未改动。

加载进度：`LoadingController`接入真实`AsyncOperation`，Unity的0–0.9归一化为0–1，
资源未就绪时封顶99%，最短显示时长2秒与真实时长取较大者，失败时收回界面并给出明确文案。

#### P2.1本地实测结果（本轮实际执行，非推断）

| 项目 | 结果 |
| --- | --- |
| Unity包解析 | Input System 1.7.0 与 Cinemachine 2.10.1 锁定到`packages-lock.json`，精确版本、非浮动 |
| Unity导入预热 | 退出码0，零C#编译错误 |
| Unity EditMode | **total=364，passed=363，failed=0，skipped=1**（跳过项仍为需要真实Host的`LegacyClientLiveSmokeTests`） |
| Unity PlayMode | **total=15，passed=15，failed=0** |
| `NARAKA/Setup/Apply P2 Scene Setup` | 连续执行两次，场景文件字节相同，无新增对象与`.meta`改动（幂等） |
| 动画曲线路径校验 | 20个动画的曲线路径全部匹配正式角色模型 |
| 第三方源场景 | Aquarius与Pure Nature源场景未被打开或修改 |
| 用户未提交的大厅UXML/USS | 11个文件均保持2026-09-10的用户版本，未被覆盖 |
| LegacyNetworkV1/服务端/数据库/云端 | 零改动，未提交、未推送、未部署 |

EditMode新增覆盖：WASD四个方向都移动且共用前进动画、目标朝向随镜头旋转、
转向受配置角速度限制并走最短路径、无移动输入时不转向、四方向同速、斜向输入归一化、
小角度转向不触发反向动作、
体力20/消耗10/不足拒绝/钳制、0.75秒恢复延迟、受击额外0.5秒暂停、
Shift点按与长按边界、长按无移动输入不原地跑、冲刺期间不自动转Run、Run→Walk不播停止动画、
奔跑反向（角度阈值135°+时间窗0.25秒）、Idle 5秒待机动作、摄像机输入不重置待机计时、移动/攻击重置计时、
三段连招顺序与回绕、0.18秒输入缓存、0.8秒连招重置、2秒蓄力与短按互斥、蓄力霸体不免伤、
F/V冷却、Death优先级、HitStun不打断Death、SuperArmor免硬直不免伤、重生基线、
同一攻击不重复命中同一目标、阵营过滤、护甲溢出、加载失败可恢复、重复切换被拒绝。

PlayMode新增覆盖：Bootstrap仍可启动且只有一个持久化组合根、重复进入Bootstrap不累积组合根、
加载界面在持久根上且切场景不丢失不重复、真实加载进度推进、地图一可加载、
玩家在正确出生点生成并播放Burst02、出场结束恢复输入、WASD驱动CharacterController、
四个方向键都既转向又移动且朝向相对镜头、镜头转开后按W角色朝镜头新方向走、
相机始终以角色为中心且鼠标不旋转角色、
传送门只触发一次并加载地图二、Burst01播放后恢复输入、
死亡后只触发一次返回地图一并按基线重生、切换后无重复玩家/相机/组合根。

待完成：

- 用户在Unity编辑器内的人工功能验收（见本节"人工验收待执行项"）。
- 战斗HUD正式视觉、正式怪物AI、长剑/太刀、暮影妖狼。
- Q-017（R3依赖阻断Windows独立播放器构建）未修复，因此人工验收只能在编辑器Play Mode内进行。

#### P2.2 单怪物战斗闭环、正式伤害规则、怪物AI与战斗HUD代码接口：已完成实现

P2.1 已于 2026-09-29 通过用户人工验收：移动、镜头、动画、输入、场景切换与操作手感
均视为已验收基线，本阶段没有改动其中任何一项参数。

##### 新增程序集（6 个）

| 程序集 | 依赖方向 | 说明 |
| --- | --- | --- |
| `Game.Features.AI.Model` | 无引用 | 泛型行为树，`noEngineReferences` |
| `Game.Features.Monster.Model` | → Core.Domain、AI.Model、Combat.Model | 怪物规则与 HFSM，`noEngineReferences` |
| `Game.Features.Monster.Controller` | → Core.Application、Monster.Model、Combat.*、Generated.Config | 编排与配置转换，`noEngineReferences` |
| `Game.Features.Monster.View` | → Monster.Controller、Combat.View、VContainer | 灰盒狼表现与场景适配 |
| `Game.Features.CombatHud.Controller` | → Character.Controller、Monster.Controller、Combat.Controller | HUD 只读状态，`noEngineReferences` |
| `Game.Features.CombatHud.View` | → CombatHud.Controller、TextMeshPro、VContainer | HUD 绑定脚本 |

依赖方向保持 `View → Controller → Model/Domain Interface ← Infrastructure`，
四个 Model/Controller 程序集全部 `noEngineReferences: true`。

##### 怪物 AI 结构

行为树只产出意图（Dormant / Patrol / Perceive / Chase / NormalAttack / SelectSkill /
Recover / Dead），怪物 HFSM 是「当前动作」的唯一真相
（Idle / Move / Attack / Skill / HitStun / Knockdown / Death）。
动作层不空闲时行为树的结果不生效，因此正在挥出去的一下必须打完。
决策频率 6Hz（配置校验强制落在 5–10Hz），休眠时拉长到 1 秒。
地面寻路使用 Unity 2021.3 自带 NavMesh，**没有引入任何第三方 AI 包**，
也没有新增 `com.unity.ai.navigation`；没有导航数据时退回直线推进。
详见 [ADR-0018](Docs/ADR/0018-monster-behavior-tree-and-hfsm.md)。

##### 伤害与反击规则

`DamageFormula` 是唯一实现，玩家与怪物共用：
`RawDamage = FinalAttack × SkillMultiplier`，
`AfterDefense = RawDamage × 100 / (100 + Defense)`，
随后先扣护甲、溢出扣生命。**防御在受击方一侧扣**。
判定顺序固定为：无敌（处决）→ 重生保护 → 反击 → 扣防御 → 扣护甲 → 扣生命；
霸体只免普通硬直。反击 0.2 秒判定窗、0 体力、0 冷却、失败 0.5 秒后摇、
只接受金色可反击技能；成功后玩家获得 2 秒霸体、目标进入 1.5 秒处决窗口；
处决由普通攻击触发、全程无敌、伤害 `FinalAttack × 1.5`。
详见 [ADR-0019](Docs/ADR/0019-authoritative-damage-counter-execute.md)。

##### P2 灰盒数值表（全部标记 `P2Graybox`，**不是已确认的平衡值**）

暮影妖狼（`Config/Source/monsters.csv`）：

| 项 | 值 | 项 | 值 |
| --- | --- | --- | --- |
| 生命 | 600 | 护甲 | 100 |
| 防御 | 20 | 攻击力 | 60 |
| 巡逻速度 | 2.4 | 追击速度 | 6.0 |
| 巡逻半径 | 6.0 | 巡逻停顿 | 2.0 秒 |
| 感知半径 | 14.0 | 脱离半径 | 22.0 |
| 攻击距离 | 3.2 | 休眠距离 | 40.0 |
| 决策频率 | 6 次/秒 | 阶段阈值 | 50% 生命 |
| 普攻倍率 | 1.0 | 普攻冷却 | 2.0 秒 |
| 普攻前摇/命中/后摇 | 0.45 / 0.25 / 0.6 秒 | 受击硬直 | 0.6 秒 |
| 死亡时长 | 2.0 秒 | 颜色标签 | 无（不可反击） |

赤瘴吐息（`Config/Source/monster_skills.csv`）：

| 项 | 值 | 项 | 值 |
| --- | --- | --- | --- |
| 颜色标签 | Red | 可反击 | 否 |
| 伤害倍率 | 1.8 | 冷却 | 12.0 秒 |
| 射程 | 0–9.0 | 锥角 | 60° |
| 阶段门槛 | 生命 ≤ 50% | 预警 | 0.8 秒 |
| 前摇/命中/后摇 | 0.35 / 0.3 / 0.9 秒 | 最近使用抑制 | 6.0 秒 |

玩家侧新增/改动的灰盒值：

| 项 | 值 | 说明 |
| --- | --- | --- |
| 最终攻击力 | 220 | 英雄 100 + 长剑 1 级 120，**合成规则是假设**，见 Q-022 |
| 防御 | 80 | 与顾沉岳一致 |
| 三段普攻倍率 | 1.0 / 1.2 / 1.6 | 取代原来的绝对伤害 100/120/160 |
| 蓄力倍率 | 3.0 | 取代原来的 300 |
| F / V 倍率 | 1.2 / 2.5 | 与 `hero_skills.csv` 的震岳斩、破军镇狱对齐 |
| 处决倍率 | 1.5 | 玩法文档 §7 |
| 处决动作时长 | 1.0 秒 | **占位值**，没有权威来源，见 Q-023 |
| 反击窗 / 失败后摇 / 霸体 / 处决窗 | 0.2 / 0.5 / 2.0 / 1.5 秒 | 玩法文档 §7 |

单位说明：距离与速度都用 Unity 世界单位，与玩家的走 5.0 / 跑 7.5 / 冲刺 12.276
同一套刻度（见 Q-021，模型比例问题未解决）。

##### 配置管线

- 新增两张 UTF-8 BOM 源表 `Config/Source/monsters.csv`、`monster_skills.csv`。
- `Shared/Config/NarakaConfigModels.cs` 新增 `MonsterConfig`、`MonsterSkillConfig`
  与三组常量（颜色标签、分类、平衡状态），`SchemaVersion` 由 `1.0.0` 提升为 `1.1.0`。
- 编译器新增校验：感知 ≤ 脱离 < 休眠；攻击距离 ≤ 感知；决策频率落在 5–10；
  阶段阈值落在 (0,1)；红色不得可反击、金色必须可反击、普通攻击不进技能表；
  预警时长必须 > 0。
- **修正既有冲突**：`heroes.csv` 两名英雄的 `Stamina` 由 60 改为 **20**
  （D-013 / ADR-0013 已确认的体力上限），并新增编译期校验与 EditMode 契约测试，
  60 这个值不可能再回到战斗模型里。
- 生成配置 `ConfigVersion` 由 `p1-config-5e52cf730692` 变为 `p1-config-45539ed9c3ac`。
  它与 Bootstrap 登录门禁 `p1-config-1` 是两个独立版本号，登录不受影响。

##### 怪物素材包审计结果（只读，未导入）

`E:\素材\30 Unity Asset Polygonal - Creatures Pack v1.0\...unitypackage`（38.1 MB）
按 gzip tar 解出 524 个条目的路径清单，**没有导入工程、没有复制任何文件进仓库**。
包内 10 个生物目录与玩法文档的十类怪物一一对应。暮影妖狼对应
`Assets/Polygonal Creatures Pack/Polygonal Wolf/`：

- FBX：`Polygonal Wolf.FBX`（本体）、`Base.FBX`，以及 18 个动画 FBX：
  `@Idle`、`@Walk Forward W/WO Root`、`@Walk Backward W/WO Root`、
  `@Run Forward W/WO Root`、`@Jump W/WO Root`、`@Bite Attack`、
  **`@Breath Attack`**、`@Pound Attack W/WO Root`、`@Take Damage`、`@Die`、
  `@Howl`、`@Eating`、`@Resting`、`@Look Around`。
- Prefab：`Polygonal Wolf Black / Brown / White`。
- 材质：同名三套 + `Demo Ground`。贴图：三套 Base + 三套 Glow，共 6 张 png。
- Animator：`Polygonal Wolf.controller`、`Demo Polygonal Wolf.controller`、`Rotate.controller`。
- 场景：`Demo Scene.unity`、`Turntable Scene.unity`（演示用，不导入）。
- 发行方 Meshtint Studio；包内没有 LICENSE 文件，**授权范围待用户确认**。
- `@Breath Attack` 正好对应「红色锥形吐息」，因此玩法映射与素材是对得上的。

**本轮没有导入这个包**，场上是明确命名的 `GrayboxWolf` 方块替身。

##### 场景装配

新增幂等工具 `NARAKA/Setup/Apply P2.2 Combat Setup`，只改 `Map02_CombatGraybox`：

- 生成 `Assets/Game/Settings/Monster/GrayboxWolf.prefab`（方块本体 + 命中盒 + 预警面片
  + NavMeshAgent + CapsuleCollider）与三份灰盒材质；
- 新增 `GrayboxWolfSpawner`（上限 1、不补充）、`CounterTrainingTarget_DevOnly`、
  `NavigationArea`；把 `GrayboxGround` 标为 Navigation Static 并在没有导航数据时烘焙一次；
- 不碰地图一、不碰玩家 Prefab、不碰相机与出生点，不打开任何第三方源场景。

`CounterTrainingTarget_DevOnly` 是**开发测试对象**：暮影妖狼在设计上只有普通攻击和
红色吐息，两者都不可反击，因此金色反击路径只能靠这个独立靶子验证，
绝不给狼编一个不存在的金色技能。

##### 战斗 HUD 代码接口

`CombatHudPresentationState` 覆盖玩家生命/护甲/体力、F/V 冷却、动作拒绝原因、
目标名称/生命/护甲、怪物技能预警类型、死亡状态；`ICombatHudController` 另外提供
`PlayerDamaged` / `MonsterDamaged` 两个受击反馈事件。
`CombatHudView` 只做绑定，每个 Inspector 字段都是可选的。
HUD 的视觉与挂载由用户手工完成，步骤见 `Docs/UI/p22-combat-hud-manual-setup.md`。

##### P2.2 本地实测结果（本轮实际执行，非推断）

| 项目 | 结果 |
| --- | --- |
| 配置编译器 `--check` 门禁 | **通过**：`ConfigVersion=p1-config-45539ed9c3ac`，`SchemaVersion=1.1.0`（仓库自带 .NET 10.0.400 SDK 实跑） |
| `Naraka.ConfigCompiler.Tests` | **20/20 通过** |
| `Tools/CI/Invoke-ServerTests.ps1` | **total=354，executed=354，passed=354，failed=0**（含 LegacyNetworkV1 golden 56/56，证明冻结传输层未被触碰） |
| Unity 导入预热 | 退出码 0，零 C# 编译错误 |
| `NARAKA/Setup/Apply P2 Scene Setup` | 成功；重建 `PlayerTuning.asset`，Animator 18 State / 0 Parameter / 0 Transition；片段长度与动画一致 |
| `NARAKA/Setup/Apply P2.2 Combat Setup` | 成功；生成灰盒狼、生成点、训练靶、导航区域并烘焙 NavMesh |
| Unity EditMode | **total=463，passed=461，failed=1，skipped=1** |
| Unity PlayMode | **未能执行**（见下） |
| 脱战修正的离线复验 | 纯 Model 层不引用 UnityEngine，用 .NET 10 SDK 直接驱动 `MonsterCore` 逐条核对：追击→脱战→持续回家→到家恢复巡逻→回家途中重新交战→无目标巡逻→远距离休眠并降频，**9 项全部符合预期** |
| Model 层类型检查 | `Core.Domain` + `AI.Model` + `Combat.Model` + `Character.Model` + `Monster.Model` 用 .NET SDK 以 C# 9 / netstandard2.1 编译，**0 警告 0 错误** |

前三项与后两项是在 Unity 许可证失效之后、用仓库自带的 .NET 10 SDK 跑出来的，
因此它们覆盖了 14:33 那次 EditMode 运行之后才做的改动。

唯一失败项是 `MonsterBehaviorTests.ItDisengagesWhenThePlayerRunsFarEnoughAway`：
脱战意图只维持一个决策周期就回到巡逻。已修正为「一直往回走直到回到出生点附近」，
并补了到家后恢复巡逻的断言。这次修正**没有**经过 Unity Test Runner 复跑，
但已经用上表的离线驱动逐条核对过行为，并通过了 Model 层类型检查。

**只改了一个服务端文件**：`Server/src/Naraka.Server.Application/Config/GameConfig.cs`
里的 `RequiredSchemaVersion` 由 `"1.0.0"` 改为 `"1.1.0"`。
这是被本轮配置扩展强制的：`NarakaConfigModels.cs` 新增了两张表，按既定规则必须提升
`SchemaVersion`，而服务端这个常量与编译器的 `SchemaVersion` 必须一致
（该常量自己的注释就写着「由测试断言，因此结构升级时不可能只改一边」）。
不改它会让服务端拒绝加载生成配置，354 项服务端测试里有 200 多项直接失败。
除这一行之外，服务端、数据库、协议与云端部署零改动。

**阻塞**：2026-09-29 14:36（UTC）起本机 Unity 许可证失效
（`No valid Unity Editor license found`，`C:\ProgramData\Unity\Unity_lic.ulf` 已不存在），
此后所有 `Unity.exe -batchmode` 一律退出码 1。因此：
脱战修正后的 EditMode 复跑、全部 PlayMode 测试、装配工具幂等复核与
Q-017 的独立播放器构建都**没有执行**。这是环境问题，与本轮代码无关：
同一份代码在 14:33 的运行里完成了 463 项 EditMode 测试。处理办法见 Q-025。

XML 与日志路径：`artifacts/ci/unity/editmode-results.xml`、`editmode.log`、
`warmup.log`、`setup.log`（`playmode-results.xml` 是 P2.1 时期的旧结果，不代表本轮）。

##### 本轮新增测试清单

EditMode（`Tests/EditMode/`）：

- `DamageFormulaTests`（9 项）：原始伤害、防御为 0 与非 0、负防御钳制、护甲吸收与溢出、
  玩家与怪物同公式、重生保护免伤、霸体不免伤、死亡拒绝伤害。
- `MonsterBehaviorTests`（20 项）：灰盒值标记、无目标时巡逻、远距离休眠、休眠降频、
  感知→追击、进入攻击距离出手、普攻不可反击、半血以上不得吐息、半血解锁红色吐息、
  预警先于命中窗、技能选择受距离/冷却/抑制约束、脱战并走回出生点、
  已交战且超过休眠距离时优先回家、行为树不能打断进行中的动作、霸体只免硬直、无霸体进硬直、玩家能杀死狼、
  死亡拒绝一切、处决窗口会过期、死亡不能进处决窗口、行为树按频率决策、选择节点短路。
- `CounterExecuteTests`（17 项）：反击基线数值、Space 起手且不耗体力、0.2 秒窗口、
  0.5 秒失败后摇、无冷却、只接受金色、普攻不可反击、红色不可反击、窗口外不生效、
  成功后 2 秒霸体、成功只上报一次、处决替换普攻、无目标时仍是连招、
  处决全程无敌、处决 1.5 倍伤害、倍率与 hero_skills 对齐、死亡不能反击、反击本身没有命中窗。
- `CombatHitTests` 扩充 5 项：护甲吃满伤害仍算命中、被反击不产生命中事实、
  命中事件带阵营、普攻与红色技能永远不可标记为可反击。

EditModeUnity（`Tests/EditModeUnity/`）：

- `GameConfigCatalogTests` 新增 4 项：英雄体力必须为 20、狼是灰盒怪且距离关系自洽、
  怪物技能引用完整且颜色与可反击一致、吐息是阶段门控的红色锥形技能。
- `P22SetupIdempotencyTests`（3 项）：连续两次装配后 Prefab 与场景逐字节相同、
  场景里每类装配对象各只有一个、狼 Prefab 是 Graybox 且带命中盒与预警。

PlayMode（`Tests/PlayMode/P22CombatPlayModeTests.cs`，9 项，**本轮未能执行**）：
只生成一只狼、数值来自生成配置、巡逻→感知→追击→攻击、玩家离开后脱战、
玩家能伤害并杀死狼、狼能扣玩家护甲与生命、红色吐息只在半血后出现且预警先于伤害、
HUD 状态跟随玩家与目标、重新进入地图二不产生重复对象。

是否存在联网条件跳过项：有，1 项 —— `LegacyClientLiveSmokeTests` 需要真实 Host，
与 P2.1 相同，按环境条件默认跳过。

##### 明确未做的事

- 不实现掉落、任务推进、经验、货币或服务端持久化（P3/P4）。
- 不实现 10 类怪物、第二名英雄、长剑/太刀可视模型与滚轮切换。
- 鼠标右键纵击仍未实现：没有经过确认的动画，不复用其他动画，继续记为阻塞项。
- 没有修改 LegacyNetworkV1、数据库、协议或云端部署；服务端只动了上面那一行版本常量。
- Q-017（R3 依赖阻断 Windows 独立播放器构建）已完成定位但**未修复**：
  `NK/Assets/Plugins/R3/` 里有 R3.dll 与三个依赖 DLL，唯独缺
  `System.Runtime.CompilerServices.Unsafe.dll`，而 R3.dll 的程序集引用表里确实有它
  （已逐字节确认）。编辑器导入与 EditMode/PlayMode 正常是因为 Editor 的 Mono BCL 自带这个程序集，
  独立播放器构建才需要工程里真的有这份托管 DLL。
  补齐文件就在仓库里：`.tools/vendor-r3-1.3.1/src/R3.Unity/Assets/Packages/
  System.Runtime.CompilerServices.Unsafe.6.0.0/lib/netstandard2.0/System.Runtime.CompilerServices.Unsafe.dll`
  （18,024 字节，SHA-256 `01748200f2400c742aa689f1f5101bd6298efdfd92c00c18f4fa473847235ba9`）。
  现有三个依赖 DLL 已核对与该 vendor 目录**逐字节相同**，因此来源无歧义。
  **本轮没有把它复制进去**：修复必须能用独立播放器构建验证，而许可证失效导致无法构建；
  并且按要求 Q-017 要作为独立提交，不与怪物 AI 大改混在一起。
- 本轮未提交、未推送、未部署云端。

#### 动画换版：带头发飘动的新动作集（2026-10-04）

用户重做了全部动作（旧动画没有头发自然飘动），源文件在
`E:\素材\新版本AS\艾斯3d建模-鸣潮 长离-标准版\新动作`（28 个动画 + 1 个标准 T 姿势）。
规则按用户给定："有新版就换，没有新版则保留旧版；新版没有 `AM` 前缀，自动识别对应关系"。

##### 替换结果

- **替换 28 个**，保留工程内旧文件名、只换文件内容。
  带前缀的 5 组对应关系：`Stand1_Action03`→`AM_Stand1_Action03`（Idle）、
  `Stand1_Action03_SEQ1`→`AM_Stand1_Action03_SEQ1`（待机动作）、
  `Summon`→`AM_Summon`（第二段普攻）、`Death`→`AM_Death`、
  `AirAttack01`–`05`→`AM_AirAttack01`–`05`；其余 19 个同名。
- **保留旧版 6 个**（新版没有对应文件）：`AM_Skill01`、`AM_QTE`、
  `AM_Stand1_Action01_SEQ1`、`AM_Stand1_Action02_SEQ1`、
  `Manipulate_Release_F`、`Manipulate_Release_F_02`。
  其中 `AM_Skill01` 是 18 个动画状态里唯一还在用旧动画的，因此 **F 技能的头发不会飘**。
- 保留旧文件名是刻意的：Animator Controller、`.meta` 里的 `loopTime` 与
  `PlayerTuning.asset` 全部按 GUID/路径引用资产，换内容不换名字等于零引用风险、
  零 GUID 变动，`.meta` 一个字都没改（3 个循环片段的 `loopTime` 原样保留）。

##### 骨骼与材质：模型不用换

新动作文件夹里的 `·长离_标准T姿势.fbx` 临时导入做了逐路径比对，结论是不需要换模型：

- 骨骼层级与正式模型完全一致（`Root/Bip001/...`，含 57 个 `Bone_Hair*`/`Bangs` 节点），
  新动画确实驱动头发骨骼；
- 差别只在网格节点：正式模型 1 个合并的 `R2T1ChangLiMd10011_LOD0`，
  标准版 8 个拆开的 `mesh_0`–`mesh_7` 外加一个淘宝水印节点；
- **新动画的骨骼曲线路径 100% 匹配正式模型**，材质仍是 `Changli/Materials` 下的
  8 个 `MI_*`（`.meta` 的 `externalObjects` 重映射未改，也没有生成任何新材质资产）。

因此保留已验收的正式模型（少 7 次绘制调用、无水印节点），临时导入的标准版已删除。

##### 修掉一个真缺陷：根位移通道换了轴

这是本轮唯一的实际缺陷，只能靠实测发现。把片段采样到模型上读**世界**位移：

| 片段 | 根节点世界位移 | 骨盆水平位移 |
| --- | --- | --- |
| `Move_F`（冲刺） | (0, **−4.833**, 0) | 0.230 |
| `Attack04_1`（V 技能） | (0, **−7.022**, 0) | 0.016 |
| `Attack10`（蓄力） | (0, **−3.335**, 0.024) | 0.122 |
| `Run_Turnback` | (0, **+4.561**, 0) | 0.046 |
| `Stop_Walk_R` | (0, **−0.566**, 0) | 0.030 |

旧导出把整体位移放在 `Root` 的水平轴（冲刺 +12.276 等），新导出放在**垂直轴**，
数值恰好是旧值除以 2.54（英寸/厘米换算比）。新动画本身是原地动作，
所以那条通道是误导出的垃圾数据。而 `RootMotionCanceller` 原本刻意"抵消水平、保留垂直"，
于是一条都抵消不掉 —— 角色每做一个动作就会沉下去或飞起来几个单位。

**修法**：`RootMotionCanceller` 改为锁住根节点的整条位移通道（三个轴）。
垂直姿态不会丢，因为它在 `Bip001` 及以下（实测 `Attack10` 骨盆相对根节点零垂直位移，
旧 `AM_Skill01` 骨盆相对根节点上升 1.043，那部分仍然保留）。
`preserveVertical` 保留旧行为备用，默认关闭，并有契约测试钉住它必须关闭。

##### 片段长度与窗口

28 个新片段里只有两条长度变了，其余逐帧一致：

| 字段 | 旧 | 新 | 处理 |
| --- | --- | --- | --- |
| `idleVariationDurationSeconds` | 15.067 | **8.000** | 直接同步 |
| `combo2.clipSeconds`（`Summon`） | 1.833 | **2.667** | 同步并把窗口按 **×1.4548** 等比缩放 |

命中窗、连段窗、后摇起点与位移窗都是"片段里的第几秒"，换一条更长的动画之后
原来的秒数指向另一个动作阶段甚至落到片段之外。按比例缩放保留设计好的相对节奏，
这恢复了 ADR-0015 正文原本的做法。`PlayerTuning.CreateBaseline()` 与
`PlayerTuningAsset` 默认值已同步，期望值/兜底值/资产三者一致。

动作位移（冲刺 12.276、V 技能 17.838）与停止距离（1.439/1.417）从此是**纯设计值**：
新片段水平位移≈0，已经没有可测量的来源，工具不再推导它们
—— 继续"同步"只会写成 0 并毁掉停止动作的指数衰减。

##### 导出残留：570 条曲线被忽略

新动画里有一批曲线指向模型上不存在的节点：26 个片段各 8 条指向
`mesh_0`–`mesh_7`（或 `0000_mesh_0` 这种带序号变体）；
`Stand1_Action03` 与 `_SEQ1` 另有一整份带 `W0_` 前缀的**重复骨架**，
两者曲线路径数因此是 491 条而不是 274 条。Unity 会忽略不存在的路径，不影响表现。

`ValidateClipPaths` 原来只报总数，换完动画后会变成 570 条噪音。改为按路径首段判断：
骨骼全在 `Root/` 之下，首段不是 `Root` 的记为导出残留并只报数量。
现在的输出是"18 个动画的骨骼曲线路径全部匹配正式角色模型（另有 570 条导出残留节点路径被忽略）"。

##### 本轮实测结果（实际执行，非推断）

| 项目 | 结果 |
| --- | --- |
| Unity 导入预热 | 退出码 0，零 C# 编译错误 |
| `NARAKA/Setup/Rebuild Player Animator` | 成功；同步 7 项（待机动作时长、第二段片段长度与 5 个窗口），Animator 18 State/0 Parameter/0 Transition |
| 骨骼曲线校验 | **18 个动画的骨骼曲线路径全部匹配正式角色模型**，570 条导出残留路径被忽略 |
| `NARAKA/Setup/Apply P2 Scene Setup` | 成功；片段长度已一致、未再改动 |
| Unity EditMode | **total=465，passed=464，failed=0，skipped=1** |
| Unity PlayMode | **total=27，passed=27，failed=0，skipped=0** |
| 装配工具幂等 | 两个工具各连续执行两次，7 个产物逐字节相同（Bootstrap/地图一/地图二场景、玩家 Prefab、PlayerTuning、Animator、GrayboxWolf Prefab）；NavMesh 检测到已有数据后跳过烘焙 |
| 动画 `.meta` | **0 个文件改动**，3 个循环片段的 `loopTime` 原样保留 |

新增/改写的契约测试：
`BakedRootMotionIsLargeEnoughToMatter` 改为测量根节点三个轴的位移（原来只看水平 Z，
换成原地动画后必然失败）；新增
`TheBakedRootChannelIsVerticalSoCancellingOnlyHorizontalWouldMissIt`（把"为什么要锁三个轴"
钉进测试）与 `ThePlayerPrefabCancelsTheWholeRootTranslationChannel`（断言 `preserveVertical` 关闭）。

##### 顺带修掉的 P2.2 场景问题

PlayMode 第一次在带怪物的地图二上跑完，暴露出一个布置问题：
`GrayboxWolfSpawner` 原先放在出生点正前方 14 单位，而狼的感知半径正好是 14，
于是玩家一落地就被发现，并在 Burst01 出场动画（输入锁定约 4.8 秒）期间被打进硬直
—— `PortalTriggersOnceAndLoadsMap02WithItsOwnBurst` 断言出场结束后输入应当解锁，因此失败。

这是场景布置问题，不是测试要放宽（"不得删除或放宽已有测试"）。
生成点移到 30 单位外：感知 14、休眠 40，狼因此在原地巡逻，玩家必须自己走近才会被发现。
出场流程不再被战斗打断，这也更符合设计意图。`P22CombatSetup` 的默认位置同步改为 30。

遗留的三处导出问题（不影响可玩性，建议重新导出）见
[Q-026](NARAKA_待确认问题.md)：F 技能缺新动画、根位移轴向与单位、待机动作里的重复骨架。

#### 动画换版的后续：用户实测报告的两个缺陷（2026-10-04）

换版完成后用户在 Unity 里实际播放，报告：

> idle 状态、run 状态、idle 五秒后的状态、v 技能状态，冲刺闪避状态，
> 第二段攻击和第三段攻击状态，受击状态都是贴在地上的，方向轴不对，
> 并且走路、idle 会出现卡顿的效果。

这是**两个独立缺陷**，都来自新导出的设置变化，都与上一轮已处理的根位移无关。
定位过程全部靠测量：先读 FBX 的节点属性与全局设置，再把片段采样到正式模型上量真实姿态，
最后用从 Git LFS 还原的旧片段做同一套导入设置下的对照。

##### 缺陷一：`Root` 节点上多了 90° 旋转和 1/2.54 缩放 → 躺平、缩小

| `Root` 节点 | 旧动画 / 正式模型 | 新动画 |
| --- | --- | --- |
| `Lcl Rotation` | 缺省（0） | **(+90, 0, 0)** |
| `Lcl Scaling` | 缺省（1） | **0.3937 = 1/2.54** |
| `PreRotation` | (−90, 0, 0) | (−90, 0, 0)（相同） |
| `Bip001` 以下的身体动画 | — | 与旧版几乎逐位相同 |

正式模型的 `Root` 静止姿态是 `Rx(−90)`（来自每个 FBX 都有的 `PreRotation`），
新片段把它驱动到 0，于是整具骨架绕世界 X 轴转 +90°：躺在地上、头朝前，同时缩到 39.37%。

实测 15 个新片段的 Root 偏差恰好是 +90°，例外是 `Walk_F`（+6.87°）与 `Attack01`（−3.14°）
—— 正是用户唯一没有点名"贴在地上"的两个；而唯一还在用旧动画的 F 技能偏差是 0°，
作为对照组成立。症状与测量完全对应。

**修法**：新增 `PlayerAnimationRootFixup`（`AssetPostprocessor`），在导入期按
"该文件的 Root 静止姿态 → 正式模型的 Root 静止姿态"重定基。补偿量不是写死的 90°，
而是算出来的，因此 6 个旧片段算出单位四元数、一个字节都不改（日志里它们不出现），
`Run_Turnback` 的 180° 转身也被完整保留（修正后偏差是 `(0,0,−180)`，X 轴归零）。
左乘常量四元数是线性映射，值与切线用同一个乘法变换，曲线形状逐帧保留。

为什么不在运行期修：`RootMotionCanceller` 分不清当前播的是新片段还是旧片段，
补偿量加给旧片段就会把旧片段转错 90°，而交叉淡入期间补偿量根本没有定义。

##### 缺陷二：FBX 时间模式 30fps → 60fps，把按帧号写死的裁剪范围腰斩 → 卡顿

工程里只有三个片段带显式裁剪范围，正好是三个循环动画，范围按**帧号**存：

| 片段 | `.meta` 范围 | 旧（30fps） | 新（60fps） | 首末帧最大骨骼差 |
| --- | --- | --- | --- | --- |
| `Walk_F` | 0–38 | 1.267s＝整段 | 0.633s＝前一半 | 0.00° → **42.80°** |
| `Run_F` | 0–22 | 0.733s＝整段 | 0.367s＝前一半 | — → **69.13°** |
| `AM_Stand1_Action03` | 0–80 | 2.667s＝整段 | 1.333s＝前一半 | 0.17° → **52.26°** |

走路只播半个步幅就硬接回起点（片段内正常单帧步进只有 2.9°，相差 15 倍），这就是卡顿。

**修法**：`ApplyImportSettings` 不再沿用 `.meta` 里的帧号，每次从
`importer.defaultClipAnimations` 重取完整 Take 范围，只覆盖 `firstFrame`/`lastFrame`/
`loopTime`，其余导入标记保留。`.meta` 的差异因此只有每个文件一行 `lastFrame`。

修完三个片段的时长回到 **1.267 / 0.733 / 2.667 秒**，与 2026-09-29 验收的基线完全一致。
上一轮报告里写的"`Walk_F` 0.633 秒、`Run_F` 0.367 秒"是这个缺陷的症状、不是设计长度，
据此提出的"脚打滑"担心同样不成立。

##### 修复后的实测

| 项目 | 修复前 | 修复后 |
| --- | --- | --- |
| 18 个片段的 Root 旋转偏差 | 15 个 +90° | **全部 0.00°** |
| 18 个片段的 Root 缩放 | 新片段全是 0.3937 | **全部 1.0000** |
| 角色姿态（Root 局部 +Z 与世界 +Y 的夹角） | 最大 90° | **全部 < 5°** |
| `Walk_F` 循环首末帧差 | 42.80° | **0.29°** |
| `Run_F` 循环首末帧差 | 69.13° | **0.00°** |
| `AM_Stand1_Action03` 循环首末帧差 | 52.26° | 41.93°（见下，导出端问题） |
| 片段时长（走/跑/idle） | 0.633 / 0.367 / 1.333 | **1.267 / 0.733 / 2.667**（＝验收基线） |

##### 遗留：新 idle 本身不是一个循环动作

裁剪范围修好之后 idle 还剩 41.93°，这一半不是引擎问题：

- 发梢在 1.0 秒处偏离首帧 **55.75°**，到末尾只回落到 41.93°，从不回到起点；
- 身体（裙摆）也差 **12.34°**；
- 逐帧扫过全部 141 个候选结束点，最接近的也有 **30.87°** —— 裁到任何更短的范围都救不回来；
- Unity 的 Loop Pose（`loopPose`）对 Generic + `NoAvatar` 的片段实测**完全无效**
  （开与不开都是 41.93°）；
- 对照：旧 idle 在同样 2.667 秒下只有 **0.17°**。

即新 `Stand1_Action03` 烘焙的头发模拟是"从静止开始逐渐摆动"的一次性动作，
必须重新导出。登记为 [Q-026](NARAKA_待确认问题.md) 第 1 项，并由
`PlayerAnimationContractTests.KnownBrokenLoops` 记录当前值防止变坏
—— 重新导出闭合之后测试会要求把登记项删掉，门槛自动收回 5°。

##### 本轮新增的测试与工具

| 新增 | 作用 |
| --- | --- |
| `TheOfficialModelRootRestMatchesTheConventionTheImportFixupAssumes` | 把导入期修正依赖的"正确静止姿态"钉在模型上，模型重新导出会先报警 |
| `EveryAnimationKeepsTheSkeletonUprightAndFullSize` | 18 个片段各取 9 个采样点，断言角色竖直且不缩放 —— 直接对应"贴在地上" |
| `LoopingAnimationsActuallyLoopSeamlessly` | 循环片段首末帧姿态差，直接对应"卡顿" |
| `LoopingClipRangesCoverTheWholeTake` | 裁剪范围必须等于完整 Take，防止再把帧号写死 |
| `NARAKA/Diag/P2.2 Animation Axis Diag` | 逐片段量 Root 曲线与真实姿态（就是找到缺陷一的工具） |
| `NARAKA/Diag/P2.2 Idle Loop Scan` | 分开统计头发与身体的循环缝，并扫描可用的循环点 |

| 验收项 | 结果 |
| --- | --- |
| Unity 导入预热 | 退出码 0，零 C# 编译错误 |
| `NARAKA/Setup/Rebuild Player Animator` | 成功；3 个 `.meta` 的 `lastFrame` 更新，28 个新片段的 Root 被重定基，6 个旧片段未改动 |
| Unity EditMode | **total=469，passed=468，failed=0，skipped=1** |
| Unity PlayMode | **total=27，passed=27，failed=0，skipped=0** |
| 动画 `.meta` 改动 | 3 个文件各 1 行（`lastFrame`），其余 31 个未改动 |
| 装配工具幂等 | 三个工具（动画、P2 场景、P2.2 战斗）各再跑一遍，10 个产物**逐字节相同** |
| 受保护路径 | 0 改动（Aquarius / PureNature / CharliShader / 大厅 UI） |


#### 蓄力的腾空与待机动作延迟（2026-10-04，用户第二轮实测后）

用户继续验收，提出两件事：

> 可以将 idle 状态 5 秒后未执行动作后播放的 Stand1_Action03_SEQ1，
> 转换为 2.7 秒后未执行则播放吗，这样就不会有 idle 突变的突兀感觉了。
> 还有一个问题是长按后执行的蓄力动画没有飞上天空，而是在原地旋转。

##### 蓄力：腾空确实在片段里，被两件事一起吃掉

逐帧读 `Attack10` 的根节点位移，`Root.z` 那一列是 `0 → 2.058（t=1.3s）→ 0.024`，
一条去而复返的弧线。按同一个补偿量还原成正确约定：

| 时刻 | 还原后的高度 | 还原后的前进 |
| --- | --- | --- |
| 0.0s | 0 | 0 |
| **1.3s** | **+5.227**（角色身高 4.04，跳得比自己还高） | 7.38 |
| 2.2s | +0.062（落回原高度） | 8.470 |

两个原因：

1. 上一轮只还原了 `Root` 的旋转与缩放，**没有还原位移** ——
   于是"向上"被读成"向前"、"向前"被读成"向下"（原始采样下角色的脚一路沉到 −3.33）；
2. `RootMotionCanceller` 当时锁了三个轴，连垂直分量一起抹掉。

**修法**：`PlayerAnimationRootFixup` 用同源补偿把位移通道一起还原，
`RootMotionCanceller` 的 `preserveVertical` 回到默认打开（这本来就是 ADR-0015 的原始分工：
抵消器只抵消代码已经驱动的水平那一份，垂直留给动画）。

还原的正确性是可验证的 —— 实测水平行程与配置值分毫不差：

| 动作 | 实测水平行程 | 配置值 |
| --- | --- | --- |
| 冲刺 `Move_F` | **12.276** | 12.276 |
| V 技能 `Attack04_1` | **17.837** | 17.838 |
| 停止走路 `Stop_Walk_R` | **1.439** | 1.439 |
| 停止奔跑 `Stop_Run_R` | **1.417** | 1.417 |

这同时推翻了上一轮的一个结论：位移值并没有"失去可测量的来源"，
来源一直在，只是被轴向错位藏住了。ADR-0015 对应小节已加指路。

##### 顺带抓出一条：第一段普攻的垂直位移残留

打开"保留垂直"之前必须确认一条不变量：游戏里没有跳跃、角色恒定贴地，
所以根节点的垂直**净变化只可能是 0**。量完 18 个状态，有一个违反：

| 状态 | 垂直净变化 | 垂直最高 | 判定 |
| --- | --- | --- | --- |
| `Attack10`（蓄力） | +0.062 | **+5.227** | 腾空弧线，落回原高度 |
| `Attack01`（第一段普攻） | **−3.167** | −0.043 | **导出残留，单调下沉** |
| 其余 16 个 | 0.000 | 0.000 | 无垂直位移 |

`Attack01` 的文件静止姿态也是唯一异常的（`(-86.86, 180, 180)` 而不是 0）。
留着它就等于第一段普攻把角色按进地里 3 个单位。导入期按不变量压平到首帧值，
压平之后它的根位移变成"水平 0.174、垂直 0"，恰好符合玩法文档
"三段普通攻击、蓄力与 F 技能不改变角色坐标"。

##### 命中判定不受影响

判定盒 `Hitbox` 是玩家根节点的**直接子物体**，不挂在骨骼上，
`MeleeHitbox` 用 `transform.TransformPoint(localOffset)` 取查询中心。
所以骨架升空不改变命中位置，战斗行为与已验收基线一致。

##### 待机动作延迟 5 → 2.4 秒

用户的思路成立：循环缝在 2.667 秒，在播到边界之前切进待机动作就看不到那一跳。
但 2.7 秒差一点点 —— 切入要走 0.1 秒交叉淡入，淡入期间 Idle 还在推进，
所以约束是「延迟 + 0.1 < 2.667」，即**延迟 < 2.567**；2.7 秒落在跳变之后 0.033 秒。
取 **2.4 秒**，留 0.167 秒余量。

副作用：待机循环从 13 秒（5 + 8）变成 10.4 秒（2.4 + 8），
待机动作占站立时间从 62% 升到 77%。这是观感取舍，由人工验收判断。
这是遮盖不是修复，片段本身仍不闭环；重新导出之后延迟可以调回 5 秒。

##### 本轮新增/改写的测试

| 测试 | 作用 |
| --- | --- |
| `TheBakedRootDisplacementMatchesTheConfiguredDisplacement` | 实测水平行程必须等于配置位移 —— 轴向还原正确性的最强证据 |
| `EveryClipsRootVerticalChannelReturnsToWhereItStarted` | 18 个状态的根节点垂直净变化必须≈0（抓出 `Attack01` 的那条） |
| `TheChargeKeepsItsVerticalLeapAndThePrefabLetsItThrough` | 蓄力腾空峰值 > 4、落回原点，且 Prefab 必须保留垂直 |
| `IdleVariationStartsBeforeTheIdleClipWouldLoopWhileThatLoopIsBroken` | 延迟 + 淡入 < Idle 片段长度；循环修好后自动让路 |
| `IdleVariationPlaysAfterTheConfiguredDelay` | 原 `...AfterFiveSeconds`，改为从配置推导 |
| 另外 3 条 idle 计时测试 | 写死的 4.5/5.1 秒在延迟缩短后会变成空测，改为从配置推导 |

被推翻并改写的两条：`TheBakedRootChannelIsVerticalSoCancellingOnlyHorizontalWouldMissIt`
与 `ThePlayerPrefabCancelsTheWholeRootTranslationChannel` —— 它们断言的
"根通道挂在垂直轴上"是轴向错位的**症状**，不是契约，错位修好后前提就不存在了。

| 验收项 | 结果 |
| --- | --- |
| Unity 导入预热 | 退出码 0，零 C# 编译错误 |
| Unity EditMode | **total=473，passed=472，failed=0，skipped=1** |
| Unity PlayMode | **total=27，passed=27，failed=0，skipped=0** |
| 装配工具幂等 | 三个工具各再跑一遍，10 个产物**逐字节相同** |
| 动画 `.meta` 改动 | 仍是 3 个文件各 1 行（`lastFrame`），本轮没有新增 |


#### 正式暮影妖狼表现层接入（2026-10-05）

此前场上是明确命名的 `GrayboxWolf` 方块替身（ADR-0018 刻意如此，避免让人误以为美术已接入）。
用户 2026-10-05 确认素材包可用于本项目并授权导入暮影妖狼所需资源，本轮完成接入。

##### 受控导入：12 个资产，不是整包

| 项 | 值 |
| --- | --- |
| 素材包 | `Unity Asset Polygonal - Creatures Pack v1.0.unitypackage` |
| 字节数 / SHA-256 | 38,110,997 / `70b6b6a423bac0080227f816c19c94b3133eb90b90b8b860528c7284a0412f18` |
| 包内条目 | 524（10 个生物目录） |
| 暮影妖狼目录真实条数 | 50（路径与用户清单一致） |
| **实际导入** | **12 个资产 + 4 个目录 meta** |
| GUID | 原样保留；全工程 2,184 个 GUID **零重复** |

导入由 `Tools/import_polygonal_wolf.py` 执行，不是手工勾选：白名单写在代码里，
运行时先校验包的字节数与 SHA-256，对不上直接拒绝导入。`.unitypackage` 本身不进仓库。

导入的 12 个：`Polygonal Wolf.FBX`、`Base.FBX`、7 个动画
（`@Idle`、`@Walk Forward WO Root`、`@Run Forward WO Root`、`@Bite Attack`、
`@Breath Attack`、`@Take Damage`、`@Die`）、`Polygonal Wolf Black.mat`、
`Polygonal Wolf Black.png`、`Polygonal Wolf Black Glow.png`。

**与用户清单的两处差异**（都已排除）：包内实际是 **21 个 FBX**
（本体 + `Base` + **19** 个动画，不是 18）；另有清单未提到的
`PPP/Post Processing.asset`、`FBX/Rotate.anim`、`Materials/Demo Ground.mat`
与两个说明文本。

**连 Black 的第三方 Prefab 也没有导入**：实测它除 FBX 之外只依赖内建 `Standard`
材质（URP 下粉色，必须换）与 `Animators/Polygonal Wolf.controller`（演示控制器，已排除），
导入它只会留下一个必然缺失的引用；而它能提供的（53 节点骨架 + 单个
`SkinnedMeshRenderer` + Avatar）直接实例化 FBX 就有，它自己也没有任何碰撞体。

##### 导入核查（`NARAKA/Diag/Duskshadow Wolf Asset Audit`，实测）

| 项 | 结果 |
| --- | --- |
| `animationType` / `avatarSetup` | Generic / `CreateFromThisModel` |
| `rootMotionBoneName` | 空 —— 没有 Root Motion 节点 |
| Transform / 蒙皮骨骼 / Renderer | 53 / 37 / 1 个 `SkinnedMeshRenderer` |
| 第三方自带 Collider | **0**（因此命中与阻挡只能用项目自有对象） |
| 7 个动画的曲线路径 | **510–520 条全部匹配模型骨架，0 条对不上** |
| 根位移净变化 | 6 个为 0；`@Die` 为 (−0.328, 0.064, −0.046)（倒地自然位移，不移动 GameObject） |
| Idle 姿态脚底 / 肩高 | y ≈ **−0.072**（基本贴地）/ 0.693 |
| 体长 Z / 体宽 X | 1.539 / 0.497，体长沿 **+Z**，不需要旋转补偿 |
| 嘴部骨骼 | `RigJaw` (0, 0.396, 0.697)、`RigHead` (0, 0.567, 0.691) |
| 第三方材质 Shader | **`Standard`** → URP 下是粉色，必须建适配层 |
| 演示场景 / 演示 Animator | 工程里 **0 个**；Build Settings 只有工程自己的 3 个场景 |

> 绑定姿态的包围盒报脚底 −0.427，那是作者烘焙的绑定姿态数据，不能用来摆碰撞体。
> 上表取的是把 Idle 采样到模型之后的骨骼世界坐标。

##### 表现层：业务路径一行未改

意图选择、HFSM、技能调度、阶段门控、伤害、命中去重、脱战、休眠、两张 CSV、
生成点上限与 HUD 接口**全部未动**。补的两件事都是纯表现：

1. **新增 `MonsterAnimatorProjector`**，把 `MonsterFrameOutput.Animation`
   投到同名 Animator State。这个字段从 P2.2 第一天就由 HFSM 产出，
   只是方块替身没有 Animator、没人消费它。形态与玩家投影器完全一致：
   **7 个 State、0 个 Parameter、0 条 Transition**，默认 `Idle`，
   `CrossFadeInFixedTime` + 缓存 Hash，运行期无字符串查找。
2. **颜色反馈从单个 Renderer 改成全部身体 Renderer**（排除预警面片，
   它被染色就看不出预警了），仍用 `MaterialPropertyBlock`，不生成材质实例。

`GrayboxWolfView` → **`DuskshadowWolfView`**（用户选择方案 A），
`.cs.meta` 的 GUID `2df49a30f96d7e7408808ba379d27c05` 原样保留，
两个 Prefab 的脚本引用都没断，0 处残留引用。

##### 动画映射

| 业务动作 | Animator State | 第三方 FBX | 长度 | 循环 | 配置动作时长 |
| --- | --- | --- | --- | --- | --- |
| Idle | `Idle` | `@Idle` | 1.333 | 是 | — |
| Walk（巡逻/回家） | `Walk` | `@Walk Forward WO Root` | 1.167 | 是 | — |
| Run（追击） | `Run` | `@Run Forward WO Root` | 0.667 | 是 | — |
| Attack（普攻） | `Attack` | `@Bite Attack` | 1.167 | 否 | 1.30 |
| Skill（赤瘴吐息） | `Skill` | `@Breath Attack` | 1.333 | 否 | 1.55 |
| HitStun（受击） | `HitStun` | `@Take Damage` | 0.667 | 否 | 0.60 |
| Death（死亡） | `Death` | `@Die` | 2.000 | 否 | 2.00 |

只用 `WO Root` 那一套移动动画，Animator 的 `applyRootMotion` 关闭，
`rootMotionBoneName` 本来也是空 —— 三重保证不出现双倍位移。

**播放速度一律保持 1**：片段长度与配置动作时长差 10–14%
（咬击短 10%、吐息短 14%、受击长 11%、死亡正好一致）。
调速属于允许的表现层适配，但按 Q-020 立下的"不擅自发明速度值"交人工验收决定。

##### 正式 Prefab 结构（项目自有 / 第三方边界清晰）

```text
DuskshadowWolf.prefab                   项目自有  layer = Enemy
├── [DuskshadowWolfView] [CapsuleCollider] [NavMeshAgent] [MonsterAnimatorProjector]
├── Model                               第三方    FBX 的嵌套 Prefab 实例
│   └── [Animator]                      项目自有的 Controller，Root Motion 关闭
├── Hitbox  [MeleeHitbox]               项目自有  localOffset (0, 0.5, 1.2), radius 1.4
└── Warning [MonsterWarningView]        项目自有  Quad
```

- 碰撞体按实测体型重新给过（`direction=Z`、height 1.5、radius 0.33、center (0,0.42,0)），
  **没有照抄**方块替身那套 1.6 高的立方体尺寸。`NavMeshAgent` radius 0.35、height 0.9。
- 命中盒的**前向偏移 1.2 与半径 1.4 与方块替身完全一致** —— 这两项决定咬击能不能
  打到玩家，动它们等于动已验收的战斗手感。只把高度 0.8 → 0.5 对齐实测嘴部高度。
- 材质：第三方 `Standard` → 项目自有 `Universal Render Pipeline/Lit` 适配层
  （`DuskshadowWolfBody.mat`，引用同样的 Base 与 Glow 贴图，`_EMISSION` 开启）。
  **第三方源材质未被修改**，并有契约测试断言它仍然是 `Standard`。
- 方块替身仍由装配工具生成，但只作为**开发回退资产**；正式场景不引用它。

##### 顺带修掉一个一直是空操作的设置

装配工具里两处 `agent.updateRotation = false` 从来没有写进过资产 ——
实测 `updateRotation` 在 Unity 2021.3 里**不是序列化字段**
（Prefab 的 YAML 里没有 `m_UpdateRotation`）。一直没出问题是因为真正拦住 Agent 的
是序列化的 `angularSpeed = 0`。现在把 `updateRotation` 放到
`DuskshadowWolfView.Awake` 里运行期关闭，装配工具只负责那个确实会被序列化的字段，
契约测试也改成断言后者 —— 在 Prefab 上断言前者只会断言 Unity 的运行期默认值。

##### 本轮新增测试（19 条）

EditModeUnity（`DuskshadowWolfSetupTests`，11 条）：
正式 Prefab 存在且用了导入的模型、场景生成点引用正式狼而不是方块、
正式 Prefab 没有活动的 Cube 本体、有 Animator 且 Root Motion 关闭且绑的是项目自有 Controller、
七个动画片段全部存在、七个动作各自映射到同名 State 且 Animator 0 Parameter／0 Transition／默认 Idle、
全部 Renderer 有可用的 URP 材质且第三方源材质未被改写、
Collider／NavMeshAgent／Hitbox／Warning／bodyRenderers 全部绑定且不依赖第三方碰撞体、
连续两次装配 5 个产物逐字节相同、场景无重复狼／重复生成点／Missing Script、
演示场景与演示 Animator 既没导入也没进 Build Settings。

PlayMode（追加到 `P22CombatPlayModeTests`，8 条）：
生成的是正式模型且没有方块本体、巡逻播 Walk、追击播 Run、普攻播 Bite、
半血吐息播 Breath 且预警先于伤害窗口、受击播 Take Damage、死亡播 Die、
以及**动画投影不改变 HFSM 输出**（强行往 Animator 投一个"死亡"动画，
动作与意图都不变、狼也没死，下一帧投影自己纠回业务状态要求的动画）。

> 一次失败值得记下来：这 8 条最初在"切换的同一帧"就断言 Animator 状态，
> 结果 5 条失败 —— `CrossFadeInFixedTime` 只是把切换排进队列，要到下一次
> Animator 求值才生效。巡逻那条侥幸通过是因为它已经播了很多帧。
> 契约是"投影会到达 Animator"而不是"同一帧到达"，因此改成等待式断言。

##### 本轮实测结果（实际执行，非推断）

| 项目 | 结果 |
| --- | --- |
| 受控导入 | 12 个资产 + 4 个目录 meta；GUID 零重复（2,184/2,184） |
| Unity 导入预热 | 退出码 0，零 C# 编译错误 |
| 配置编译器 `--check` | **通过**：`ConfigVersion=p1-config-45539ed9c3ac`，`SchemaVersion=1.1.0`（与基线一致，本轮未动配置） |
| `NARAKA/Setup/Apply P2.2 Combat Setup` | 成功；Animator **7 State / 0 Parameter / 0 Transition**，NavMesh 检测到已有数据后跳过烘焙 |
| Unity EditMode | **total=484，passed=483，failed=0，skipped=1** |
| Unity PlayMode | **total=35，passed=35，failed=0，skipped=0** |
| 装配工具幂等 | P2 场景装配 + P2.2 战斗装配连续执行，10 个产物**逐字节相同**（含正式狼 Prefab／Controller／材质） |
| 第三方演示内容 | 演示场景 0 个、演示 Animator 0 个、Build Settings 无第三方场景 |
| 服务端 / 数据库 / 云端 / LegacyNetworkV1 | **零改动**，未提交、未推送、未部署 |

唯一跳过项仍是需要真实 Host 的 `LegacyClientLiveSmokeTests`。

##### 视觉验收反馈：怪物推着角色移动（2026-10-05，已修）

用户视觉验收通过，但报出一个行为问题：

> 怪物攻击角色后，且处于攻击范围内，怪物会处于追击状态推着角色移动，
> 而不是在原地攻击角色，这个可能是碰撞体的问题。

**不是碰撞体的问题**，碰撞体只是让它看得见。成因是三件事串起来：

1. 普攻冷却是 2 秒，而 `IsInNormalAttackRange()` 同时要求"冷却结束"与"在攻击距离内"。
   冷却期间 `NormalAttack` 分支不成立，行为树落到下一个分支 **`Chase`**；
2. `Chase` 的移动目标是**玩家坐标本身**，而 View 原来的到达判定是 `arriveDistance = 0.6`
   —— 比两边胶囊半径之和（狼 0.33 + 玩家 0.32 ≈ 0.65）还小，因此永远判定不出"到达"；
3. 玩家每帧用 `CharacterController.Move` 落重力，于是被穿透解算挤开。

**顺带挖出一处数值不自洽**（此前被这个缺陷掩盖着）：

| 项 | 值 |
| --- | --- |
| 配置攻击距离 | 3.2 |
| 命中盒实际触达 | 1.2 + 1.4 + 玩家半径 0.32 = **2.92** |
| 物理接触 | ≈ 0.65 |

"在 3.2 出手"本身打不到玩家 —— 之所以每次都能打中，正是因为狼先顶到了 0.65。
因此停止距离不能取攻击距离本身，必须 ≤ 2.92。登记在 [Q-024](NARAKA_待确认问题.md)。

**修法分两半**：

- **View**（几何）：追击停止距离 = `攻击距离 − chaseStopMargin(0.8)` = **2.4**，
  同时把它交给 `NavMeshAgent.stoppingDistance` 让它自己减速收尾。
  停多远取决于命中盒的偏移与半径，那是表现层数据，所以由 View 算。
- **Model**（决定）：View 把结论通过 `MonsterSenses.IsWithinEngageRange` 交给 Model
  （与 `DistanceFromHome` 同一条通道）。`MonsterIntent.Chase` 在已经到位时产出
  `Idle` + `MoveTarget.None` + `FaceTarget`，而不是 `Move` + `Run`。
  **意图仍然是 `Chase`** —— 变的是动作层怎么执行它，这正是 ADR-0018 的分工。

第二步不是可选的：如果只在 View 里停住，动作层仍是 `Move`、动画仍是 `Run`，
狼会**在原地跑步**，等于用一个可见毛病换另一个。

**没有改动**：攻击距离 3.2、感知 14、脱离 22、休眠 40、普攻冷却 2.0、
伤害倍率、决策频率 6Hz 全部未动。

新增 3 条测试：EditMode `AWolfAlreadyInEngageRangeWaitsInsteadOfRunningInPlace`、
`AWolfStillOutOfEngageRangeKeepsChasing`（反面，防止狼在远处站着不动），
PlayMode `TheWolfStopsShortInsteadOfPushingThePlayer`（跨过一整个
"攻击 → 冷却期追击 → 再攻击"循环，断言玩家位移 < 0.5、狼最近距离 > 1.2，
并确认这段时间行为树确实选过 `Chase`，否则测试没覆盖到目标场景）。

| 验收项 | 修复后结果 |
| --- | --- |
| Unity EditMode | **total=486，passed=485，failed=0，skipped=1** |
| Unity PlayMode | **total=36，passed=36，failed=0，skipped=0** |


##### 正式狼视觉验收清单（2026-10-05 用户已执行，结论：通过，仅报出上面那一个行为问题）

1. 狼使用 Black 外观，材质与贴图正常，**没有粉色**。
2. 没有 Missing Material、没有 T 姿势。
3. 脚底贴地，比例合适 —— **注意**：正式狼肩高约 0.69、体长约 1.54，
   而玩家模型高约 4.04（Q-021 的比例问题仍未解决）。本轮**没有缩放狼**，
   也没有动攻击距离 3.2 与感知距离 14。
4. Idle 循环正常。
5. 巡逻 Walk 正常。
6. 追击 Run 正常。
7. Bite 方向与命中盒一致（命中盒在 (0, 0.5, 1.2)、半径 1.4）。
8. Breath 方向、动画与红色预警一致。
9. Take Damage 表现正常。
10. Die 播放完整。
11. **不存在 Root Motion 双倍位移或动作后回退**。
12. 狼能正常巡逻、追击、攻击、脱战回家。
13. 玩家能够正常攻击并击杀狼。
14. HUD 目标状态仍正常。
15. 场景中只有一只狼。
16. 咬击/吐息的动画与配置时长差 10–14%，是否需要调播放速度由你决定。


#### 人工验收待执行项

1. 长离模型在地图一的实际表现（材质、蒙皮、缩放、朝向）。
2. 18个动画的实际观感与衔接，特别是3个循环动画（Idle/Walk_F/Run_F）的循环接缝。
3. 第三人称镜头手感：距离9米、轴心高度2.8米、俯仰-25°～65°、灵敏度、阻尼与指针锁定是否合适。
   （2026-09-28按用户反馈把距离与高度各调为原来的两倍：4.5→9 米、1.4→2.8 米。）
4. 相机相对移动的手感：转向角速度720°/秒是否跟手；`Run_Turnback`的135°阈值是否合适。
5. Shift点按与长按的手感边界是否符合预期。
6. 三段连招与2秒蓄力的节奏。
7. 灰盒传送门与训练假人的位置是否便于验证。
8. 加载界面在两次切图之间的连续性。

退出条件：完整战斗循环通过自动化与性能验收。
**P2.1 自动化与人工验收均已通过（2026-09-29）；P2.2 实现与自动化验收已通过（2026-10-05复验：EditMode 473/472/0/1，PlayMode 27/27，服务端354/354）。P2整体关闭仍等待手工视觉、Profiler性能和Q-017独立播放器构建门禁。**

### P3 远征闭环：未开始

- 地图一/地图二抽象测试场景、固定传送门、异步加载与死亡返回**已在P2.1提前完成**，见上。
- 本阶段剩余：临时背包、死亡清理、返回大厅结算、断网闪退结算和幂等（均依赖服务端权威）。

退出条件：正常、死亡、重复请求、断网和服务器异常测试全部通过。

### P4 内容系统：未开始

- 两名英雄、两把武器、十类怪物。
- 六章主线、支线、物品、魂玉、护甲、消耗品和宠物内容扩充；锻造基础能力已前置到P1。

退出条件：所有内容可由配置驱动，并能在灰盒地图完整跑通。

### P5 联网展示与热更新：未开始

- 最多10人移动同步、共享时间天气。
- HybridCLR、Addressables、Luban、资源CDN和A/B回滚。

退出条件：多人移动长稳、热更新和回滚演练通过。

### P6 打磨发布：未开始

- 正式地图素材绑定、任务空间布局、音频、特效、性能、可访问性和发布。

退出条件：候选版本通过功能、性能、安全、兼容和回滚门禁。

## 4. 下一步

P2.1已通过人工验收；P2.2功能已位于本地分支
`claude/p22-combat-and-animation-refresh`的提交
`94455edb98c1c18105ad24c907a9945bbab50f38`，相对`main`领先1个提交。下一步顺序：

1. 用户在Unity里人工确认换版后的18个动画、灰盒狼战斗闭环与战斗HUD实际观感；
   HUD布局和组件挂载继续由用户在Unity中手工完成。
2. 按`NARAKA_开发规范.md` §16补充Profiler记录，证明当前灰盒战斗的稳定态0B GC/frame与阶段帧时预算。
3. Q-017（R3依赖阻断独立播放器构建）需要单独修复并单独提交，否则无法用构建出的客户端验收；
4. 上述门禁通过后再合并P2.2分支并决定下一个P2子阶段；
5. P2开发期间不频繁更新低内存云主机，只有形成新的完整服务端大类且确需云端权威能力时才按ADR-0008集中发布。

当前分支以本地P2.2提交为基础，另有本轮尚未提交的验收修复与文档同步，整体尚未合并至`main`；
本轮验收没有推送、没有部署云端，没有修改数据库或云端NK库。P2.2基础提交只对服务端应用层做了一处配置SchemaVersion
`1.0.0 → 1.1.0`的同步，没有修改LegacyNetworkV1。

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
