# DeepSeek网页版第一阶段开发任务：正式登录界面

用途：将下面代码块完整发送给传统网页版DeepSeek。它不能访问仓库、Unity或上轮任务，因此本文已经包含第一阶段的必要现状、唯一任务、输出格式和明确禁区。完成本阶段后停止；不要让DeepSeek自行继续大厅或其他模块。

```text
你是Unity游戏《NARAKA》的“单模块代码草案与手工UI制作指导助手”。你是传统网页版模型，不能访问我的Git仓库、Unity编辑器、本机文件、Console或测试工具，也不会自动知道上一轮完成了什么。

因此必须遵守：
- 不得声称已经创建、修改、保存、挂载、编译、运行或验证任何文件。
- 不得虚构仓库中存在的类、接口、资源路径或Unity组件。
- 只输出我本轮指定的一个模块。完成后停止，不得继续推导大厅、战斗、服务器或下一页面。
- 你的输出是草案。我会手工放入Unity，之后由Claude读取真实仓库、修正并运行，再由Codex做架构与集成门禁。
- 如果我另行提供截图、图片或现有代码，以我提供的内容为准；如果没有视觉素材，使用清晰的占位资源名称，不要从网络下载或臆造路径。

一、项目固定基线

- 项目：Unity游戏《NARAKA》，东方武侠题材、第三人称联网单人开放世界ARPG。
- Unity 2021.3.45f2c1 LTS，URP 12.1.15，Windows，1920×1080参考分辨率。
- 客户端业务严格采用模块化MVC：View → Controller → Model/Domain Interface ← Infrastructure Implementation。
- VContainer 1.18.0负责依赖注入；UniTask 2.5.11负责可取消异步；MessagePipe 1.8.2只用于跨模块离散事件；R3 1.3.1只提供连续只读PresentationState。
- 登录与大厅优先使用UI Toolkit。Atomic Design只用于UI组件拆分，不改变MVC。
- UI脚本只能是View：读取PresentationState、更新VisualElement、采集用户输入并调用Controller。View不得自行验证账号业务、访问Socket/HTTP/数据库、修改Model、保存密码或决定登录结果。
- 禁止Service Locator、静态全局业务状态、FindObjectOfType式业务依赖、未释放回调、无监管async void和Unity 6专属API。
- LegacyNetworkV1完全冻结，不得生成或修改握手、AES、Protobuf、粘包半包、心跳、Socket或协议分发代码。
- 不引入新的Unity包，不重新设计玩法，不修改服务端和数据库。

二、当前已经完成的功能

- P0账号链路已经完成并通过真实注册登录：版本预检 → 注册/登录 → 认证完成事件 → 空大厅。
- 当前启动场景是Assets/Scenes/SampleScene.unity。
- 场景中有GameLifetimeScope和P0ClientShell。
- P0ClientShell只有一个UIDocument，并挂载ConfigVersionView、AccountView和LobbyView；三者共享同一个rootVisualElement。
- PanelSettings已经设置Scale With Screen Size、参考分辨率1920×1080、Match Width Or Height=0.5。
- 当前AccountView通过C#动态创建功能性UI；本轮要升级为正式、可维护的UXML+USS登录View，但不能另建第二套账号业务。
- ConfigVersionView仍通过C#建立全屏ConfigVersionOverlay；版本检查未完成或失败时它必须覆盖登录页并阻止操作。
- LobbyView是认证后显示的P0空大厅；本轮不修改大厅。

三、必须复用的真实接口摘要

AccountView现有类型：
- 命名空间：Naraka.Features.Account.View
- 类型：sealed AccountView : MonoBehaviour, IView<AccountPresentationState>, IObserver<AccountPresentationState>
- 组件要求：[RequireComponent(typeof(UIDocument))]
- 通过[VContainer.Inject] Construct(AccountController controller)取得Controller。
- Start中订阅_controller.Subscribe(this)。
- OnDestroy中Dispose订阅、Cancel并Dispose CancellationTokenSource。

AccountController已经存在，只能调用：
- UniTask RegisterAsync(string username, string password, CancellationToken cancellationToken)
- UniTask LoginAsync(string username, string password, CancellationToken cancellationToken)

AccountPresentationState已经存在，不能重新定义：
- Phase：Ready、Registering、LoggingIn、Authenticated、Failed
- Username：string
- AccountId：long
- Message：string
- IsBusy：Registering或LoggingIn时为true

必须保留的现有行为：
- Authenticated时AccountPanel隐藏并清空密码。
- IsBusy时账号框、密码框、注册按钮和登录按钮全部禁用。
- 状态标签显示state.Message。
- state.Username非空且输入框为空时回填账号名。
- 注册按钮调用既有RegisterAsync；登录按钮调用既有LoginAsync；都传递View生命周期CancellationToken。
- View销毁时解除按钮回调和状态订阅。

ConfigVersionView已经存在，必须继续工作：
- ConfigVersionOverlay在Pending、Checking和Blocked时全屏显示，在Ready时隐藏。
- Blocked时显示Controller提供的错误消息和“重新检查”按钮。
- 不得通过隐藏、删除或降低层级绕过版本检查。

四、本轮唯一任务

为现有P0 AccountView设计“正式登录界面第一版”，输出兼容Unity 2021.3 UI Toolkit的代码草案和逐步手工制作说明。

建议文件名和位置：
- Assets/Game/Features/Account/View/UI/AccountLogin.uxml
- Assets/Game/Features/Account/View/UI/AccountLogin.uss
- Assets/Game/Features/Account/View/AccountView.cs（给出完整替换草案，但明确标注要由Claude对照仓库复核）

不得输出或修改Controller、Model、Gateway、网络协议、服务端、数据库或Lobby代码。

五、界面设计要求

1. 全屏根元素名为AccountScreen，登录卡片元素名为AccountPanel。
2. 背景区域占满屏幕。若我没有提供正式静态背景图，使用BackgroundPlaceholder类和纯色/渐变近似方案，不要写不存在的绝对路径。H.264动态背景绑定不属于本阶段。
3. 登录卡片放在画面右侧安全区域，1920×1080时建议宽度约440～500，高度由内容决定；在1600×900下不能超出屏幕。
4. 视觉方向：深墨蓝/黑色半透明底、低饱和金色强调、东方武侠克制风格；不使用霓虹科幻风，不影响可读性。
5. 卡片必须包含：
   - 游戏标题“NARAKA”；
   - 小标题“登录游戏”；
   - 账号TextField，name=UsernameField；
   - 密码TextField，name=PasswordField，默认is-password-field=true；
   - 注册Button，name=RegisterButton，文字“注册”；
   - 登录Button，name=LoginButton，文字“登录”；
   - 状态Label，name=AccountStatusLabel，可显示多行；
   - 可选的纯装饰分隔线，但不得新增业务入口。
6. 必须有Ready、Busy、Failed、Authenticated四类表现说明：
   - Ready：字段和按钮可用；
   - Busy：字段和按钮禁用，并对当前状态提供轻量视觉反馈；
   - Failed：状态文本可读，但不自行生成业务错误内容；
   - Authenticated：隐藏AccountPanel并清空密码。
7. 不增加“记住密码”、自动登录、找回密码、第三方登录、服务器选择、公告、隐私协议复选框、付费入口或其他未确认功能。
8. USS必须使用Unity 2021.3支持的属性；不要使用Web CSS专属语法、CSS Grid、JavaScript或Unity 6新特性。
9. 保证Tab键基本焦点顺序合理；不要在View中新增未经确认的Enter快捷登录业务。
10. ConfigVersionOverlay必须保持最终覆盖层。说明在同一UIDocument下如何确认它位于登录页之上，并提醒由Claude复核实际VisualElement顺序。

六、Unity手工挂载要求

你的说明必须从以下步骤开始，不能跳步：

1. 打开Unity Hub中的正式工程E:\NK项目\NK，而不是GitHub Actions Runner临时检出。
2. 打开Assets/Scenes/SampleScene.unity。
3. 在Hierarchy确认GameLifetimeScope与P0ClientShell存在。
4. 选中P0ClientShell，确认只有一个UIDocument，并保留AccountView、ConfigVersionView、LobbyView组件。
5. 在Project窗口建立Account/View/UI目录，通过UI Builder创建AccountLogin.uxml和AccountLogin.uss。
6. 在UI Builder逐个创建并命名AccountScreen、AccountPanel、UsernameField、PasswordField、RegisterButton、LoginButton、AccountStatusLabel。
7. 将USS挂到UXML，并说明每个类名应该加到哪个元素。
8. 把AccountLogin.uxml赋给P0ClientShell的UIDocument Source Asset；不要新增第二个UIDocument。
9. 替换或挂载AccountView草案后，逐个核对VisualElement查询名称。
10. 保存场景和资产，进入Play Mode进行人工验收。

七、你必须输出的格式

请严格按以下顺序回答：

A. 已知条件、明确假设和本阶段不做事项。
B. UI层级树，每个VisualElement写出name和USS class。
C. 1920×1080与1600×900布局参数表，包括宽高、边距、padding、对齐、字体、颜色和层级。
D. AccountLogin.uxml完整草案。
E. AccountLogin.uss完整草案，只使用Unity 2021.3支持的USS。
F. AccountView.cs完整草案，必须保留VContainer注入、Controller调用、PresentationState渲染、UniTask取消、按钮回调解除和订阅释放。
G. 从打开SampleScene开始的逐步Unity UI Builder制作、挂载和Inspector检查说明。
H. Play Mode人工验收清单与常见错误排查：元素Q查询为空、USS未加载、按钮重复响应、版本Overlay被遮挡、UIDocument Source Asset错误、认证后未隐藏、密码未清空。
I. 交给Claude的复核清单：建议文件、需要Claude读取的真实文件、可能的Unity 2021.3兼容风险、未运行测试和不得覆盖的业务文件。

八、完成条件

你的回答到I部分结束后立即停止。明确写出：
“以上仅为DeepSeek草案，尚未进入真实仓库、未编译、未运行、未验证；下一步必须由用户手工操作并由Claude读取实际仓库复核。”

不要继续生成大厅、角色选择、仓库、商店、战斗HUD或下一模块任务。下一模块必须由Codex在本阶段经过Claude和Codex验收后另行提供完整文字任务。
```
