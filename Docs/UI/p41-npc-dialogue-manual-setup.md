# P4.1 NPC、底部对话与主线任务：装配记录与人工验收

日期：2026-10-12
说明：用户于 2026-10-12 授权 Claude 代为完成原本由用户手工进行的素材导入与 Unity 装配。本文记录实际做了什么、如何复现、如何回滚，以及留给用户的人工验收项。
状态：2026-10-12 用户人工验收通过，素材许可已确认（Q-035 关闭）。

## 1. 素材导入（声明式、可复查）

- 工具：`Tools/import_combat_girls_npcs.py`（预演：`python Tools/import_combat_girls_npcs.py`；写盘：加 `--apply`）。
- 包身份核对：Katana 包 96,016,910 字节 / SHA-256 `80f106cd…62f3`；Sword 包 125,490,872 字节 / SHA-256 `a01f9590…1e4a`。对不上即拒绝。
- 只写出 21 个文件（共 70.7 MiB，PNG/FBX 走 Git LFS）及原始 `.meta`，第三方 GUID 不变，与工程已有 2,266 个 GUID 零冲突：
  - 两个角色 FBX：`School_Katana_Girl/Models/School_Katana_FullBody.fbx`、`CombatGirl_Sword/Models/CombatGirl_Sword.fbx`；
  - 两个 Idle：`Animations/Normal/Idle.fbx`、`Animations/Normal/S_Idle.fbx`（Humanoid、循环）；
  - 两个 Avatar 来源：`Humanoid_Bot/Models/Humanoid_F.fbx`、`Humanoid_Fe_Sword.fbx`（模型与 Idle 都是 Copy From Other Avatar，缺了无法重定向；它们不在场景中渲染）；
  - 15 张主贴图（身体、脸、眼、头发、一套服装、武器）。
- 明确排除：包内脚本与编辑器工具、演示 UI/展台与重复的 TMP 着色器、挂 Magica Cloth 2 组件的演示 Prefab（工程无该插件）、包内 Standard 材质、演示 Animator、全部非 Idle 动画、部件 FBX 与其他配色。

## 2. 自动装配（`NARAKA/P4.1/Assemble NPCs And Quest HUD Into Map01`）

编辑器工具 `Assets/Game/EditorTools/P41NpcQuestSetup.cs`，可在菜单或批处理 `-executeMethod Naraka.EditorTools.P41NpcQuestSetup.AssembleBatch` 运行；重复运行先删除上次创建的同名根对象再重建，结果可复现。报告写到 `artifacts/p41/p41-assemble.txt`。

- 材质：`Assets/Game/Art/Characters/Npc/Materials/` 下项目自有 URP Lit 材质（主贴图、平滑度 0.15；头发/脸/眼带透明通道时 Alpha Clip 0.5、双面）。不修改第三方材质与 `.meta`。
- 动画：`Assets/Game/Art/Characters/Npc/Animators/` 下单状态 Idle 控制器，Avatar 取 `Humanoid_F.fbx`，关闭 Root Motion。
- Prefab：`NPC_ShenShuangchi.prefab`（太刀，校服）、`NPC_LuYunsheng.prefab`（剑）。按完整身体底模量身高缩放到 3.8（主角约 4，Q-021），分别缩放 2.267 与 2.276；隐藏其他服装、表情变体与裸身底模；根节点挂 `SphereCollider`（Trigger，半径 3.5，中心抬高 1.2）与 `NpcInteractionVolume`；子物体 `BodyCollider` 为实体胶囊（半径 0.55），玩家不能穿过 NPC；头顶四个标记（可接取“!”、对话目标蓝“!”、可复命“?”、待领取橙“?”）带 `WorldBillboard`，默认隐藏，按任务状态显示。
- Map01 摆放（场景根对象 `P41_Npcs`）：
  - 沈霜迟 `(391.55, 34.99, 597.67)`，传送门 `(385.10, 36.10, 602.50)` 靠出生点一侧、偏开主路，面向出生点；
  - 陆云笙 `(423.47, 34.99, 573.55)`，距沈霜迟 40 米的平坦空地，面向沈霜迟；
  - 选点规则：地形坡度 < 15°、与出生点高差 ≤ 4 米、1.5 米内无实体碰撞体、头顶不被建筑覆盖。位置可在 Unity 中直接拖动调整，不需要改代码。
- 任务 HUD（场景根对象 `P41_QuestHud`，Screen Space Overlay，参考分辨率 1920×1080）：底部对话框（姓名/正文/继续提示）、中下方“E 交谈”与短提示、右侧中部追踪栏、居中领取面板（领取/关闭按钮）。文字使用 Unity 内置动态字体（`Text`），中文由系统字体回退显示；工程里没有中文 TMP 字体资产，也不分发系统字体文件。
- `P41_EventSystem`：Map01 原本没有 EventSystem（大厅的 EventSystem 随大厅场景卸载），领取面板按钮需要它。
- 输入资产：`NarakaPlayerControls.inputactions` 的 Player 表新增 `Interact`（Button，`<Keyboard>/e`）。

截图：`artifacts/p41/npc-npc_shuangchi.png`、`npc-npc_yunsheng.png`、`dialogue-hud.png`（`NARAKA/P4.1/Capture NPC And Dialogue Screenshots`，不保存场景）。

## 3. 用户人工验收清单

前提：本机 Host 来自本分支（Bootstrap 返回 `quest` 能力）、数据库已执行 0011。

1. 登录 → 开始游戏 → Map01：右侧追踪栏显示“入世·问路 / 前往沈霜迟处接取任务”，沈霜迟头顶黄色“!”。
2. 走到传送门旁的沈霜迟身边：出现“E 交谈 · 沈霜迟”；按 E 打开底部对话框，逐字显示；逐字中点击显示整句，再点击下一句；连点不会跳句；对话中角色不能移动/攻击，世界不暂停。
3. 中途按 Esc：对话关闭、任务不接取；再次按 E 从头开始。
4. 读完最后一句：任务接取，追踪栏变为“前往林边药庐，与陆云笙交谈。”，陆云笙头顶蓝“!”。
5. 找陆云笙对话读完（接取后至少 3 秒）：追踪栏变为“回到沈霜迟处复命”，沈霜迟头顶“?”。
6. 回沈霜迟处读完提交对话：弹出领取面板，显示“账号经验 100 / 铜币 ×300 / 血包 ×2”，指针可见。
7. 关闭面板、退出重登再进 Map01 与沈霜迟交谈：播放“东西还没拿？”后再次弹出面板。
8. 点击领取：面板关闭，提示领取内容；返回大厅等级为 2，等级奖励可领取。
9. 再与两人交谈：只播放日常对白。
10. 视觉：两名 NPC 的比例、站位、朝向、材质观感（无卡通描边与布料物理，为静态 Idle）；对话框、提示、追踪栏、领取面板的位置与字号。需要调整时直接在场景中修改，或修改工具常量后重新运行装配菜单。

## 4. 回滚

删除 Map01 根对象 `P41_Npcs`、`P41_QuestHud`、`P41_EventSystem` 并保存场景即可让功能静默；NPC 资源位于 `Assets/CombatGirlsCharacterPack/` 与 `Assets/Game/Art/Characters/Npc/`。服务端不声明 `quest` 能力时客户端不发送任何任务协议。
