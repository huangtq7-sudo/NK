# P1本地验收记录

日期：2026-09-09
范围：P1.0 至 P1.9（大厅与账号长期系统）
执行环境：本机 Windows 11、Unity 2021.3.45f2c1、仓库内 .NET 10 SDK、无云端连接

## 1. 实测结果

以下全部为**本轮实际执行**的命令与输出，不是推断。

### 服务端

```bash
Tools/CI/Invoke-ServerTests.ps1
```

```
Restoring the server solution with NuGet vulnerability auditing enabled.
Building the server solution in Release.
Verifying that the generated configuration matches Config/Source.
Running Naraka.ConfigCompiler.Tests.
Running Naraka.Server.ArchitectureTests.
Running Naraka.Server.Application.Tests.
Running Naraka.Server.LegacyNetworkV1.Tests.
Running Naraka.Server.Infrastructure.Tests.
Server CI passed: total=354, executed=354, passed=354, failed=0.
```

分项：ConfigCompiler 20、ArchitectureTests 4、Application 217、LegacyNetworkV1 56、Infrastructure 55。

Release构建：0警告、0错误。

### 配置一致性门禁

CI在跑测试前执行`--check`，通过。生成物`Shared/Generated/Config/naraka-config.json`与`Config/Source/`下的18张CSV一致，Unity镜像副本一致。

`ConfigVersion = p1-config-5e52cf730692`，`SchemaVersion = 1.0.0`。

### 数据库迁移 dry-run

```
Validated 0001_p0_identity.sql (4 statements).
Validated 0002_p1_account_progression.sql (3 statements).
Validated 0003_p1_account_foundation.sql (5 statements).
Validated 0004_p1_inventory.sql (3 statements).
Validated 0005_p1_shop.sql (2 statements).
Validated 0006_p1_weapons.sql (2 statements).
Validated 0007_p1_gacha.sql (4 statements).
Validated 0008_p1_progression_rewards.sql (7 statements).
Validated 0009_p1_social.sql (7 statements).
```

**未对任何数据库实际执行迁移。**

### Unity

```bash
Tools/CI/Invoke-UnityTests.ps1 -TestPlatform All
```

```
Unity project import passed: exit=0, no C# compilation errors found.
Unity EditMode passed: total=282, passed=281, failed=0, inconclusive=0, skipped=1.
Unity PlayMode passed: total=3, passed=3, failed=0, inconclusive=0, skipped=0.
```

唯一跳过项是`LegacyClientLiveSmokeTests.RegisterAndLoginAgainstConfiguredHost`——它需要一个真实运行的Host，按环境条件跳过。这与P0基线的行为一致，不是本轮新增的跳过。

PlayMode日志零异常。

新增的9项专用面板UXML契约测试全部通过：英雄、兵器、仓库、商店、锻造、抽奖、签到、成就、好友聊天。测试会克隆真实UXML，并逐一验证各Panel View查询的元素名称与控件类型；它不替代布局、素材和交互手感的人工视觉验收。

## 2. 覆盖的关键不变量

下列每一条都有直接断言它的测试，而不是间接推断：

**经济与幂等**
- StarterGrant每个账号只发一次，且是累加而不是覆盖
- 购买、锻造、抽奖、签到、奖励领取都在单事务内完成余额+流水+库存+业务结果+幂等记录
- 重复的RequestId/OrderId只产生一次结果
- 余额不足、库存不足、容量不足、并发重复请求都会整笔回滚

**抽奖**
- 十连至少一个蓝及以上
- 20抽保底出红
- 提前出红重置保底计数
- 未展示订单在重新登录后恢复
- 客户端不生成任何随机结果（`GachaRoller`是纯函数，随机源在服务端）

**锻造**
- 材料足够必定成功，服务端零随机
- 重复请求只升一级

**签到**
- 服务器05:00日界，跨月跨年单调
- 每周期一次补签，需要补签卡
- 补签不推进连续次数（主进度与连续次数分表分字段）
- 重复领取被主键拒绝

**成就与账号等级**
- `AchievementXp`与`AccountXp`分表分列，迁移契约测试直接断言`0008`不含`account_xp`
- `IsActiveInP1 = false`的成就进度恒为0，即使服务端返回了数字客户端也归零

**红点**
- 叶子有内容时祖先跟着亮
- 清空唯一叶子后祖先熄灭
- 兄弟节点存在时祖先保持亮
- 读完一段会话不影响其他会话
- 看过之后来了新内容会重新亮起（bool做不到这一点）
- 没有任何内容时不显示红点

**社交**
- 好友关系成对写入，不存在单向好友
- 屏蔽同时清除好友关系与两个方向的申请
- 互相申请自动成为好友
- 只有好友能开会话、能发消息
- 限流10秒10条，窗口过后释放
- 被拒绝的请求不消耗限流额度
- 控制字符被剔除，存下来的与显示的一致
- 一方已读不影响另一方未读

**版本门禁与能力兼容**
- Bootstrap版本匹配但响应缺少`serverCapabilities`时，兼容模式仍允许登录
- 兼容模式下遍历全部十个入口，一个未知协议都不发送
- 每个入口都映射到一个已登记的能力字符串
- 完整P1客户端使用`p1-config-1`，会在预检阶段拒绝旧`p0-config-1`云端

**冻结传输**
- `LegacyWireGoldenTests`对`Client.Count == 12`与`Server.Count == 19`的断言原样保留并通过
- 应用协议编号从19起，与冻结范围0–18无交集
- 迁移`0001`/`0002`内容被哈希锁定

## 3. 未验证 / 待人工确认

- **未执行真实Host + MySQL端到端冒烟。** 需要用户启动Host与数据库并授权；本轮未取得授权，也未读取任何连接串。
- **全部界面的视觉效果需要人工Play Mode验收**：布局、字号、素材位置、动画节奏、滚动手感。本轮只保证元素名稳定、状态分支（加载/成功/失败/空数据/服务器未支持）完整、生命周期正确解绑。
- 无正式角色模型，英雄界面只保留`HeroModelAnchor`，未导入或生成任何未知角色模型。
- 抽奖红色卡背暂用玄夜卡背素材，正式素材到位后替换。

## 4. 已知问题的处理

仓库中记录的Windows Player构建阻塞属于**既有已知问题**，不是本轮回归。本轮未扩大修复范围，也未尝试绕过它。P1的验收方式是EditMode + PlayMode + 服务端CI，不依赖Player构建。

## 5. 本轮明确未做的事

- 未修改、重启或重新部署阿里云服务器
- 未对云端NK库执行任何迁移
- 未提交或推送Git（工作区改动保留，等待用户复核）
- 未读取、输出、提交或记录任何`.env`、密码、密钥或连接串
- 未修改已部署的迁移`0001`与`0002`
- 未安装Visual Studio、容器、Redis、MQ或任何额外云组件
- 未开始P2战斗或P3远征的任何内容
