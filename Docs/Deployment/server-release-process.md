# NARAKA服务端集中发布流程

本流程适用于当前2 GiB阿里云Windows开发主机。目标是把编译负载留在本地，把云端操作限制为哈希校验、顺序迁移和单进程目录切换。

## 1. 发布边界

一个大类功能及其服务端权威闭环完成后才安排集中发布，例如完整的账号概要、英雄与兵器选择、仓库、商店、锻造、抽奖、签到奖励、社交或一个战斗里程碑。UI排版、图片替换、面板开关、客户端动画和不改变应用契约的修复不发布云端。

每个发布批次必须同时包含：客户端与服务端消息、服务端Application与Infrastructure实现、数据库迁移、版本门禁、自动化测试和人工Unity闭环。

## 2. 本地正式打包

正式发布要求干净Git工作区，并默认运行服务端CI和Unity全部测试。运行前应保存场景并关闭正在占用正式工程的Unity编辑器：

```powershell
./Tools/Deployment/New-NarakaServerRelease.ps1 `
    -ReleaseId 'p1-account-summary-001' `
    -UnityEditorPath 'E:\2021.3.45f2c1\Editor\Unity.exe'
```

输出位于`artifacts/releases/<ReleaseId>`，包含：

- Host自包含ZIP；
- DatabaseMigrator自包含ZIP及迁移；
- `release-manifest.json`；
- 云端部署脚本；
- 低内存守护启动器模板；
- 本轮测试结果。

仅本地排查时可以使用`-AllowDirtyCandidate`或`-SkipUnityTestsCandidate`。这种清单的`Deployable`为`false`，云端部署脚本会拒绝，不能通过改JSON绕过门禁。

## 3. 云端集中部署

通过RDP把两个ZIP、`release-manifest.json`和`Deploy-NarakaServerRelease.ps1`复制到云端`C:\NarakaInstall\Releases\<ReleaseId>`；测试结果保留在本地即可，守护器模板只在获准安装时复制。关闭HeidiSQL和PowerShell ISE，在管理员PowerShell中运行：

```powershell
& 'C:\NarakaInstall\Releases\p1-account-summary-001\Deploy-NarakaServerRelease.ps1' `
    -ReleaseDirectory 'C:\NarakaInstall\Releases\p1-account-summary-001'
```

脚本执行前必须保证：

- `NarakaMySQL57`为Running；
- 可用内存至少400 MiB；
- 发布清单来自干净Git提交并且`Deployable=true`；
- 两个ZIP及每份迁移的SHA-256匹配；
- 当前Host目录和计划任务仍使用已验收路径。

部署期间Host会短暂停止，MySQL保持运行。脚本不会并行启动新旧Host，不会打印机器级连接串，不会删除上一版或失败版目录。

## 4. 迁移与回滚约束

DatabaseMigrator依次执行dry-run、正式迁移和重复执行验证。迁移必须扩展式且向后兼容，因为Host失败回滚不会自动逆向撤销数据库结构。删除列、重命名表或不可逆数据改写必须拆到确认所有旧Host不再需要旧结构之后的独立发布。

Host健康检查包括：

- `GET /health/live`；
- `GET /health/ready`且MySQL可达；
- 5222、8011和3306仍只存在单个loopback监听；
- `Naraka.Server.Host`进程数严格为1；
- Bootstrap版本与发布清单一致。

失败时新版目录移动到`C:\NarakaDeploy\backups`下的失败目录，上一版恢复到活动路径并重新启动。备份和失败目录的清理属于后续明确维护动作，不在发布脚本中自动递归删除。

## 5. 低内存守护器

`Tools/Deployment/Cloud/Start-NarakaServerSupervised.ps1`是下一次集中发布时安装的守护器模板，当前尚未替换云端已经工作的启动器。它不增加第二个Host或新服务，只复用计划任务现有PowerShell父进程：

- Host退出后等待30秒再重新启动；
- 可用内存低于256 MiB时等待，不制造重启风暴；
- 已存在Host时不再启动第二份；
- stdout/stderr达到默认10 MiB才轮换，并仅保留三份旧日志；
- 连接串只从机器级环境变量注入，不写入参数、清单或日志。

守护器安装会改变现有计划任务动作，必须在一个大类功能完成后的维护窗口单独取得用户许可并完成回滚验证，不能因为模板已经入库就直接操作云端。

## 6. 发布后验收

云端健康检查通过后，在本地手动启动SSH隧道，再完成该大类功能的真实Unity登录、重登和断线恢复验收。只有云端进程保持存活、内存无持续增长、客户端功能成功且日志无未处理异常，才能记录为已发布。
