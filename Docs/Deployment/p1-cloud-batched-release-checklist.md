# P1云端批量部署检查表

状态：**尚未执行。** 本文档只描述部署前需要完成的步骤，本轮未对云服务器或云端NK库做任何修改。

依据：[ADR-0008](../ADR/0008-batched-low-memory-cloud-releases.md)（按大类集中发布）、[ADR-0012](../ADR/0012-red-dot-prefix-tree-and-server-capabilities.md)（能力声明与兼容模式）。

## 0. 当前差距

| 项 | 云端现状 | 本地现状 |
| --- | --- | --- |
| Host应用协议 | 0–20（P1.1-A） | 0–20 与 21–64 |
| 数据库迁移 | `0001`、`0002` | `0001`–`0009` |
| 表数量 | 4 | 27 |
| `serverCapabilities` | 不返回该字段 | 返回`Implemented`（12项） |
| Bootstrap `configVersion` | `p0-config-1` | `p1-config-1` |
| 生成配置 | 无 | `p1-config-5e52cf730692` |

完整P1客户端已使用`p1-config-1`，会在版本预检阶段拒绝当前仍返回`p0-config-1`的云端。能力字段缺失时的兼容模式只在版本门禁匹配时生效，不能绕过ConfigVersion。完成本次集中部署后才能用当前P1客户端登录并验收真实业务。

## 1. 部署前必须完成（尚未完成的项标注为待办）

- [ ] 人工在Unity Play Mode验收十个界面的视觉效果与交互（本轮自动化已通过，视觉未验收）
- [ ] 用户与Codex复核本轮全部改动
- [ ] 将工作区改动整理为**干净的Git提交**（本轮未提交任何内容，需要用户明确授权）
- [x] 服务端Release构建通过
- [x] 服务端CI全绿：352/352
- [x] 配置`--check`一致性门禁通过
- [x] Unity EditMode 281通过/0失败/1跳过、PlayMode 3/3
- [x] 迁移`0001`–`0009` dry-run全部通过
- [ ] 备份云端NK库（部署前的最后一步，需要用户执行）

## 2. 迁移

本次要新增执行的迁移：`0003`、`0004`、`0005`、`0006`、`0007`、`0008`、`0009`。

全部为**新增表**，没有任何`ALTER TABLE`、`DROP`或数据重写，因此符合ADR-0008的扩展优先策略：Host启动失败时可以直接恢复上一版二进制，数据库不需要回滚。

`0001`与`0002`已经在云端执行过，本轮**未做任何改动**（有测试锁定其内容）。迁移器按`schema_migrations`跳过它们。

新增的23张表：

```
account_profile          account_grants           currency_ledger
idempotency_records      account_inventory        account_equipment
account_shop_purchases   account_weapons          account_gacha
gacha_orders             gacha_order_results
account_signin           account_signin_claims    account_reward_claims
account_achievements     account_achievement_state
account_reddot           account_friends          account_friend_requests
account_blocks           chat_conversations       chat_messages
chat_read_positions
```

迁移器验收会检查：27张表存在、`0001`–`0009`全部记录在`schema_migrations`、每个账号都有进度行。

## 3. 首次登录会发生什么

Host升级后，每个账号**第一次登录**会触发`EnsureProvisionedAsync`：在一个事务里发放StarterGrant（铜币/幻丝/金币各1000）并写入不可变流水。

- 发放是**累加**而不是覆盖，因此现有账号的余额不会被抹平。
- 由`account_grants`的`(account_id, grant_key)`主键保证每个账号只发一次。
- 详见 [ADR-0011](../ADR/0011-starter-grant-and-currency-ledger.md)。

**部署前请确认这是预期行为**：所有现有测试账号都会各多出1000铜币、1000幻丝、1000金币。

## 4. 版本门禁

- Bootstrap的`configVersion`随完整P1应用协议提升为`p1-config-1`，客户端场景与默认值同步更新。该门禁落实ADR-0008，禁止旧客户端在不理解完整P1契约时继续进入。
- Host升级后返回`serverCapabilities`，当前P1客户端据此启用全部P1功能。旧客户端会因ConfigVersion不匹配停在版本预检，这是预期保护，不是兼容性回归。
- 生成配置有自己的版本号`p1-config-5e52cf730692`，与Bootstrap门禁互不影响。
- 生成配置必须随Host一起打进发布包：Host启动时会加载并校验它，缺失或损坏时**直接拒绝启动**而不是带着半份配置对外服务。

## 5. 部署顺序（ADR-0008固定流程）

1. 资源与GUI检查
2. 停止唯一Host进程
3. 解压新版本目录
4. 迁移dry-run
5. 首次迁移
6. 重复执行迁移，验证幂等
7. 备份旧目录
8. 原子切换到新目录
9. 健康检查（`/health/live`、`/health/ready`、`/config/version`、`/bootstrap/config-version`）
10. 单进程与loopback监听检查

任何Host验收失败都恢复上一版目录，失败版本保留供排查，不直接删除。

## 6. 部署后验证

- [ ] `/bootstrap/config-version`返回`serverCapabilities`且包含12项
- [ ] `/config/version`返回`p1-config-5e52cf730692`
- [ ] 用真实客户端注册一个新账号，确认三种货币各为1000
- [ ] 用一个既有账号登录，确认余额是**原值加1000**而不是1000
- [ ] 再次登录同一账号，确认**没有第二次发放**
- [ ] 逐个打开十个大厅入口，确认都能取到服务端数据
- [ ] 用当前P1客户端完成版本预检与登录；确认旧`p0-config-1`客户端被版本门禁阻止

## 7. 本轮明确未做的事

- 未通过RDP、SSH或脚本修改云服务器
- 未重启或重新部署阿里云Host
- 未对云端NK库执行任何迁移
- 未提交或推送Git
- 未读取、输出或记录任何`.env`、密码、密钥或连接串

以上每一项都需要用户单独授权后才能执行。
