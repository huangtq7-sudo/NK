# 《NARAKA》技术架构基线

版本：2.3
更新日期：2026-09-01
状态：实施权威摘要
详细来源：`outputs/naraka_design_v2/NARAKA_TDD_技术设计文档_MVC_v2.0.docx`

## 1. 固定技术基线

- 客户端：Unity 2021.3 LTS、C#、URP、Windows 首发。
- 服务端：C#/.NET 10 LTS、模块化单体优先，部署至阿里云Windows环境。
- 数据库：MySQL/InnoDB，SqlSugar ORM。
- 总体业务架构：模块化 MVC；不得改成 MVVM、纯 ECS 或其他顶层架构。
- 网络传输层暂时冻结，继续使用既有 AES、握手、Protobuf、粘包半包、心跳、Socket.Select 和协议分发方案。
- 客户端与服务端业务代码使用 C#。Shader Graph/HLSL、SQL迁移、YAML/JSON/Excel属于必要资产或配置，不改变C#主语言约束。
- Unity 6 专属的 Unity Behavior 不作为首版硬依赖；怪物行为树通过自研 C# 行为树接口或已授权、兼容 Unity 2021.3 的行为树插件实现。

## 2. 总体分层

### Model

- 保存领域实体、值对象、配置快照和纯业务规则。
- 负责战斗属性、体力、伤害、背包、任务、经济、远征和红点等状态。
- 不引用 MonoBehaviour、Animator、UI、Socket、SQL或具体资源。
- 尽量不引用 UnityEngine，以便通过 EditMode 和纯 .NET 单元测试。

### Controller

- 解释输入，编排用例、命令、状态机、网络请求和领域事件。
- 是 Model 与 View 之间唯一的业务协调层。
- 不直接访问 Socket、数据库或具体UI文本。
- 不长期持有场景 View，所有订阅必须随场景或生命周期释放。

### View

- 负责UI、模型、动画、特效、音效、镜头和玩家输入采样。
- 只能显示 Model/PresentationState，不决定伤害、扣货币、任务完成或掉落归属。
- Animator参数只是业务状态的投影，不能成为状态真相。

### Infrastructure

- 位于MVC之外，实现网络、资源、配置、日志、时间、持久化和遥测接口。
- 由 VContainer 在 Composition Root 注入。
- 禁止反向引用具体 View 或绕过 Controller 修改业务状态。

依赖方向：`View → Controller → Model/Domain Interface ← Infrastructure Implementation`。

## 3. 客户端框架与使用边界

- VContainer：Composition Root、场景 LifetimeScope 和依赖注入；禁止 Service Locator。
- UniTask：网络等待、场景加载、Addressables、动画编排和取消；所有异步链传递 CancellationToken。
- MessagePipe 1.8.2：跨模块离散事件，例如 `AccountAuthenticatedEvent`、`MonsterKilledEvent`、`LootPickedEvent`；通过VContainer注入`IDomainEventBus`，禁止全局总线和Service Locator。
- R3 1.3.1：只读连续状态和UI订阅；`ReactiveProperty<T>`封装在Application内部的`ReactiveState<T>`中，View只能取得既有`IReadOnlyState<T>`，不得取得可写属性。
- UnityHFSM或自研HFSM适配层：玩家动作和怪物动作状态机。
- 自研/授权行为树适配层：怪物巡逻、追击、技能选择和阶段决策。
- Input System：键鼠输入、按键重映射和动作上下文。
- Cinemachine：第三人称镜头、传送和首领镜头；不实现目标锁定。
- Animator、Animation Rigging、Timeline/Playables、Splines：角色、武器、处决和方向修正表现。
- Addressables：场景、Prefab、UI、音频和特效资源管理。
- HybridCLR：业务程序集热更新。
- Luban：Excel配置校验并生成客户端/服务端C#和二进制配置。
- Odin Inspector/Validator：配置编辑和批量校验；若无授权则使用自研 EditorWindow/PropertyDrawer 替代。
- DOTween：非战斗UI动画；核心战斗时间线不依赖Tween。
- Wwise：音频事件、Bus、State、Switch、RTPC和Snapshot；若授权或平台接入受阻，可通过 `IAudioService` 替换Unity Audio实现。
- DOTS/Jobs/Burst/Collections/Mathematics：只用于经Profiler证明有收益的感知粗筛、人群和批处理，不承载玩家核心状态。
- GPU Instancing/BRG：重复怪物、远端玩家代理和远景批渲染；玩家与首领保留高质量GameObject Animator。

## 4. 客户端模块

- Boot：版本、补丁、日志、设备能力和登录前初始化。
- Account：登录、会话、账号快照和单账号会话替换。
- Lobby：英雄、武器、仓库、商店、锻造、签到、抽奖和等级奖励。
- World：地图、传送、远征、共享时间和天气。
- Combat：输入、HFSM、伤害、体力、攻击、反击、处决和技能。
- Character：英雄属性、移动、动画投影和状态标签。
- Weapon：长剑、太刀、连招、命中窗口、切换和强化表现。
- AI：感知、行为树、动作状态机、技能调度和休眠。
- Navigation：NavMesh/A*、安全检测、路径简化、平滑和分级重算。
- Inventory：模板/实例、仓库、负载和临时战利品。
- Quest：任务状态机、目标监听和奖励领取。
- Pet：灵愈、破锋、目标选择和表现。
- Presence：最多10人地图实例的移动展示。
- RedDot：节点树、规则、SeenVersion和UI聚合。
- Audio/VFX：音频和特效服务。
- HotUpdate/Config：版本、资源、程序集和配置更新。

## 5. 玩家战斗实现

- 玩家采用分层状态机：Locomotion、Action、Reaction和Overlay Flags。
- Locomotion：Idle、Move、Sprint。
- Action：Dodge、Attack、Charge、Skill、Counter、Execute、SwitchWeapon、UseItem。
- Reaction：HitStun、Knockdown、Death。
- Overlay Flags：Invulnerable、SuperArmor、SpawnProtection。
- 霸体屏蔽HitStun但不屏蔽Damage和Death；无敌标签直接阻断伤害。
- 近战命中使用可配置扇形/胶囊扫掠和Physics NonAlloc查询。
- 使用 `HitId`、方向点积、高度、阵营、FlyingTag和已命中集合进行二次过滤。
- 逻辑攻击窗口由可测试的时间轴配置控制，动画事件只用于表现同步，不能是唯一判定来源。

## 6. 怪物AI与寻路

- 感知层：Jobs/Burst批量完成距离、视野和空间粗筛，近邻再精确检测。
- 决策层：行为树以约5–10Hz选择巡逻、追击、攻击、技能、撤退和阶段动作。
- 动作层：HFSM逐帧执行具体技能和动画。
- 调度层：按距离、冷却、权重、颜色标签、50%生命阶段和最近使用抑制选择技能。
- 休眠层：远离玩家的怪物进入Dormant并停止高频决策。
- 地面单位使用AI Navigation/NavMesh；飞行单位使用3D航点或导航体积。
- 玩家脱战自动寻路通过NavMeshPath/A*得到路径，再用盒式检测验证安全通道、点积删除冗余拐点，并用样条和预瞄点平滑移动。
- 运行时按“局部重算→完整重算→停止并提示”处理偏离和卡住。

## 7. UI架构

- UI仍严格属于MVC的View层，Atomic Design只作为组件制作规范。
- 大厅、英雄、仓库、商店、锻造、抽奖、签到和设置优先使用UI Toolkit；若Unity 2021.3具体控件或性能不足，则通过同一View接口切换uGUI实现。
- 战斗HUD、世界空间UI、背包、大地图、弹窗、死亡和结算使用uGUI。
- Bento只用于大厅的信息布局方式，不成为业务架构。
- UI Controller从领域Model生成只读 `PresentationState`，View使用R3订阅。
- 所有界面、Prefab、锚点、资源绑定和事件绑定通过C# Editor工具生成或校验，减少人工拖拽。

## 8. 网络边界

以下传输实现冻结，不在首版重构：

- AES-256/Rijndael CBC和现有密钥派生实现。
- `MsgSecret`公钥握手和会话密钥切换。
- Protobuf序列化。
- 粘包、半包处理。
- 目标心跳契约为30秒发送、120秒判死；已审计的旧客户端实际为300秒发送，冻结期服务端适配器临时使用360秒判死以避免误断线；传输层解冻后统一到30/120秒。
- 客户端收包线程、心跳线程、主线程派发。
- 服务端Socket.Select、多连接缓冲和循环完整发送。
- 服务端反射协议入口及客户端协议号委托字典。

业务层只依赖 `INetworkFacade`、`ISessionService` 和 `LegacyNetworkAdapter`：

- `SendAsync<T>`：普通业务发送。
- `RequestAsync<TReq,TRes>`：带超时、取消和RequestId的请求响应。
- `Events<T>`：服务器推送的主线程强类型流。
- `SendCriticalAsync`：退出和结算关键发送。

安全风险登记：CBC和旧KDF属于冻结遗留风险。冻结期间必须验证每消息随机IV、完整性校验和防重放序列；网络层未来解冻时优先迁移AEAD，但本阶段不修改传输实现。

## 9. 多人移动展示

- `PresenceState`包含PlayerId、Sequence、ServerTick、MapInstanceId、HeroId、WeaponId、Position、Yaw、LocomotionState和Flags。
- 服务器只校验速度、地图边界、合法传送和发包频率，然后向同实例玩家扇出。
- 近距离约10Hz，远距离约5Hz，客户端保留约100ms插值缓冲。
- 偏差大于5米时直接Teleport纠正；500ms无新状态时平滑转Idle。
- 不做多人战斗预测、回滚、攻击、技能、生命、怪物、掉落或宠物同步。

## 10. 热更新与配置

- 代码分层：Boot、Core、Hotfix、Generated、Content。
- HybridCLR更新业务Model、Controller和规则程序集。
- Addressables更新场景、Prefab、材质、UI、音频和特效。
- 配置采用 `Excel → Luban校验 → 生成两端C#/二进制 → 差异与签名 → 灰度 → 全量/回滚`。
- 版本分为ClientVersion、CodeHotfixVersion、ResourceVersion、ConfigVersion和ProtocolVersion。
- P0使用Host的`GET /bootstrap/config-version`返回ConfigVersion、客户端最低/最高版本和ProtocolVersion；客户端Bootstrap MVC必须在注册或登录前完成兼容性检查，失败时保持Account入口阻塞并允许重试。
- 版本预检不进入冻结的`LegacyNetworkV1`传输实现：本机开发允许loopback HTTP，直接远端地址必须使用HTTPS；单开发者云端环境允许按ADR-0006用加密SSH隧道把云端loopback映射为本机loopback，但不得直接公开HTTP端口。P0端点只提供兼容性元数据，不替代P5正式Manifest的签名、SHA-256、A/B缓存和KnownGood回滚机制。
- Manifest使用签名和SHA-256校验，客户端保留A/B缓存和KnownGood版本。
- FileSystemWatcher只允许开发环境，不作为生产配置热更方案。
- XNB属于XNA内容格式，不作为Unity项目的运行时数据方案。

## 11. 音频、特效和天气

- Wwise总线：Master、Music、Player、Weapon、Ability、Monster、Pet、Environment、UI、Ambience、Voice和Cinematic。
- RTPC包含Health、Armor、Combat、Boss、Time、Weather、Rain、Speed和Charge。
- 音效只能由已确认DomainEvent或ViewSignal触发，避免重复播放。
- 特效使用URP、Shader Graph、VFX Graph、Particle System、Decal、对象池、LOD和GPU Instancing。
- 天气由服务端广播权威状态，客户端通过URP Volume、光照、天空、粒子、Shader参数和音频平滑过渡。

## 12. 服务端架构

- 首版采用模块化单体，不为“先进”而提前拆微服务。
- 模块：Gateway/Session、Player、Inventory、Economy、Quest、Expedition、Gacha、Config和Presence。
- 基础框架：.NET Generic Host、Microsoft DI、SqlSugar、MySQL、FluentValidation、FluentMigrator、Quartz.NET和Polly。
- Redis/Tair用于会话、在线状态和短期锁；RocketMQ只用于审计、统计、邮件和非实时通知，不进入实时路径。
- log4net负责基础日志，OpenTelemetry提供Trace/Metrics，并接入Prometheus/Grafana或阿里云SLS/ARMS。
- 当前单开发者云端环境按ADR-0006部署：.NET 10自包含`win-x64` Host与MySQL 5.7.26同机运行，数据库、Bootstrap和LegacyNetworkV1均只监听loopback，本地Unity通过SSH隧道访问。
- 当前开发主机通过Windows服务和计划任务开机启动，不引入容器、Redis、MQ或额外监控平台。正式发布前再依据容量、安全与运维证据决定独立数据库、TLS入口、容器或ACK，不能把当前单机拓扑视为生产基线。

## 13. 数据库与事务

核心表包括账号、玩家、会话、等级奖励、货币余额、不可变货币流水、堆叠库存、实例物品、负载、武器强化、任务、宠物、红点已读、抽奖订单/结果/保底、签到、远征、远征快照、临时掉落、幂等记录、邮件、审计和配置版本。

- 所有关键业务使用RequestId或OrderId幂等。
- 货币余额和货币流水在同一个事务内保持守恒。
- 返回大厅或异常结算时锁定远征行，合并临时掉落，写入库存/邮件并保存SettlementSummary。
- 重复结算直接重放首次成功响应。
- 死亡事务只清理当前远征未结算的MonsterDrop，并记录DeathAudit。
- 玩家明确规定的断网/闪退规则为直接结算，不建立玩家可恢复远征窗口。
- 密码只在服务端使用Argon2id和独立盐哈希，禁止可逆保存。

## 14. 红点

- 使用节点树/前缀树聚合和脏标记局部刷新。
- 业务模块发布 `RedDotSourceChangedEvent`，RedDotController计算最终可见状态。
- 使用 `Version/SeenVersion`，不使用容易覆盖新内容的单一布尔值。
- 临时战利品拾取时不触发仓库红点，返回大厅正式入库后才触发。
- 任务可接取、可提交、等级奖励、签到、抽奖未展示结果和新物品分别由独立规则节点负责。

## 15. 性能与测试

- 1080p60目标帧时间16.67ms；主线程≤8ms、渲染线程≤8ms、GPU≤14ms。
- 战斗稳定态目标0B GC/frame。
- 近处活跃AI≤15，总活跃≤30，休眠实体≤50；地图素材确定后重新实测。
- 对象池覆盖怪物、远端玩家、投射物、VFX、掉落、伤害数字和常用UI。
- EditMode测试Model，PlayMode测试动画窗口、命中、UI绑定和场景加载。
- 服务端使用单元测试与MySQL/Redis Testcontainers集成测试。
- 协议使用Golden Files和Fuzz测试；服务端使用BenchmarkDotNet/k6或Socket压测。
- CI门禁检查C#格式、静态分析、测试、Luban/Protobuf生成一致性、Addressables重复依赖、URP材质和热更回滚。

## 16. 实施阶段

- P0 基础：仓库、Unity/服务端骨架、MVC、CI、网络适配、登录和空大厅。
- P1 战斗垂直切片：1英雄、1武器、1怪物、HUD和完整伤害循环。
- P2 远征闭环：两图抽象、传送、临时掉落、死亡、异常结算和幂等。
- P3 内容系统：2英雄、2武器、10怪物、任务、物品和锻造。
- P4 大厅经济：商店、抽奖、签到、等级奖励、红点和宠物。
- P5 联网展示与热更：10人移动、共享时间天气、HybridCLR和Addressables。
- P6 打磨发布：地图素材绑定、性能、音频、可访问性、测试和发布。
