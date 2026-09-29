# ADR-0014：P2 接入 Input System 1.7.0 与 Cinemachine 2.10.1

- 状态：已接受
- 日期：2026-09-26

## 背景

P2 需要正式的键鼠输入与第三人称镜头。`NARAKA_技术架构.md` 早已把 Input System 与
Cinemachine 列为计划内依赖，但 P0/P1 从未安装，工程里也没有任何玩家输入实现。

选版本不能凭"最新"：Cinemachine 3.x 是 Unity 2022.3+ 的产品线，装到 2021.3 上会直接
编译失败；Input System 1.8 之后的若干版本把最低 Unity 声明提升到了 2021.3，个别版本
在 2021.3.45 上的行为没有本项目的实测证据。另一方面 ADR-0003 已经确立过一条约束：
关键依赖必须能在网络不可用时还原。

## 决策

版本锁定为：

| 包 | 版本 | 来源 |
| --- | --- | --- |
| `com.unity.inputsystem` | **1.7.0** | `https://packages.unity.cn` |
| `com.unity.cinemachine` | **2.10.1** | `https://packages.unity.cn` |

依据是本编辑器自己的包清单
`E:\2021.3.45f2c1\Editor\Data\Resources\PackageManager\Editor\manifest.json`：

```
com.unity.cinemachine   minimumVersion 2.10.1   version 2.10.1
com.unity.inputsystem   minimumVersion 1.7.0    version 1.7.0
```

也就是说这两个版本号不是挑出来的，而是 Unity 2021.3.45f2c1 为自己声明的对应版本。
两者的 `package.json` 都声明 `"unity": "2019.4"`，满足下限；Cinemachine 2.x 是
2021.3 唯一可用的产品线。

可获得性已实测：两个版本都已解包存在于本机 npm 缓存
`%LOCALAPPDATA%\Unity\cache\npm\packages.unity.cn\`，因此断网也能还原，符合 ADR-0003 的意图。

`Packages/manifest.json` 使用精确版本号，不使用浮动版本；`packages-lock.json` 记录
`source: registry` 与 `url: https://packages.unity.cn`。升级这两个包必须单独提交并重新
执行 Unity 编译与全部测试。

### activeInputHandler 必须是 Both

`ProjectSettings.asset` 的 `activeInputHandler` 由 `0`（仅旧输入）改为 **`2`（Both）**，
不能改成 `1`（仅新输入）。原因是工程里有两个第三方演示脚本使用 `Input.GetAxis`：

- `Assets/PureNature/Scripts/FreeCamera.cs`
- `Assets/Aquarius Fantasy - High Elves/Demo Scenes/DemoCharacter.cs`

设为"仅新输入"会让整个工程编译失败。我们自己的 `Assets/Game` 下已实测零处旧输入调用，
正式玩家输入只走 Input System；`Both` 只是为了不修改第三方资源而保留旧后端。

### 输入资产与动作表

- 资产：`Assets/Game/Settings/Input/NarakaPlayerControls.inputactions`，由脚本按稳定
  GUID 生成，因此相同输入必定产生相同文件。
- `Player` 动作表：`Move`(Vector2/WASD 2DVector)、`CameraLook`(Vector2/鼠标位移)、
  `SprintOrDash`(左 Shift)、`Attack`(鼠标左键)、`SkillF`(F)、`SkillV`(V)。
- `Debug` 动作表：`TriggerHit`(F9)、`TriggerDeath`(F10)、`TriggerRevive`(F11)。
  它只在 `UNITY_EDITOR || DEVELOPMENT_BUILD` 下被启用，正式发布构建不会激活；
  使用它的调试显示整份文件也被同一条编译条件包起来。
- 加载、出场、死亡与设置界面打开时禁用 `Player` 动作表。启用状态是业务状态
  （`PlayerOverlayFlags.InputLocked`）的投影，不是第二份真相。
- `NarakaPlayerInputProvider` 在运行期使用资产的**副本**（`Instantiate`），不直接用资产本体。
  `InputActionAsset` 会把"是否已启用"和绑定解析结果缓存在资产实例上，同一份资产被
  先后两个 provider 使用时后者会继承前者的解析结果，于是读不到自己启动之后才出现的设备。
  这个问题是在 PlayMode 测试里实测出来的，Unity 自己的 `PlayerInput` 组件也用同样的办法回避。

### 第三人称镜头结构

```
NarakaAppRoot/CameraRig            ThirdPersonCameraRig：位置跟随角色胸口，旋转只来自鼠标
├── CameraPivot                    环绕轴心
└── ThirdPersonCamera              CinemachineVirtualCamera + CinemachineCollider
                                   Body = Transposer(LockToTarget, offset (0,0,-distance))
                                   Aim  = HardLookAt(轴心)
NarakaAppRoot/Main Camera          CinemachineBrain
```

轴心**不是角色的子物体**：它只取角色的位置，不取角色的旋转。由此得到三条硬性保证：

- 角色永远是环绕中心：相机看的是挂在角色身上的轴心；
- 鼠标转动镜头不会旋转角色：轴心与角色的朝向无关；
- 角色用 A/D 转身不会把相机拖着转。

遮挡由 `CinemachineCollider` 处理：它只缩短相机到轴心的距离，不移动轴心，
因此贴墙时中心点仍然在角色身上。距离、轴心高度、俯仰范围、灵敏度、上下反转与阻尼全部在
`ThirdPersonCameraSettings` 里，没有散落的魔法数字。不做目标锁定，不做肩部偏移锁定镜头。

当前取值：轴心高度 **2.8 米**、距离 **9 米**。初版是 1.4 / 4.5，
2026-09-28 用户实机反馈"角色看不清"，按要求把两者各乘二。

场景加载与重生后由 `WorldSceneEntry` 调用 `ThirdPersonCameraRig.Rebind(newPlayer)`；
相机与轴心都挂在持久化根上，因此既不会保留已销毁的 Transform，也不会累积第二台相机。

## 结果

- 工程可在断网条件下还原这两个依赖；升级需要单独提交与完整回归。
- `Both` 输入后端是为第三方资源保留的妥协，不是长期目标。第三方演示脚本被移除或
  改写之后可以另立 ADR 收敛为"仅新输入"。
- 调试动作表的存在依赖编译条件而不是纪律。正式发布包里它不可能被启用。
