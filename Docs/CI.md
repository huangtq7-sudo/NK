# NARAKA基础CI

## CI门禁

- `Server CI`在GitHub托管的Windows Runner上恢复、Release构建并运行全部.NET测试项目，同时把NuGet直接和传递依赖漏洞警告`NU1901`至`NU1904`视为错误。
- `Unity Client CI`在安装并激活Unity `2021.3.45f2c1`的Windows自托管Runner上依次运行EditMode和PlayMode测试。
- 两条工作流在影响各自代码的`main`推送、以`main`为目标的Pull Request和手动触发时运行。
- 基础CI不读取`.env`，不连接MySQL，不启用Unity到Host/MySQL的真实注册登录冒烟。
- 测试XML、TRX和Unity日志作为GitHub Actions Artifact保留14天。

## Unity自托管Runner

Unity中国版`2021.3.45f2c1`不能由公共Runner稳定、精确地还原，因此客户端门禁使用自托管Windows Runner。Runner必须满足：

1. GitHub Actions Runner版本不低于`2.327.1`，并具有`self-hosted`、`Windows`、`X64`和`unity-2021.3.45f2c1`标签。
2. Unity编辑器已经在Runner服务账号下激活，版本必须为`2021.3.45f2c1`。
3. 仓库变量`NARAKA_UNITY_EDITOR_PATH`指向该机器上的`Unity.exe`，例如`E:\2021.3.45f2c1\Editor\Unity.exe`。
4. Runner只用于受信任代码。工作流拒绝在外部Fork Pull Request中运行自托管任务；公开仓库不应允许不可信代码进入该Runner。

## 本机入口

从仓库根目录执行：

```powershell
./Tools/CI/Invoke-ServerTests.ps1
./Tools/CI/Invoke-UnityTests.ps1
```

服务端脚本要求`global.json`指定的.NET SDK；优先使用`-DotNetPath`、`DOTNET_EXE`或PATH中的`dotnet`，开发工作区最后回退到忽略提交的`.tools/dotnet/dotnet.exe`。

Unity脚本优先使用`-UnityEditorPath`或`UNITY_EDITOR_PATH`，当前开发机最后回退到固定基线路径。脚本在运行测试前校验编辑器版本，并检查测试结果中至少存在一项测试且没有失败或不确定项。EditMode中由环境控制的真实联网测试允许按设计跳过。

## P0边界

基础CI只覆盖确定性的编译和自动化测试。Host、MySQL、版本端点和真实客户端登录的本机联合验收仍属于单独的P0最终验收，不在无密钥CI中执行。
