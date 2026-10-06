# ADR-0003：关键Unity第三方包使用内嵌固定版本

- 状态：已接受
- 日期：2026-08-31

## 背景

当前网络无法稳定通过Git协议访问GitHub，Unity Package Manager直接解析Git依赖会导致工程无法还原。

## 决策

将VContainer 1.18.0、UniTask 2.5.11、MessagePipe 1.8.2和R3 Unity 1.3.1作为项目内嵌包保存在`NK/Packages/`，保留原包许可证、包元数据与上游提交来源。R3运行时及其必需依赖以固定版本DLL保存在`NK/Assets/Plugins/R3/`并记录SHA-256。URP锁定为Unity 2021.3.45自带的12.1.15。

## 结果

工程可在不访问GitHub的情况下恢复关键P0依赖。升级包版本必须单独提交并重新执行Unity编译和测试。

## 2026-10-05 补充：第三方美术素材按同一条规则固定来源

本 ADR 原本只覆盖代码依赖（内嵌包与固定版本 DLL）。正式暮影妖狼接入时发现
美术素材需要同样的保证：**来源、版本与哈希必须写下来，否则半年后没人说得清
工程里这只狼是从哪个包的哪一版导出来的。**

因此把规则扩展到从 `.unitypackage` 导入的第三方美术素材：

| 项 | 值 |
| --- | --- |
| 素材包 | `Polygonal - Creatures Pack v1.0`（Meshtint Studio） |
| 路径 | `E:\素材\30 Unity Asset Polygonal - Creatures Pack v1.0\Unity Asset Polygonal - Creatures Pack v1.0.unitypackage` |
| 字节数 | 38,110,997 |
| SHA-256 | `70b6b6a423bac0080227f816c19c94b3133eb90b90b8b860528c7284a0412f18` |
| 本轮导入 | `Assets/Polygonal Creatures Pack/Polygonal Wolf/` 下 12 个资产（白名单） |

三条约束：

1. **`.unitypackage` 本身不进仓库**，与内嵌代码包不同 —— 38 MB 的二进制里有
   九成是本阶段用不到的另外九类怪物。进仓库的是**选择性导入后的那 12 个文件**。
2. **导入必须是声明式、可复现的**，不是一次手工勾选。
   [`Tools/import_polygonal_wolf.py`](../../Tools/import_polygonal_wolf.py) 的白名单写在代码里，
   运行时先校验包的字节数与 SHA-256，对不上直接拒绝；
   同时提取 `asset` 与 `asset.meta`，因此第三方 GUID 原样保留。
3. **第三方资产保留在自己的原始目录**，不搬进 `Assets/Game/`，`.meta` 不覆盖。
   需要适配（例如内建 `Standard` Shader 在 URP 下是粉色）时只**新建**项目自有的适配层，
   并在 [资源映射说明](../Monster/duskshadow-wolf-asset-mapping.md) 里记下原值与替换值。
