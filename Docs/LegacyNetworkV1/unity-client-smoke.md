# Unity LegacyNetworkV1客户端冒烟

状态：Unity 2021.3→.NET 10 Host→MySQL真实注册登录已验证

## 安全约束

- Host只从`NARAKA_MYSQL_CONNECTION_STRING`读取数据库连接串。
- 冒烟账号必须使用`p0_`前缀，密码只通过进程环境变量注入。
- `.env`、真实密码、会话密钥和测试结果不提交。
- 冒烟完成后必须运行清理模式；清理工具会拒绝删除不以`p0_`开头的账号。

## 流程

1. 把本机`.env`连接串注入Host进程，启动`Naraka.Server.Host`。
2. 为Unity批处理进程设置`NARAKA_RUN_LEGACY_CLIENT_SMOKE=1`、`NARAKA_SMOKE_USERNAME`和`NARAKA_SMOKE_PASSWORD`。
3. 只运行`Naraka.P0.Tests.LegacyClientLiveSmokeTests`。
4. 确认测试完成握手、注册和登录，且`AccountId > 0`。
5. 设置`NARAKA_ACCOUNT_SMOKE_CLEANUP_USERNAME`，运行`Naraka.Server.AccountSmoke --no-build --no-restore`删除临时账号。

常规EditMode回归不设置联网开关，因此该测试显示为跳过，不会意外写入本机数据库。
