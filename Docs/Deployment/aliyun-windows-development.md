# 阿里云Windows开发环境部署与运维

状态：2026-09-01已完成基础部署与Unity客户端验收
适用范围：单一开发者从本地使用云端Host和MySQL，不对外提供游戏服务

## 1. 已验收拓扑

| 组件 | 运行方式 | 监听与访问边界 |
| --- | --- | --- |
| Windows主机 | Windows Server 2022，2 vCPU、2 GiB内存、40 GiB系统盘 | 仅管理入口允许受限远程访问 |
| MySQL | MySQL 5.7.26，Windows服务`NarakaMySQL57`，自动启动 | `127.0.0.1:3306` |
| NARAKA Host | .NET 10自包含`win-x64`，计划任务`NarakaServerHost`以SYSTEM启动 | `127.0.0.1:5222`和`127.0.0.1:8011` |
| 本地Unity | 正式工程`E:\NK项目\NK` | 通过SSH本地端口转发访问云端loopback端口 |

当前部署目录为`C:\NarakaDeploy\cloud-82c02c7\host`。MySQL程序位于`C:\Naraka\mysql-5.7.26-winx64`。路径不是配置或密钥；后续发布可以使用新的版本目录并保留可回滚的上一版。

## 2. 安全边界

- 不在本文、Git、日志或聊天中保存数据库密码、连接串、SSH口令或其他密钥。
- 连接串只存在于机器级环境变量`NARAKA_MYSQL_CONNECTION_STRING`中。只能检查变量是否存在，不能打印变量值。
- `3306`、`5222`和`8011`均保持loopback监听，不向公网开放。
- SSH与远程桌面只允许管理来源地址。阿里云控制台恢复可用后，仍需核对云平台侧防火墙规则并收敛管理端口来源；Windows防火墙不能替代该项复核。
- 当前没有域名和TLS终止层，因此不得把`5222`直接开放给远程HTTP客户端。开发客户端必须经过SSH隧道。

安全检查机器级变量时只运行：

```powershell
$connectionPresent = -not [string]::IsNullOrWhiteSpace(
    [Environment]::GetEnvironmentVariable(
        'NARAKA_MYSQL_CONNECTION_STRING',
        'Machine'))
$connectionPresent
```

预期只输出`True`，不得执行或截图任何会显示变量值的命令。

## 3. 本地建立SSH隧道

在本地Windows PowerShell中运行，并保持窗口开启：

```powershell
ssh -N `
    -o ExitOnForwardFailure=yes `
    -o ServerAliveInterval=30 `
    -o ServerAliveCountMax=3 `
    -L 127.0.0.1:5222:127.0.0.1:5222 `
    -L 127.0.0.1:8011:127.0.0.1:8011 `
    <SSH_USER>@<SERVER_PUBLIC_IP>
```

`<SSH_USER>`和`<SERVER_PUBLIC_IP>`只在本地命令中替换，不写回仓库。隧道窗口空白且不退出属于正常状态。

本地验收：

```powershell
Test-NetConnection 127.0.0.1 -Port 5222
Test-NetConnection 127.0.0.1 -Port 8011
Invoke-RestMethod -Uri 'http://127.0.0.1:5222/health/ready'
Invoke-RestMethod -Uri 'http://127.0.0.1:5222/bootstrap/config-version'
```

预期两个TCP测试均为`True`；就绪探针返回`MySQL reachable`；版本端点返回`p0-config-1`、客户端范围`0.1`至`0.1`和`LegacyNetworkV1`。

## 4. 云端日常检查

以下命令在远程Windows主机执行：

```powershell
Get-Service -Name 'NarakaMySQL57' |
    Select-Object Name, Status, StartType

Get-ScheduledTask -TaskName 'NarakaServerHost' |
    Select-Object TaskName, State

Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue |
    Where-Object LocalPort -In 3306,5222,8011 |
    Select-Object LocalAddress, LocalPort, OwningProcess

Invoke-RestMethod -Uri 'http://127.0.0.1:5222/health/live'
Invoke-RestMethod -Uri 'http://127.0.0.1:5222/health/ready'
Invoke-RestMethod -Uri 'http://127.0.0.1:5222/bootstrap/config-version'
```

Host日志位于当前发布目录：

- `host.background.stdout.log`
- `host.background.stderr.log`

正常状态下错误日志为空。排查时只截取必要尾部内容，并先确认其中不含敏感数据。

## 5. 已完成验收证据

- 部署基线提交：`82c02c7debec9d399bd88df0c77c98cce491945d`。
- DatabaseMigrator包SHA-256：`A19F1F195E13BD9CFDD0DEDA0532FB0D66CF292A2D305C07578F19A590AC97FC`。
- Host包SHA-256：`65C4AE11121BB48E93B7AFE4CA52BBA7AB2713AD71B6ED7573678462F4A4A6CE`。
- 迁移`0001_p0_identity.sql`的dry-run、首次执行和重复执行退出码均为0；已核对3张P0表和迁移记录。
- `/health/live`、`/health/ready`、版本端点和8011 TCP监听均通过。
- 重启后MySQL服务、Host计划任务、三个loopback监听和健康检查均恢复。
- 正式Unity工程经SSH隧道完成版本预检、真实注册、真实登录和空大厅跳转，Unity运行无错误。

## 6. 当前限制与后续门禁

- 这是开发环境，不具备生产级高可用、独立数据库、自动备份、集中日志、告警、TLS域名或多环境隔离。
- 2 GiB内存余量有限，不在本机安装非必要服务；是否扩容以实际监控数据决定。
- 认证会话仍为单进程内存状态，Host重启后客户端需要重新登录。
- 本轮验收账号仍在`NK`库中。删除属于数据变更，必须由用户明确许可后使用只针对该测试账号的命令执行。
- 每次升级必须单独核对构建来源与SHA-256，先执行迁移dry-run，再做健康检查和Unity闭环验收；不得直接覆盖唯一可用版本。
