# ADR-0005：登录前执行配置版本兼容性检查

- 状态：已接受
- 日期：2026-08-31

## 背景

P0退出条件要求客户端启动后先检查版本，再允许注册、登录并进入空大厅。既有`LegacyNetworkV1`传输层处于冻结状态，本轮不得为版本检查修改其握手、AES、Protobuf、心跳或Socket分发行为。

## 决策

服务端Host提供`GET /bootstrap/config-version`，返回ConfigVersion、MinimumClientVersion、MaximumClientVersion和ProtocolVersion。客户端以独立Bootstrap模块化MVC完成获取与纯Model兼容性判断，通过`IStartupReadiness`阻塞Account Controller，只有检查通过后才允许注册和登录。

本机开发只接受loopback HTTP地址，远端Bootstrap地址必须使用HTTPS。该端点仅承担P0兼容性预检，不替代P5正式配置发布中的签名、SHA-256、A/B缓存与KnownGood回滚。

跨模块的`AccountAuthenticatedEvent`由MessagePipe实现的`IDomainEventBus`传递；持续展示状态通过R3实现的`ReactiveState<T>`发布。View仍只依赖既有只读状态接口，顶层架构保持模块化MVC。

## 结果

网络传输层保持不变；配置或协议不兼容、客户端版本过旧/过新、端点超时或不可达时，登录入口明确阻塞并向用户显示可重试原因。Bootstrap Model、Controller、MessagePipe接缝和启动场景装配均纳入Unity自动化测试。
