# 《NARAKA》开发进度与续聊入口

版本：1.11
更新日期：2026-09-21
当前阶段：P1大厅与账号长期系统已完成本地自动化、云端集中部署和真实客户端验收并正式关闭；P2战斗垂直切片尚未开始，等待独立范围确认与开发许可

## 1. 当前状态

- 游戏玩法已经形成v2 GDD，并补充了跨对话Markdown基线。
- 技术架构已经形成v2 TDD，顶层架构固定为模块化MVC。
- Unity版本冲突已经纠正：正式基线锁定Unity 2021.3 LTS + URP，不采用Unity 6作为首发基础。
- 断线规则冲突已经纠正：遵循用户明确要求，断网或闪退直接幂等结算，不采用5分钟战斗恢复窗口。
- 英雄、技能、武器、10类怪物、任务六章、物品、经济、宠物、天气和多人移动规则已经具备首版数值基线。
- 地图美术和正式空间布局暂不设计，等待用户提供地图素材。
- 正式Unity工程位于`E:\NK项目\NK`，版本锁定为`2021.3.45f2c1`；P0客户端和服务端骨架已经建立。
- 当前已完成P0基础以及P1大厅与账号长期系统。P1包含账号快照、三种货币、英雄/兵器选择、仓库、商店、锻造、抽奖、签到、账号等级奖励、成就、红点、好友和一对一聊天；服务端权威、幂等、持久化、配置驱动与模块化MVC边界均已落实。`p1-lobby-systems-002`已部署到阿里云Windows Server 2016开发环境，MySQL迁移`0001`至`0009`、27张表、12项能力声明、Bootstrap门禁和生成配置版本均已验收；本地通过手动SSH隧道完成真实客户端闭环。P2尚未开始。

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

### P2 战斗垂直切片：未开始

- 顾沉岳、长剑、暮影妖狼和战斗HUD。
- 移动、镜头、体力、闪避、三段攻击、蓄力、伤害、护甲、受击和死亡。
- 先使用灰盒场景和占位资源验证完整循环。

退出条件：完整战斗循环通过自动化与性能验收。

### P3 远征闭环：未开始

- 地图一/地图二抽象测试场景。
- 固定传送门、异步加载、临时背包、死亡清理、返回大厅结算、断网闪退结算和幂等。

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

P1已经关闭。下一阶段为P2战斗垂直切片，但尚未开始任何P2业务实现。开始前必须：

1. 保留并单独审查当前未提交的大厅手工UI调整和角色资源导入工具，不能把未验证内容伪装成P1已归档基线；
2. 把P2拆成可独立验收的灰盒移动/镜头、战斗状态机、攻击与命中、怪物AI、战斗HUD、自动化与性能门禁；
3. 明确P2第一批任务并取得用户许可后再修改代码；Unity场景、Prefab、UI挂载和视觉调整继续由用户手工完成；
4. P2开发期间不频繁更新低内存云主机，只有形成新的完整服务端大类且确需云端权威能力时才按ADR-0008集中发布。

在取得P2开发许可前，不修改P2代码、云服务器或云端NK库。

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
