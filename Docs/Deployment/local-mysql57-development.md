# 本机轻量 MySQL 5.7 开发实例

## 用途与边界

本实例只用于《NARAKA》本机数据库迁移、服务端集成和冒烟验收，不替代云端开发库，也不是生产部署。

- 版本：MySQL Community Server 5.7.26 x64。
- 安装目录：`E:\NarakaLocal\mysql57`，不在Git仓库内。
- 数据库：`NK`。
- 监听：仅`127.0.0.1:3306`，不向局域网或公网开放。
- 轻量参数：`max_connections=20`、`innodb_buffer_pool_size=128M`、`performance_schema=OFF`。
- 运行方式：当前Windows用户按需启动的隐藏进程，不注册Windows服务，不随开机常驻。
- 凭据：应用连接串只保存在当前Windows用户的`NARAKA_MYSQL_CONNECTION_STRING`环境变量中；禁止打印、写入仓库、日志或命令行参数。

## 手动启动与停止

在本机普通PowerShell中，从仓库根目录执行：

```powershell
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\Tools\Database\Start-NarakaLocalMySql57.ps1
```

使用完毕后停止：

```powershell
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\Tools\Database\Stop-NarakaLocalMySql57.ps1
```

脚本只会启动或停止`E:\NarakaLocal\mysql57`中的精确`mysqld.exe`，不会操作其他MySQL实例。
电脑重启后本实例保持停止，需要再次手动启动。

## 安全状态检查

以下检查不会显示连接串：

```powershell
Get-NetTCPConnection -State Listen -LocalPort 3306 |
    Select-Object LocalAddress, LocalPort, OwningProcess

Get-Process -Name mysqld |
    Select-Object ProcessName, Id,
        @{Name='WorkingSetMiB';Expression={[math]::Round($_.WorkingSet64 / 1MB, 1)}}

$present = -not [string]::IsNullOrWhiteSpace(
    [Environment]::GetEnvironmentVariable(
        'NARAKA_MYSQL_CONNECTION_STRING',
        'User'
    )
)

[PSCustomObject]@{ UserConnectionVariablePresent = $present }
```

预期监听地址必须是`127.0.0.1`。如果3306已被其他程序占用，启动脚本会停止并报错，不会替换或关闭该程序。

## 迁移和远征冒烟

新PowerShell会话中先把用户级连接串注入当前进程，但不要输出变量内容：

```powershell
$env:NARAKA_MYSQL_CONNECTION_STRING =
    [Environment]::GetEnvironmentVariable(
        'NARAKA_MYSQL_CONNECTION_STRING',
        'User'
    )

& .\.tools\dotnet\dotnet.exe run `
    --project Server\tools\Naraka.Server.DatabaseMigrator `
    --configuration Release `
    --no-restore

& .\.tools\dotnet\dotnet.exe run `
    --project Server\tools\Naraka.Server.ExpeditionSmoke `
    --configuration Release `
    --no-restore

$env:NARAKA_MYSQL_CONNECTION_STRING = $null
```

`Naraka.Server.ExpeditionSmoke`使用随机测试账号，并在结束时删除测试数据。2026-10-08的验收结果为：

- 迁移0001至0010连续执行两次成功，共核对33张表。
- 死亡清理、原子结算、库存溢出转邮件、货币流水和幂等重放通过。
- 完整服务端CI：387/387通过，Release构建0警告、0错误。
