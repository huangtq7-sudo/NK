# LegacyNetworkV1 兼容审计

审计日期：2026-08-31  
状态：传输行为已冻结并建立首批Golden Files；尚未接入正式服务端Host

## 1. 审计范围

- 旧客户端：`E:\ClientProject\Assets\Scripts`
- 旧服务端：`D:\培训项目\7.21net\SimpleServer`
- 旧运行时：.NET Framework 4.6.1
- 旧序列化库：protobuf-net 2.4.4

本轮只读取旧目录，没有修改旧客户端、旧服务端或数据库代码。新代码只建立字节兼容编解码器、协议目录、Golden Files和自动化测试。

## 2. 冻结的线格式

```text
Int32LE PayloadLength
UInt16LE ProtocolNameUtf8Length
UTF8 ProtocolName
AES Encrypted Protobuf Payload
```

- `PayloadLength`包含协议名长度前缀、协议名和加密后的protobuf内容，不包含自身4字节。
- 协议名来自`ProtocolEnum.ToString()`，接收端用名称反射消息类型。
- TCP粘包通过读取首个完整帧后递归处理剩余字节解决。
- 旧服务端监听`127.0.0.1:8011`；该地址是审计事实，不是正式部署配置。

## 3. 冻结的握手、加密和心跳

- 建连后客户端先发送`MsgSecret`。
- `MsgSecret`请求和响应都使用公共口令加密；客户端收到响应后切换到会话口令。
- 旧实现使用固定IV、固定Salt、`PasswordDeriveBytes`、256位密钥、CBC和PKCS7。
- 这些算法只允许存在于`LegacyNetworkV1`兼容边界，新协议不得继续使用。
- 旧客户端心跳发送间隔为300秒；旧服务端以30秒为基准，120秒无心跳即断开，二者存在确定性冲突。

## 4. 协议目录漂移

客户端目录只包含`0–11`：

| 值 | 协议 |
|---:|---|
| 0 | None |
| 1 | MsgSecret |
| 2 | MsgPing |
| 3 | MsgTest |
| 4 | MsgRegister |
| 5 | MsgLogin |
| 6 | MsgLoadPlayerData |
| 7 | MsgSavePlayerData |
| 8 | MsgLoadInventory |
| 9 | MsgSaveInventory |
| 10 | MsgLoadTask |
| 11 | MsgSaveTask |

服务端额外包含：

| 值 | 协议 |
|---:|---|
| 12 | MsgPlayerDataResponse |
| 13 | MsgInventoryResponse |
| 14 | MsgTaskResponse |
| 15 | MsgLoadConfig |
| 16 | MsgConfigResponse |
| 17 | MsgShopPurchase |
| 18 | MsgTaskReward |

同时，旧客户端的三个响应DTO把`ProtocolType`分别设置为对应请求协议；旧服务端则发送独立响应名。因此服务端下发`MsgPlayerDataResponse`、`MsgInventoryResponse`或`MsgTaskResponse`时，当前旧客户端无法通过`ProtocolEnum.Parse`完成解码。

该漂移已作为测试中的显式兼容事实保存。适配边界已选择冻结客户端能够识别的请求名作为三个响应的线协议名和嵌入协议值；旧源码和历史服务端目录保持不变。

## 5. 公网部署阻断项

以下问题不改变旧线格式，但阻止把旧服务端直接暴露到公网：

1. 登录成功后没有把账号身份绑定到`ClientSocket`；玩家数据、背包、任务和经济消息直接信任客户端传入的`accountId`。
2. 注册和登录使用明文密码字段，数据库也按明文比较，不符合项目Argon2id规范。
3. 数据库连接信息硬编码在旧程序集源码中，数据库异常日志还会输出完整连接串。
4. 旧公共口令和会话口令是服务端全局值，不是每连接独立密钥。
5. 帧长度缺少可靠的负数、超大值和恶意输入保护。
6. 存档、背包和经济更新没有完整的事务、幂等键和服务端权威校验。

因此阿里云阶段只能部署新的Host与安全适配边界，不能原样开放旧`SimpleServer.exe`和MySQL端口。

## 6. Golden Files

`Server/tests/Naraka.Server.LegacyNetworkV1.Tests/Golden/legacy-wire-v1.json`由旧版已编译可执行文件生成，包含：

- 密钥请求；
- 密钥响应；
- 心跳；
- 登录请求；
- 玩家数据响应。

每条向量包含旧protobuf字节、AES密文和完整TCP包。测试验证.NET 10兼容实现的加密、解密、组帧、拆帧与旧可执行文件完全一致。

Golden Files只使用专用测试口令，不包含本机数据库密码、云服务器凭据或生产密钥。

## 7. 下一步

1. 把已完成的认证会话守卫和响应别名解析器接入真实Socket消息分发。
2. 将protobuf-net 2.4.4 DTO隔离在LegacyNetworkV1，映射到Application层命令和响应。
3. 处理旧客户端300秒心跳与服务端120秒超时的冲突，并补充时序测试。
4. 本机完成客户端版本检查、登录和空大厅冒烟后，再准备阿里云部署。
