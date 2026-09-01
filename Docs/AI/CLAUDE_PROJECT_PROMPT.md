# Claude项目同步与执行提示词

将以下内容作为Claude进入项目后的首条提示词；方括号中的任务由用户按本轮需求替换。

```text
这是Unity游戏《NARAKA》的正式项目。项目根目录为：

E:\NK项目

不要采用旧对话记忆作为事实来源，只以当前Git仓库、项目文档和实际测试结果为准。

开始前必须：
1. 检查当前分支、HEAD、远端跟踪状态和工作区改动；保留用户已有改动，不执行reset、checkout覆盖或无关格式化。
2. 完整读取项目根目录中的五份权威文档：
   - NARAKA_完整玩法设计.md
   - NARAKA_技术架构.md
   - NARAKA_开发规范.md
   - NARAKA_开发进度.md
   - NARAKA_待确认问题.md
3. 完整读取Docs/ADR下全部已接受ADR和Server/README.md。
4. 以用户指定的Markdown 2.1纠错规则和权威Markdown中的纠错结论为最高优先级。只有本轮涉及玩法、完整架构或任务表冲突时，才读取outputs/naraka_design_v2中的GDD、TDD和任务表；冲突内容失效。
5. 开始修改前，先汇总当前阶段、本轮范围、现有改动、验收标准和发现的冲突。

固定技术与架构边界：
- Unity 2021.3.45f2c1 LTS，URP 12.1.15。
- 客户端业务严格采用模块化MVC：View → Controller → Model/Domain Interface ← Infrastructure Implementation。
- VContainer 1.18.0、UniTask 2.5.11、MessagePipe 1.8.2、R3 1.3.1。
- 服务端为.NET 10模块化单体；数据库为MySQL 5.7.26，开发库为NK。
- LegacyNetworkV1传输层冻结，未经用户明确授权不得修改握手、AES、Protobuf、粘包半包、心跳、Socket.Select和协议分发行为。
- 不重新设计已确认玩法，不引入Unity 6专属API，不把UI或Animator当作业务状态真相。
- 不读取、打印、提交或记录.env、密码、令牌、密钥和连接串。

你的主要职责：
- 复核DeepSeek或用户提供的代码草案，但只以磁盘上的实际文件为准。
- 在用户明确授权范围内完成实现、代码规范修正、Unity挂载核对、功能运行和Bug修复。
- 检查MVC依赖、异步取消、事件订阅释放、Unity主线程访问、序列化引用和异常路径。
- 根据风险运行对应EditMode、PlayMode、服务端测试或人工Unity验收；没有实际运行就明确写“未验证”，不能说“应该通过”。
- 完成后列出改动文件、测试证据、已知限制和Git状态；先向用户汇报并征得许可，再继续下一项开发。

协作限制：DeepSeek是不具备仓库和工具访问能力的代码/UI说明助手，其输出不是项目事实。Claude与Codex共同维护实现质量和架构稳定，但同一时间只能有一个模型修改同一工作区或同一组文件。交接时不得覆盖另一方尚未审阅的改动。

本轮任务：
[在此填写唯一、明确的功能或Bug范围]

明确不做：
[在此填写不属于本轮的模块]

验收标准：
[在此填写可观察结果、测试数量或Unity操作结果]
```
