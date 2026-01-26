# 开发者文档（DOC-002）

面向参与开发/维护 ExpandScreen 的同学，沉淀协议、架构与贡献流程等“能直接上手”的资料。

## 快速入口

- 通信协议（API）：`docs/developer/API.md`
- 系统架构：`docs/developer/ARCHITECTURE.md`
- Release 指南：`docs/developer/RELEASE.md`
- 贡献指南：`CONTRIBUTING.md`
- 开发环境与构建：`docs/DEVELOPMENT.md`
- 项目开发流程记录：`docs/开发流程文档.md`

## 文档约定

- 以**当前代码实现**为准：C# 端位于 `src/ExpandScreen.Protocol` 与 `src/ExpandScreen.Services`。
- 协议与消息字段使用 **PascalCase**（与 `System.Text.Json` 默认行为一致）。
- 需要变更协议时，优先写清楚：兼容策略、版本号、灰度/回滚方案，并补齐双方（Windows/Android）实现与测试。
