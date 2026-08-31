# ADR-0002：.NET 10服务端与LegacyNetworkV1边界

- 状态：已接受
- 日期：2026-08-31

## 背景

既有SimpleServer基于.NET Framework 4.6.1，正式技术基线要求受支持的.NET LTS模块化单体，同时用户要求暂不修改网络传输层。

## 决策

新服务端目标框架锁定为.NET 10 LTS。既有AES、握手、Protobuf、粘包半包、心跳、Socket.Select和协议分发的协议与运行行为冻结，通过`Naraka.Server.LegacyNetworkV1`适配边界接入。

接入前必须从旧实现生成Golden Files和兼容测试；业务模块不得直接引用旧Socket、AES或协议分发代码。

## 结果

旧源码不直接成为新业务层依赖。为.NET 10所需的兼容调整必须保持线级协议和时序行为，并由自动化测试证明。

## P0审计结果

- 已从旧.NET Framework可执行文件生成密钥请求、密钥响应、心跳、登录和玩家数据响应Golden Files。
- .NET 10兼容实现已通过AES、组帧、拆帧和不完整帧测试。
- 旧客户端与服务端存在响应协议名漂移，心跳间隔也存在确定性冲突；这些差异必须显式适配，不能覆盖历史事实。
- 旧Handler信任客户端`accountId`，因此公网部署前必须在适配边界建立认证会话并把身份注入Application命令。
- 详细证据见`Docs/LegacyNetworkV1/compatibility-audit.md`。
