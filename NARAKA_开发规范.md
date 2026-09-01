# 《NARAKA》开发规范

版本：1.1
更新日期：2026-09-01
适用范围：Unity客户端、C#服务端、配置工具、Editor工具、测试与部署脚本

## 1. 基本原则

- 客户端业务严格使用模块化MVC，任何框架都只能作为MVC内部实现工具。
- 同一个业务状态只能有一个权威来源，Animator、UI文本、缓存副本和网络DTO都不能成为第二份真相。
- 先完成可测试的正确实现，再依据Profiler和容量数据引入DOTS、BRG、缓存或分布式组件。
- 所有账号资产、经济、任务奖励、抽奖和远征结算由服务端权威判定。
- 网络传输层暂时冻结，新增功能通过适配器扩展，不复制或绕过现有Socket/AES逻辑。
- 不以“使用框架数量”作为完成标准；每个框架必须有明确职责、生命周期和替换边界。

## 2. 语言和文件类型

- Unity客户端运行时代码：C#。
- Unity Editor自动化工具：C#。
- 服务端业务、数据访问和后台任务：C#。
- 测试代码：C#为主。
- Shader使用Shader Graph，确有必要时使用HLSL。
- 数据库迁移允许SQL，但优先由FluentMigrator或C#迁移封装。
- 配置源允许Excel，生成物为C#类型和二进制/JSON调试数据。
- CI/CD允许YAML和PowerShell；这些属于工程配置，不属于游戏业务语言。
- Unity项目不使用XNB作为运行时数据格式。

## 3. 推荐目录

```text
Assets/Game/
  Boot/
  Core/
    MVC/
    Domain/
    Common/
  Features/
    Account/
      Model/
      Controller/
      View/
    Lobby/
    World/
    Combat/
    Character/
    Weapon/
    AI/
    Navigation/
    Inventory/
    Quest/
    Economy/
    Pet/
    Presence/
    RedDot/
  Infrastructure/
    Network/
    Config/
    Resources/
    Audio/
    Telemetry/
    Time/
  Generated/
    Protocol/
    Config/
    UI/
  EditorTools/
  Tests/
    EditMode/
    PlayMode/
```

服务端按相同领域划分程序集：Gateway、Player、Inventory、Economy、Quest、Expedition、Gacha、Config、Presence和SharedKernel。

## 4. MVC硬性约束

### Model允许

- 领域实体、值对象、纯规则、状态快照和Repository接口。
- 可测试的伤害、体力、背包、任务、保底、锻造和结算规则。
- 不可变集合或受控修改入口。

### Model禁止

- MonoBehaviour、GameObject、Animator、UI组件、Addressables句柄。
- 直接发送网络消息、读写数据库或播放表现。
- 静态全局业务状态。

### Controller允许

- 输入解释、Use Case编排、命令、HFSM、事件发布和PresentationState构建。
- 通过接口请求网络、时间、配置、资源或持久化能力。

### Controller禁止

- 直接设置UI文字、颜色或Animator参数。
- 直接访问Socket、AES、SqlSugar或具体数据库连接。
- 跨场景长期持有View引用。

### View允许

- MonoBehaviour、UIDocument、VisualElement、uGUI、Animator、VFX、Audio和Cinemachine。
- 将输入转换为意图并交给Controller。
- 订阅只读PresentationState和ViewSignal。

### View禁止

- 计算最终伤害、扣除货币、推进任务或决定掉落归属。
- 直接修改Model或发送业务协议。

## 5. 程序集规则

- 示例：`Game.Core`、`Game.Feature.Combat.Model`、`Game.Feature.Combat.Controller`、`Game.Feature.Combat.View`、`Game.Infrastructure.Network`、`Game.Generated.Protocol`。
- Model程序集不能引用View、Infrastructure或UnityEngine对象程序集。
- Controller可以引用Model和抽象接口，不能引用Infrastructure实现。
- View可以引用Controller公开接口和PresentationState，不能获得Model可写引用。
- Infrastructure实现由VContainer在Composition Root注册。
- 使用Assembly Definition和依赖测试阻止反向引用。

## 6. 命名规范

- 命令：动词+对象+`Command`，如`DodgeCommand`、`SettleExpeditionCommand`。
- 领域事件：已发生事实+`Event`，如`MonsterKilledEvent`。
- 查询：`Get/Query`+对象，如`GetInventoryQuery`。
- 表现信号：对象+`ViewSignal`，如`ArmorBrokenViewSignal`。
- 异步方法以`Async`结尾。
- 接口以`I`开头，实现类不添加无意义的`Impl`后缀。
- 配置ID使用强类型值对象，避免裸`int`在不同领域之间误传。
- 私有字段使用`_camelCase`，公共类型和成员使用`PascalCase`。
- 不使用含糊缩写；协议DTO、领域对象和View数据必须通过命名区分。

## 7. 异步、事件和生命周期

- UniTask调用必须接收或创建明确的CancellationToken。
- 场景退出、View销毁、会话替换和应用退出必须取消对应任务。
- 禁止无监管的`async void`，只有Unity事件入口允许并必须捕获异常。
- MessagePipe用于离散事实事件，R3用于连续只读状态，不得用二者重复表达同一状态。
- 所有订阅放入CompositeDisposable或LifetimeScope，在场景卸载时释放。
- 网络线程只解帧、解密、反序列化和入队，任何Unity对象只能在主线程访问。

## 8. 状态机规范

- 玩家Locomotion、Action、Reaction互相独立但转换规则集中管理。
- 无敌、霸体和重生保护使用Overlay Tag，不创建互相冲突的平行主状态。
- 状态转换必须有原因、入口、退出和取消窗口。
- 动画事件不能是唯一逻辑判定；命中窗、无敌帧和处决窗由可测试配置驱动。
- 怪物行为树负责选行动，怪物HFSM负责执行行动，两处不能同时保存“当前动作”真相。

## 9. 配置规范

- 所有英雄、武器、技能、怪物、任务、物品、掉落、商店、抽奖、签到、宠物和天气数值必须配置化。
- 禁止在业务代码中散落魔法数字。
- Excel源经过Luban Schema、引用完整性和业务规则检查后才能生成。
- 客户端和服务端使用同一Schema生成不同目标代码。
- 配置发布必须包含ConfigVersion、Hash、签名、兼容范围和回滚版本。
- ScriptableObject主要承载Unity表现引用；服务端权威数值不能只存在ScriptableObject。

## 10. UI规范

- UI必须遵循MVC：UI脚本只作为View，所有业务动作交给Controller。
- Atomic Design用于Atom、Molecule、Organism、Template和Screen的组件复用。
- 1920×1080为设计参考，必须验证16:9、安全区、不同DPI和窗口模式。
- UI Toolkit与uGUI通过统一接口隔离，禁止同一面板同时维护两份业务状态。
- 所有按钮、标签、列表、Prefab引用和资源地址应由Editor工具生成或校验。
- UI动效使用DOTween或USS Transition，但不能影响业务计时。
- 红点只订阅RedDotPresentationState，不能自行查询背包或任务系统。
- 设置界面不暂停游戏；打开时仅屏蔽本地角色输入。

## 11. 资源和场景规范

- Addressables组：Local_Boot、Local_Common、Remote_UI、Remote_Hero、Remote_Weapon、Remote_Monster、Remote_Map01、Remote_Map02、Remote_VFX和Remote_Audio。
- 地图一和地图二禁止相互直接依赖；共享资源进入Common。
- Prefab、材质、贴图、动画和音频必须有稳定命名、Label和所有权模块。
- Standard材质导入后通过Editor工具转换URP Lit，并检查透明、法线、Emission、阴影和Instancing。
- 高频对象必须使用对象池；池中对象归还时必须清理事件、状态、粒子和异步任务。
- 地图素材确定前只创建抽象测试场景，不固定正式坐标。

## 12. 服务端与数据库规范

- Controller/Handler只做协议验证和Use Case入口，不直接编写复杂SQL。
- 领域服务和Repository接口隔离SqlSugar实现。
- 所有经济、抽奖、锻造、奖励和结算接口携带RequestId/OrderId并实现幂等。
- 货币余额和不可变流水必须同事务提交。
- 密码使用Argon2id和独立盐；日志禁止记录密码、令牌、会话密钥和完整个人信息。
- 数据迁移必须向前兼容，发布前在副本数据库执行并验证回滚策略。
- Redis、MQ或缓存故障不能造成重复发奖或资产丢失。

## 13. 日志和可观测性

- 日志至少包含TraceId、RequestId，涉及远征时增加PlayerId和ExpeditionId。
- 抽奖增加GachaOrderId，经济增加CurrencyType和LedgerId，但不记录敏感内容。
- 客户端记录启动、补丁、登录、加载、网络状态、远征和崩溃上下文。
- 服务端记录连接数、消息延迟、数据库连接池、Redis、MQ积压、幂等重放和经济守恒异常。
- 高频正常路径使用采样或聚合，禁止每帧刷日志。

## 14. 测试规范

- 每项Model规则至少包含正常、边界、非法输入和重复请求测试。
- Combat必须测试体力、无敌、霸体、飞行限制、混合三段、反击和处决。
- Expedition必须测试正常结算、死亡清理、断网闪退直接结算和重复请求。
- Economy必须测试余额守恒、库存溢出、抽奖保底和动画中断恢复。
- UI必须测试Presenter绑定、面板关闭释放、场景切换和ESC不暂停。
- 热更新必须测试版本兼容、下载中断、Hash失败、A/B回滚和KnownGood启动。
- 性能优化必须附Profiler或压测记录，不能只凭推测提交。

## 15. Git和变更控制

- 一个提交只处理一个清晰目的，避免混入无关资源或格式化。
- 不提交Library、Temp、Logs、Obj、Build和本地密钥。
- 自动生成文件必须标明生成器和源文件，禁止手工修改后被下次生成覆盖。
- 修改已确认玩法必须同时更新玩法文档、配置、测试和开发进度。
- 重要架构决策写入ADR：MVC边界、GameObject/DOTS边界、LegacyNetworkV1、远征结算、热更新和配置签名。

## 16. 模块完成定义

一个模块只有满足以下条件才算完成：

- Model、Controller、View和Infrastructure边界明确并通过依赖检查。
- 主流程和异常分支有自动化测试。
- 配置可生成并通过引用和业务校验。
- 没有未释放订阅、异步任务或Addressables句柄。
- 稳定态无新增每帧GC，性能符合当前阶段预算。
- 日志、错误码、协议、迁移和回滚说明齐全。
- UI和资源在目标分辨率下完成视觉验收。
- 开发进度文档已经同步更新。

## 17. 多模型协作规范

- DeepSeek仅提供代码草案和Unity手工UI/挂载说明，不能把无法访问仓库、编译器或Unity的输出描述为已验证结果。
- Claude负责在实际仓库中复核草案、实现功能、运行测试和修复缺陷；Codex负责跨模块集成、架构门禁、CI、服务端、数据库迁移和部署一致性。
- 同一时间只能有一个模型修改同一工作区或同一组文件。交接必须包含Git状态、改动文件、实测结果和未验证事项。
- 所有模型都必须先以当前Git仓库和权威Markdown为准；旧对话和其他模型输出不是事实来源。
- 每项用户需求完成后必须暂停并汇报，取得用户许可后才能开始下一项开发。
- 详细流程和可复制提示词见`Docs/AI/three-model-collaboration.md`。
