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
