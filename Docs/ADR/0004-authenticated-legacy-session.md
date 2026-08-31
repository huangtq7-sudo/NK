# ADR-0004：LegacyNetworkV1认证会话边界

- 状态：已接受
- 日期：2026-08-31

## 背景

旧SimpleServer在登录后仍直接信任玩家数据、背包、任务和经济消息中的客户端`accountId`。旧客户端与服务端对三种响应协议名也存在漂移。原样部署会允许账号身份伪造，并导致响应无法被冻结客户端解析。

## 决策

- 每个连接由服务端生成不可空的`ConnectionId`。
- 登录成功后由`IAuthenticatedSessionRegistry`把连接绑定到唯一`AccountId`。
- 旧消息中的`accountId`只作为一致性检查；与会话不符或连接未认证时立即拒绝。
- 密码使用Argon2id，当前参数为64MiB内存、3轮、并行度2、32字节Salt和32字节Hash；参数与账号记录一起保存。
- Application只依赖`IPasswordHasher`、Repository和会话抽象；Argon2与SqlSugar实现留在Infrastructure。
- `MsgPlayerDataResponse`、`MsgInventoryResponse`和`MsgTaskResponse`对冻结客户端分别使用`MsgLoadPlayerData`、`MsgLoadInventory`和`MsgLoadTask`作为线协议名及嵌入协议值。

## 结果

- 客户端不能再决定权威账号身份。
- 历史协议目录漂移仍保留在审计清单，适配器显式解决而不是篡改旧源码。
- P0会话暂存于单进程内存，进程重启后要求重新登录；未来只有在多实例容量数据证明需要时才引入共享会话存储。
- 当前只完成Use Case、会话守卫和协议解析器；真实Socket消息分发接入仍是下一项工作。
