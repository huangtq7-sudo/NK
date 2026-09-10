# 配置表编写与维护指南

适用范围：`Config/Source/`下的全部CSV源表，以及`Tools/Config/Naraka.ConfigCompiler`。

设计依据见 [ADR-0010](../ADR/0010-csv-config-pipeline.md)。

## 1. 改一个数值要做什么

```bash
# 1. 用 Excel 打开并修改 Config/Source/ 下的表，保存为 CSV UTF-8（带 BOM）
# 2. 重新生成
dotnet run --project Tools/Config/Naraka.ConfigCompiler
```

编译器会做三件事：校验、生成`Shared/Generated/Config/`下的JSON与清单、把生成物镜像到`NK/Assets/StreamingAssets/Config/`。

校验失败时它返回非0退出码，并指出**源文件与行号**。不要去改生成的JSON——下次编译会把它覆盖掉，而且CI的一致性门禁会失败。

## 2. 保存格式

- 编码必须是 **UTF-8 带 BOM**。Excel靠BOM识别中文；存成无BOM的UTF-8会在Excel里显示为乱码。
- 第一行是表头，列名区分大小写，与`Shared/Config/NarakaConfigModels.cs`里的字段名一致。
- 字段里有逗号、引号或换行时用双引号包裹，双引号本身写成两个（RFC 4180）。
- 布尔值写`true`/`false`。数字不要带千分位分隔符。

## 3. 表清单

| 文件 | 内容 | 稳定ID |
| --- | --- | --- |
| `currencies.csv` | 三种货币与初始发放量 | `CurrencyId` |
| `items.csv` | 全部仓库物品、堆叠上限、品质、图标键 | `ItemId` |
| `heroes.csv` | 英雄 | `HeroId` |
| `hero_skills.csv` | 英雄技能 | `SkillId` |
| `weapons.csv` | 长剑与太刀 | `WeaponId` |
| `weapon_levels.csv` | 每把武器每一级的数值 | `WeaponId` + `Level` |
| `forge_recipes.csv` | 从某级升到下一级的材料与货币消耗 | `RecipeId` |
| `shop_products.csv` | 商店商品、价格、限购 | `ProductId` |
| `gacha_pools.csv` | 奖池、单抽/十连价格、保底 | `PoolId` |
| `gacha_entries.csv` | 奖池内的奖励与权重 | `PoolId` + `RewardId` |
| `signin_rewards.csv` | 七日签到奖励 | `Day` |
| `signin_milestones.csv` | 连续签到节点奖励 | `MilestoneDays` |
| `account_level_rewards.csv` | 账号等级与所需累计经验、奖励 | `Level` |
| `achievements.csv` | 成就、分类、目标、成就经验、事件源 | `AchievementId` |
| `inventory_capacity.csv` | 仓库容量档位与扩容代价 | `Tier` |
| `avatars.csv` | 头像 | `AvatarId` |
| `avatar_frames.csv` | 头像框 | `AvatarFrameId` |
| `pets.csv` | 宠物 | `PetId` |

## 4. 编译器会拒绝什么

- 重复的稳定ID。
- 指向不存在的行的外键（比如奖池里引用一个不存在的`ItemId`）。
- 负的价格或数量。
- `StackLimit <= 0`。
- 非法的概率权重，或某个奖池的权重总和为0。
- 白/蓝/紫/金/红之外的品质。
- 20抽保底品质在奖池里没有任何可兑现的奖励。
- 武器等级不连续或有重复。
- 锻造配方引用不存在的材料。
- 签到天数不在1–7。
- 非法的账号等级奖励等级。

这些不是"建议"，是硬性失败。一条不合法的配置进不了生成物，因此也进不了服务端。

## 5. 几条容易踩的规则

**新增一个成就时**，如果它依赖P2战斗或P3远征才会有的事件，`IsActiveInP1`必须填`false`。服务端不会给它累加进度，客户端会把它整体压暗并显示"等待后续版本开放"。**不要为了让界面好看而填`true`**——那会让玩家看到一个永远卡在0的进度条。

**新增一个账号等级奖励时**，`RewardKind`填`None`表示该等级没有奖励。服务端会拒绝领取，界面会把按钮显示为"无奖励"而不是可点击。

**补签卡**是`items.csv`里的一个普通可堆叠物品（`special_makeup_card`）。它的产出渠道与价格在`shop_products.csv`里配置，不在代码里。

**改抽奖权重时**注意十连蓝保底与20抽红保底：编译器会检查保底品质在奖池里可兑现，但不会检查权重是否"合理"。把红色权重设成0仍然合法（保底会强制给出），但会让20抽之前完全出不了红。

## 6. 谁是权威

服务端配置是**经济判定的唯一权威**。客户端加载同一份配置**只用于展示**：图标、名称、说明、排序、"下一级预览"这类界面信息。

价格、概率、奖励与强化结果一律以服务端返回值为准。View与Controller都不得用客户端配置里的数值推导任何账号资产变化。

## 7. CI门禁

`Tools/CI/Invoke-ServerTests.ps1`在跑测试之前会执行：

```bash
dotnet run --project Tools/Config/Naraka.ConfigCompiler -- --check
```

如果生成物与源表不一致（比如有人改了CSV但忘了重新生成，或者手改了JSON），这一步会失败并阻止后续测试。**提交前请务必先跑一次编译器。**
