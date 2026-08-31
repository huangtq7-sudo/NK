# ADR-0001：客户端采用模块化MVC

- 状态：已接受
- 日期：2026-08-31

## 决策

客户端业务依赖方向固定为`View → Controller → Model/Domain Interface ← Infrastructure Implementation`。VContainer、UniTask、MessagePipe、R3、HFSM和行为树只能作为MVC内部工具。

Model程序集不引用UnityEngine对象、View或Infrastructure；Controller只依赖Model与抽象接口；View不获得Model可写引用；Infrastructure实现只在Composition Root注册。

## 结果

使用asmdef和自动化依赖测试阻止反向引用。网络、资源、配置、时间和遥测均通过接口进入业务层。
