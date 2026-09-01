# ADR-0006：云端开发环境采用单机与SSH隧道

- 状态：已接受
- 日期：2026-09-01

## 背景

当前目标是让开发者在本地Unity中使用阿里云Windows Host与云端MySQL，不向其他玩家或公网客户端提供服务。云主机资源为2 vCPU、2 GiB内存和40 GiB系统盘，应避免在没有容量证据前引入容器、Redis、消息队列或完整可观测性平台。

`GET /bootstrap/config-version`在直接远程访问时必须使用HTTPS；`LegacyNetworkV1`传输层继续冻结，不能为了部署改变握手、AES、Protobuf、心跳或Socket分发行为。

## 决策

- 开发环境使用Windows Server 2022单机部署：自包含`win-x64`的.NET 10 Host与MySQL 5.7.26运行在同一台主机。
- MySQL只监听`127.0.0.1:3306`；Host只监听`127.0.0.1:5222`和`127.0.0.1:8011`。
- 本地Unity通过SSH本地端口转发访问云端loopback端口。Bootstrap HTTP不会以明文暴露在公网，客户端仍使用既有loopback地址。
- MySQL以Windows服务自动启动；Host由SYSTEM计划任务在开机时启动。连接串仅通过机器级`NARAKA_MYSQL_CONNECTION_STRING`注入，脚本、日志和仓库不得输出其值。
- SSH和远程桌面只用于管理，并在Windows防火墙中限制管理来源。数据库和游戏端口不建立公网入站规则。
- 这是开发环境拓扑，不是正式生产发布方案。域名、TLS、环境隔离、备份、监控、密钥托管和容量扩展在发布阶段另行设计和验收。

## 结果

- 不修改`LegacyNetworkV1`即可让正式Unity工程完成云端版本预检、注册、登录和空大厅闭环。
- SSH隧道关闭后客户端连接会按预期中断；开发者公网地址变化时必须更新管理端口的来源白名单。
- 单机故障会同时影响Host与数据库；当前接受该开发期限制，不能据此宣称达到生产可用性。
- 操作和验收记录见`Docs/Deployment/aliyun-windows-development.md`。
