# Claude仓库开发、复核与调试提示词

用途：在Claude能够访问`E:\NK项目`真实Git仓库和Unity工程时，将下面代码块完整作为首条任务发送。Claude可以自动读取仓库，不需要把源码大段粘贴到对话中。

```text
这是Unity游戏《NARAKA》的正式项目。项目根目录为：

E:\NK项目

不要采用旧对话记忆、DeepSeek回答或其他模型总结作为事实来源。只以当前Git仓库、项目权威文档、磁盘上的实际文件和本轮真实测试结果为准。

一、开始前必须完成的只读检查

1. 检查当前分支、HEAD、origin/main、提交标题和工作区状态；保留用户已有改动，不执行reset、checkout覆盖、clean、rebase、无关格式化或批量资源重写。
2. 完整读取项目根目录五份权威Markdown：
   - NARAKA_完整玩法设计.md
   - NARAKA_技术架构.md
   - NARAKA_开发规范.md
   - NARAKA_开发进度.md
   - NARAKA_待确认问题.md
3. 完整读取Docs/ADR下全部已接受ADR、Server/README.md，以及本轮直接相关的Docs/AI文档。
4. 以用户指定的Markdown 2.1纠错规则和权威Markdown纠错结论为最高优先级。只有本轮涉及玩法、完整架构或任务表冲突时，才读取outputs/naraka_design_v2中的GDD、TDD和任务表；若该目录不存在，明确报告，不得虚构其内容。五份Markdown与旧GDD/TDD/任务表冲突时，以Markdown为准。
5. 阅读本轮目标文件及相邻asmdef、Composition Root、Editor工具和测试。开始修改前先汇总：当前阶段、已完成能力、现有未提交改动、本轮唯一范围、验收标准、发现的冲突和准备修改的文件。

二、固定技术与架构边界

- Unity 2021.3.45f2c1 LTS，URP 12.1.15，Windows首发，1920×1080参考分辨率。
- 客户端业务严格采用模块化MVC：View → Controller → Model/Domain Interface ← Infrastructure Implementation。
- VContainer 1.18.0负责依赖注入；UniTask 2.5.11负责可取消异步；MessagePipe 1.8.2只传递跨模块离散事件；R3 1.3.1只发布连续只读PresentationState。
- 服务端为.NET 10模块化单体；数据库为MySQL 5.7.26，开发库为NK。
- LegacyNetworkV1传输层冻结。未经用户明确授权，不得修改握手、AES、Protobuf、粘包半包、心跳、Socket.Select、协议分发、线协议名或网络线程模型。
- 不重新设计已确认玩法，不使用Unity 6专属API，不引入新的第三方包，不把UI、Animator、网络DTO或缓存当作业务状态真相。
- 不读取、打印、复制、提交或记录.env、密码、令牌、私钥、SSH口令、数据库连接串或数据库导出。
- 同一时间只有一个模型修改同一工作区或同一组文件。DeepSeek输出只是草案；必须读取磁盘实际文件并逐项复核，不能盲目覆盖。

三、当前已经完成的事实

- P0已经关闭：Account、Lobby、Bootstrap模块化MVC已经接入，登录前ClientVersion、ConfigVersion和ProtocolVersion检查会阻止不兼容注册/登录。
- Unity通过LegacyNetworkV1真实Socket完成Argon2id注册登录、连接级认证、MessagePipe认证事件、R3只读PresentationState和空大厅跳转。
- 当前启动场景是NK/Assets/Scenes/SampleScene.unity；P0ClientShell包含一个UIDocument以及ConfigVersionView、AccountView、LobbyView，GameLifetimeScope由VContainer装配。
- 当前登录、版本检查和大厅View使用代码动态构建UI Toolkit元素；它们是已验证的P0功能界面，不是正式美术界面。
- AccountPresentationState包含Ready、Registering、LoggingIn、Authenticated、Failed；AccountView必须保留账号/密码输入、注册、登录、忙碌禁用、状态消息、认证后隐藏和密码清空行为。
- ConfigVersionView必须在预检Pending/Checking/Blocked时覆盖登录界面，Blocked时提供“重新检查”，Ready后隐藏；不得绕过IStartupReadiness。
- LobbyView目前只是P0空大厅。本轮不得开发大厅经济、英雄、武器、仓库、商店或P1战斗功能。
- Unity基础CI已通过：干净Library独立预热后EditMode 14通过、1环境跳过、0失败，PlayMode 1/1；真实联网启用时EditMode 15/15。服务端测试42/42。
- 云端开发环境为Windows Server 2016 Datacenter、MySQL 5.7.26和.NET 10 Host；3306、5222、8011只监听loopback，本地通过手动SSH隧道访问。部署已验收，但与本轮UI任务无关，不得修改。

四、你的职责

- 先审查用户或DeepSeek已经放入磁盘的UXML、USS、C#和场景/UIDocument改动；如果磁盘中没有草案，可以在下面限定范围内完成最小实现。
- 修复Unity 2021.3兼容性、UI Toolkit查询与回调、VContainer注入、R3订阅、UniTask取消、OnDestroy释放、空引用、重复注册、元素命名、PanelSettings和场景序列化问题。
- View只能显示PresentationState并把用户意图交给既有Controller。不得把校验、认证、版本兼容或网络访问搬进View。
- 实际运行与风险相称的Unity编译、EditMode、PlayMode和人工Play Mode验收。没有运行的项目必须写“未验证”，禁止写“应该通过”。
- 完成后列出实际改动文件、场景/UIDocument/Inspector变化、测试命令与数量、人工验收结果、未验证事项和Git状态。不要自行commit或push，除非用户另行明确授权。
- 完成本轮后暂停并向用户汇报，取得许可后才能开发下一个页面或模块。

五、本轮唯一任务：正式登录界面View升级

目标：在不改变P0账号业务和Bootstrap预检的前提下，把现有程序化功能登录页升级为可维护的正式UI Toolkit登录界面，并复核用户按照DeepSeek说明进行的手工挂载。

重点检查和可能涉及的文件：
- NK/Assets/Game/Features/Account/View/AccountView.cs
- NK/Assets/Game/Features/Bootstrap/View/ConfigVersionView.cs
- NK/Assets/Game/Features/Account/Controller/AccountPresentationState.cs（只读契约，除非发现明确Bug且先报告）
- NK/Assets/Game/Features/Bootstrap/Controller/ConfigVersionPresentationState.cs（只读契约）
- NK/Assets/Game/EditorTools/P0ProjectSetup.cs
- NK/Assets/Game/Tests/PlayMode/BootScenePlayModeTests.cs
- NK/Assets/Scenes/SampleScene.unity
- 新增登录UXML/USS时放在Account View模块所属目录，并保持稳定元素名和可重复Editor装配。

功能要求：
1. 保留单一UIDocument和现有GameLifetimeScope，不新建第二套登录场景、Controller、Model、网络Gateway或全局状态。
2. 登录界面必须包含标题、账号输入、密码输入、注册按钮、登录按钮和状态消息；密码默认遮挡。
3. Registering或LoggingIn时账号、密码和两个按钮均禁用；Controller状态恢复后重新启用。
4. Authenticated后隐藏登录面板、清空密码，并由既有AccountAuthenticatedEvent进入空大厅。
5. ConfigVersionOverlay必须位于登录页之上并阻止交互；Blocked时显示现有错误信息和重新检查按钮，Ready后隐藏。
6. 使用现有PanelSettings的1920×1080 Scale With Screen Size基线，并至少人工检查1920×1080和1600×900，不出现裁切、重叠或不可点击区域。
7. 可以使用用户已经提供并导入仓库的正式背景/Logo资源；资源尚未进入仓库时使用清晰占位引用并报告，不得伪造路径、复制外部素材或把绝对E盘素材路径写入运行时代码。
8. 不新增“记住密码”、找回密码、第三方登录、服务器选择、公告、付费入口或未确认玩法。

明确不做：
- 不修改Account Controller/Model业务规则、Bootstrap Controller/Model、Lobby业务、服务端、MySQL、部署、CI、LegacyNetworkV1和任何P1/P4功能。
- 不重做注册登录协议，不改变版本号、端口、连接地址或认证会话。
- 不为了视觉效果引入DOTween或其他新包。

验收标准：
- Unity无C#编译错误和Console异常。
- 现有启动场景仍能找到AccountPanel、ConfigVersionOverlay和LobbyPanel；若元素结构调整，同步更新并运行对应PlayMode测试。
- 版本检查未Ready时无法操作注册/登录；Blocked可重试；Ready后登录页可用。
- 注册、正确登录、错误密码/账号不存在、忙碌状态和认证后大厅切换的可观察行为保持一致。
- 相关EditMode与PlayMode测试实际通过，并报告精确通过/失败/跳过数量。
- Git差异只包含本轮登录View、必要Editor装配、测试和对应文档，不包含Library、Temp、Logs、密钥或无关格式化。
```
