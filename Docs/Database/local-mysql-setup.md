# 本机MySQL接入说明

状态：代码接缝已完成；本机MySQL服务尚未检测到

## 安全边界

- 连接串只从`NARAKA_MYSQL_CONNECTION_STRING`环境变量读取。
- `.env`和本地配置已被Git忽略，真实密码不得写入仓库、文档或聊天记录。
- 健康检查只返回配置缺失、配置无效、错误号或超时，不输出连接串和驱动异常消息。
- MySQL只监听本机或云服务器内网；安全组和Windows防火墙不得向公网开放`3306`。
- 客户端永远不直接连接MySQL。

## 当前实现

- `MySqlConnector 2.6.2`负责异步连接和`SELECT 1`真实健康检查。
- `SqlSugarCore 5.1.4.217`只在Infrastructure内创建客户端，Application和Domain不引用SqlSugar类型。
- 显式覆盖`SQLitePCLRaw.lib.e_sqlite3 2.1.13`，避免SqlSugar传递依赖中的高危`2.1.11`版本。
- 连接超时在健康检查配置中最多为5秒。

## 本机准备

1. 安装或启动受支持的MySQL 8.x Windows服务。
2. 创建仅供本机开发使用的`naraka_dev`数据库和最小权限账号。
3. 把`.env.example`复制为不会提交的`.env`，只在本机填入真实连接串。
4. 通过启动脚本把`.env`注入进程环境；应用本身不读取仓库中的明文秘密文件。
5. 执行数据库迁移后调用`/health/ready`验证数据库探针。

当前尚未创建业务表或迁移，因为需要先确认本机MySQL版本、现有数据库名称以及是否保留旧表数据。
