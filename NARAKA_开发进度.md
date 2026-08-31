# 《NARAKA》开发进度与续聊入口

版本：1.2
更新日期：2026-08-31
当前阶段：P0工程基础进行中

## 1. 当前状态

- 游戏玩法已经形成v2 GDD，并补充了跨对话Markdown基线。
- 技术架构已经形成v2 TDD，顶层架构固定为模块化MVC。
- Unity版本冲突已经纠正：正式基线锁定Unity 2021.3 LTS + URP，不采用Unity 6作为首发基础。
- 断线规则冲突已经纠正：遵循用户明确要求，断网或闪退直接幂等结算，不采用5分钟战斗恢复窗口。
- 英雄、技能、武器、10类怪物、任务六章、物品、经济、宠物、天气和多人移动规则已经具备首版数值基线。
- 地图美术和正式空间布局暂不设计，等待用户提供地图素材。
- 正式Unity工程位于`E:\NK项目\NK`，版本锁定为`2021.3.45f2c1`；P0客户端和服务端骨架已经建立。
- 当前已完成MySQL/SqlSugar、Argon2id账号业务、真实LegacyNetworkV1 Socket、Unity Account模块化MVC、MessagePipe/R3实现、登录前`ConfigVersion`检查、真实客户端登录冒烟和空大厅；基础CI尚未完成，因此P0仍未关闭。

## 2. 已有成果

### 设计文件

- `outputs/naraka_design_v2/NARAKA_GDD_游戏设计文档_v2.0.docx`
- `outputs/naraka_design_v2/NARAKA_TDD_技术设计文档_MVC_v2.0.docx`
- `outputs/naraka_design_v2/NARAKA_开发任务与模块依赖_v2.0.xlsx`
- `docs/NARAKA/NARAKA_完整玩法设计.md`
- `docs/NARAKA/NARAKA_技术架构.md`
- `docs/NARAKA/NARAKA_开发规范.md`
- `docs/NARAKA/NARAKA_开发进度.md`
- `docs/NARAKA/NARAKA_待确认问题.md`

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

### P0 基础：进行中

已完成：

- 以`E:\NK项目`作为单仓库根目录，建立Git忽略规则、属性规则和无密钥环境变量模板。
- 在正式Unity工程中锁定URP 12.1.15、VContainer 1.18.0、UniTask 2.5.11、MessagePipe 1.8.2和R3 1.3.1；关键第三方包以项目内嵌包或固定DLL保存，避免Git网络不可用时无法还原。
- 建立`Game.Core.Domain`、`Game.Core.Application`、`Game.Infrastructure.Network`、`Game.Boot`和Editor/Test程序集边界。
- 建立MVC接口、`INetworkFacade`、`MockNetworkFacade`和`LegacyNetworkAdapter`边界；MessagePipe/R3实现位于Infrastructure/Application接缝后，未改变既有业务接口或网络传输层。
- 建立VContainer Composition Root、URP自动配置工具并写入启动场景。
- 建立.NET 10 LTS模块化单体服务端Solution、健康端点、模块清单、LegacyNetworkV1边界和架构测试。
- Unity批处理配置返回码0；Unity EditMode回归14项通过、0失败、1项真实联网测试按环境条件默认跳过，PlayMode启动场景测试1/1通过；此前单独启用的Unity→Host→MySQL真实注册登录冒烟1/1通过并已删除测试账号。
- 服务端Release测试构建通过，架构测试4/4、Application测试7/7、LegacyNetworkV1测试21/21、Infrastructure测试10/10通过，共42项、0失败；NuGet直接和传递依赖漏洞审计为0。
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

待完成：

- 建立客户端/服务端基础CI，并以持续集成结果完成P0最终退出验收。

退出条件：客户端能够启动、检查版本、登录并进入空大厅；服务端和MySQL完成健康检查。

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

继续P0，按以下顺序推进：

1. 建立客户端/服务端基础CI，确保Unity EditMode、PlayMode和.NET测试持续通过。
2. 执行P0最终本机验收并关闭P0。
3. P0关闭后，再准备阿里云Windows Host与云端MySQL部署。

## 5. 新对话续接提示词

在新的Codex对话中打开同一个项目目录，然后发送：

> 请先完整读取 `docs/NARAKA/` 下的五份项目文档，以及 `outputs/naraka_design_v2/` 下的GDD、TDD和任务表。以Markdown文档中的2.1纠错规则为最高优先级，继续开发Unity 2021.3 LTS + URP的《NARAKA》。客户端业务必须严格采用模块化MVC，网络传输层暂不修改。开始前先汇总当前阶段、已完成内容、下一项任务和发现的冲突，不要重新设计已经确认的玩法。

如果只想继续某个模块，在上述文字后追加：

> 本轮只继续【模块名称】，完成代码、Unity配置、测试和进度文档更新；不要扩大到其他阶段。

## 6. 每轮结束要求

- 更新本文件中的当前阶段和完成项。
- 新增或关闭`NARAKA_待确认问题.md`中的问题。
- 若修改玩法，同步更新`NARAKA_完整玩法设计.md`。
- 若修改架构，同步更新`NARAKA_技术架构.md`和对应ADR。
- 给出实际执行过的编译、测试、渲染或构建结果，不用“应该可以”代替验证。
