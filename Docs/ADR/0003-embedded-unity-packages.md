# ADR-0003：关键Unity第三方包使用内嵌固定版本

- 状态：已接受
- 日期：2026-08-31

## 背景

当前网络无法稳定通过Git协议访问GitHub，Unity Package Manager直接解析Git依赖会导致工程无法还原。

## 决策

将VContainer 1.18.0、UniTask 2.5.11、MessagePipe 1.8.2和R3 Unity 1.3.1作为项目内嵌包保存在`NK/Packages/`，保留原包许可证、包元数据与上游提交来源。R3运行时及其必需依赖以固定版本DLL保存在`NK/Assets/Plugins/R3/`并记录SHA-256。URP锁定为Unity 2021.3.45自带的12.1.15。

## 结果

工程可在不访问GitHub的情况下恢复关键P0依赖。升级包版本必须单独提交并重新执行Unity编译和测试。
