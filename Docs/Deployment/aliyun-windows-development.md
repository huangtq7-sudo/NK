# 阿里云Windows开发环境部署与运维

状态：2026-09-02已按Windows Server 2016重装后的实际环境完成在线验收
适用范围：单一开发者从本地使用云端Host和MySQL，不对外提供游戏服务

## 1. 当前已验收拓扑

| 组件 | 运行方式 | 监听与访问边界 |
| --- | --- | --- |
| Windows主机 | Windows Server 2016 Datacenter，Build 14393，2 vCPU、2 GiB内存、40 GiB系统盘 | Windows防火墙只允许公网TCP 3389和22 |
| MySQL | MySQL 5.7.26，Windows服务`NarakaMySQL57`，自动启动 | `127.0.0.1:3306` |
| NARAKA Host | .NET 10自包含`win-x64`，计划任务`NarakaServerHost`以SYSTEM启动 | `127.0.0.1:5222`和`127.0.0.1:8011` |
| SSH管理入口 | Win32-OpenSSH Server 10.0.0.0p2 Preview，服务`sshd`自动启动 | TCP 22；只允许专用公钥，禁止密码登录和交互Shell |
| 本地Unity | 正式工程`E:\NK项目\NK` | 手动启动SSH本地端口转发后访问本机`127.0.0.1:5222/8011` |
| 数据库GUI | HeidiSQL 12.21 Portable，仅在云端RDP会话中手动打开 | 只连接`127.0.0.1:3306`，不得对外开放数据库端口 |

当前Host部署目录为`C:\NarakaDeploy\cloud-82c02c7\host`，MySQL程序位于`C:\Naraka\mysql-5.7.26-winx64`，HeidiSQL位于`C:\NarakaTools\HeidiSQL-12.21`。这些路径不是配置或密钥；后续发布可以使用新的版本目录并保留可回滚的上一版。

云端PowerShell ISE已经安装但不会自动启动。云主机不安装Visual Studio、Unity、容器、Redis、MQ或其他非必要服务，以控制2 GiB主机的内存占用。

## 2. 安全边界

- 不在本文、Git、日志、聊天或截图中保存数据库密码、连接串、SSH口令、私钥或其他密钥。
- 连接串只存在于机器级环境变量`NARAKA_MYSQL_CONNECTION_STRING`中。只能检查变量是否存在，不能打印变量值。
- `3306`、`5222`和`8011`均保持loopback监听，不向公网开放。
- Windows防火墙Domain、Private和Public三个Profile均已启用，默认入站阻止、出站允许；启用前曾使用10分钟计划任务保护RDP回滚，并在新RDP连接成功后取消回滚。
- 当前仅有`NK-Allow-RDP-3389`和`NK-Allow-SSH-22`两条公网入站允许规则。为避免开发者动态公网地址变化再次锁死管理入口，这两条规则当前允许任意来源；这是单开发者开发环境的明确风险折中，不是生产安全基线。
- SSH禁止密码和键盘交互认证，只允许既有ED25519专用公钥。服务器端同时用`AllowTcpForwarding local`、`PermitOpen`和公钥`permitopen`双重限制目标只能是`127.0.0.1:5222`与`127.0.0.1:8011`；禁止Agent转发、远程转发、Tunnel、X11和TTY。
- 当前没有域名和TLS终止层，因此不得把`5222`直接开放给远程HTTP客户端。开发客户端必须经过SSH隧道。
- 阿里云平台侧防火墙仍需在控制台恢复可用后复核；正式发布前必须改为更严格的管理入口、独立密钥托管和TLS方案。

安全检查机器级变量时只运行：

```powershell
$connectionPresent = -not [string]::IsNullOrWhiteSpace(
    [Environment]::GetEnvironmentVariable(
        'NARAKA_MYSQL_CONNECTION_STRING',
        'Machine'))
$connectionPresent
```

预期只输出`True`，不得执行或截图任何会显示变量值的命令。

## 3. 云端服务生命周期

- `NarakaMySQL57`为自动启动的Windows服务。
- `sshd`为自动启动的Windows服务。
- `NarakaServerHost`为SYSTEM身份的开机计划任务。
- 关闭本地电脑、断开本地网络、停止本地SSH隧道、关闭RDP窗口或注销RDP用户，都不会停止云端MySQL和Host。
- 关闭RDP窗口只断开显示会话，HeidiSQL或ISE可能仍留在会话中占用内存。维护完成后应关闭GUI工具并注销RDP会话。
- 只有关闭/重启云实例、服务自身失败或云平台故障才会中断云端组件。Host重启后认证会话丢失，客户端需要重新登录。

云端日常只读检查：

```powershell
Get-Service -Name 'NarakaMySQL57','sshd' |
    Select-Object Name, Status, StartType

Get-ScheduledTask -TaskName 'NarakaServerHost' |
    Select-Object TaskName, State

Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue |
    Where-Object LocalPort -In 22,3306,5222,8011 |
    Select-Object LocalAddress, LocalPort, OwningProcess

Invoke-RestMethod -Uri 'http://127.0.0.1:5222/health/live'
Invoke-RestMethod -Uri 'http://127.0.0.1:5222/health/ready'
Invoke-RestMethod -Uri 'http://127.0.0.1:5222/bootstrap/config-version'
```

Host日志位于当前发布目录：

- `host.background.stdout.log`
- `host.background.stderr.log`

正常状态下错误日志为空。排查时只截取必要尾部内容，并先确认其中不含敏感数据。

## 4. 本地手动SSH隧道

本地不再自动建立隧道。计划任务`NarakaCloudTunnel`为无触发器、无自动重试的按需任务，动作直接运行Windows自带`ssh.exe`。桌面提供两个快捷方式：

- `NARAKA-启动云隧道`
- `NARAKA-停止云隧道`

使用流程：

1. 确认本地已经联网，并登录配置任务的Windows用户。
2. 双击“启动云隧道”，等待约10秒。
3. 验证本地5222和8011后再打开Unity。
4. 本地断网、睡眠或SSH失效后，先双击“停止云隧道”，再双击“启动云隧道”。
5. 本地关机后隧道必然结束；下次登录后仍需手动启动。

本地验收：

```powershell
Test-NetConnection 127.0.0.1 -Port 5222
Test-NetConnection 127.0.0.1 -Port 8011
Invoke-RestMethod -Uri 'http://127.0.0.1:5222/health/ready'
Invoke-RestMethod -Uri 'http://127.0.0.1:5222/bootstrap/config-version'
```

预期两个TCP测试均为`True`；就绪探针返回`MySQL reachable`；版本端点返回`p0-config-1`、客户端范围`0.1`至`0.1`和`LegacyNetworkV1`。

普通云主机重启不应修改主机密钥。只有重装系统或明确更换SSH主机密钥时，才允许通过RDP读取新ED25519指纹、在本地隔离验证后更新`known_hosts`；禁止遇到错误就盲目删除主机记录。

## 5. 手动使用数据库GUI

1. 通过RDP登录云服务器。
2. 启动`C:\NarakaTools\HeidiSQL-12.21\heidisql.exe`。
3. 使用已授权的数据库账户连接`127.0.0.1:3306`并选择数据库`NK`。密码只能在云服务器界面输入，不能复制到聊天或文档。
4. 日常查看不应使用root；没有GUI专用只读账户时，必须先取得用户许可再创建。
5. 使用完毕后关闭HeidiSQL；若打开过PowerShell ISE也一并关闭，然后注销RDP会话以释放内存。

## 6. 已完成验收证据

- 部署基线提交：`82c02c7debec9d399bd88df0c77c98cce491945d`。
- DatabaseMigrator包SHA-256：`A19F1F195E13BD9CFDD0DEDA0532FB0D66CF292A2D305C07578F19A590AC97FC`。
- Host包SHA-256：`65C4AE11121BB48E93B7AFE4CA52BBA7AB2713AD71B6ED7573678462F4A4A6CE`。
- Win32-OpenSSH MSI SHA-256：`DDEC9C53864280759CF9F74791CEFD387100E3946AA849A1C138A4ED1B96B7D9`，Authenticode签名有效且签名者为Microsoft Corporation。
- MySQL迁移`0001_p0_identity.sql`的dry-run、首次执行和重复执行退出码均为0；已核对`accounts`、`player_profiles`、`schema_migrations`和唯一`0001`记录。
- `/health/live`、`/health/ready`、版本端点和8011 TCP监听通过；数据库返回`MySQL reachable`。
- OpenSSH配置语法通过，公钥认证退出码为0，密码认证关闭，公钥和全局配置都只允许转发5222与8011。
- 本地临时隧道与最终按需任务均验证5222、8011、Bootstrap、数据库和LegacyNetworkV1可达。
- 本地曾验证循环启动器能在受控终止SSH后约41秒恢复，但用户随后明确改为手动任务；当前事实以无触发器的按需任务为准。
- Windows Server 2016重装后的最终云端/本地重启验收由用户明确免除，并按用户决定视为完成；不得将其描述为实际执行过的重启测试。

## 7. 当前限制与后续门禁

- 这是开发环境，不具备生产级高可用、独立数据库、自动备份、集中日志、告警、TLS域名或多环境隔离。
- Windows Server 2016和Win32-OpenSSH Preview属于当前开发期选择，正式发布前必须重新评估受支持生命周期和安全更新策略。
- 2 GiB内存余量有限，不在云主机安装非必要服务；HeidiSQL和ISE只在需要时打开，用完关闭并注销。是否扩容以实际监控数据决定。
- 认证会话仍为单进程内存状态，Host重启后客户端需要重新登录。
- 本轮验收账号是否仍在`NK`库中应以数据库实际查询为准；删除属于数据变更，必须由用户明确许可后使用只针对目标测试账号的命令执行。
- 本地自动隧道已经取消。开始Unity联调前必须人工启动，断网恢复后也必须人工停止并重新启动。
- 每次升级必须单独核对构建来源与SHA-256，先执行迁移dry-run，再做健康检查和Unity闭环验收；不得直接覆盖唯一可用版本。
