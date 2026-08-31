# 本机MySQL接入说明

状态：本机迁移与真实连接验证已完成

## 安全边界

- 连接串只从`NARAKA_MYSQL_CONNECTION_STRING`环境变量读取。
- `.env`和本地配置已被Git忽略，真实密码不得写入仓库、文档或聊天记录。
- 健康检查只返回配置缺失、配置无效、错误号或超时，不输出连接串和驱动异常消息。
- MySQL只监听本机或云服务器内网；安全组和Windows防火墙不得向公网开放`3306`。
- 客户端永远不直接连接MySQL。

## 当前实现

- 本机开发目标为MySQL 5.7.26、数据库`NK`、用户`NK`。
- `MySqlConnector 2.6.2`负责异步连接和`SELECT 1`真实健康检查。
- `SqlSugarCore 5.1.4.217`只在Infrastructure内创建客户端，Application和Domain不引用SqlSugar类型。
- 显式覆盖`SQLitePCLRaw.lib.e_sqlite3 2.1.13`，避免SqlSugar传递依赖中的高危`2.1.11`版本。
- 连接超时在健康检查配置中最多为5秒。

## 本机准备

1. 保持本机MySQL 5.7.26仅用于开发兼容验证；云端生产库使用仍受安全维护的MySQL版本。
2. 确认数据库`NK`和最小权限用户`NK`存在。
3. 把`.env.example`复制为不会提交的`.env`，只在本机填入真实密码。
4. 把连接串注入当前进程的`NARAKA_MYSQL_CONNECTION_STRING`环境变量；不要把密码写进命令历史。
5. 运行`Naraka.Server.DatabaseMigrator --dry-run`检查迁移，再执行正式迁移。
6. 启动Host并调用`/health/ready`验证数据库探针。

初始迁移`Server/migrations/0001_p0_identity.sql`只创建`schema_migrations`、`accounts`和`player_profiles`，不会删除旧表。账号密码只预留Argon2id哈希、独立Salt和参数字段，不保存明文密码。

## 验证结果

- `0001_p0_identity.sql`已成功执行，并再次幂等执行通过。
- 实际核对到3张P0表，`schema_migrations`中存在版本`0001`。
- Host `/health/live`返回200。
- `/health/ready`返回200，数据库状态为`MySQL reachable`，LegacyNetworkV1状态为已监听8011端口。
- 真实连接串保存在已忽略的本机`.env`中，Git仓库只保留占位模板。
