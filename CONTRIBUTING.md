# 贡献指南（DOC-002）

感谢参与 ExpandScreen 的开发与维护！本指南面向提交代码/文档的贡献者，重点覆盖：分支/提交规范、开发环境、测试与文档流程。

## 1. 开发环境

### Windows

- Visual Studio 2022（.NET 桌面开发）
- .NET 8 SDK
- 可选：WDK（驱动编译/签名/安装）

### Android

- Android Studio（JDK 17）
- Android SDK（API 29+）

更完整说明见：`docs/DEVELOPMENT.md`

## 2. 获取代码

```bash
git clone https://github.com/fenglingyubing/ExpandScreen.git
cd ExpandScreen
```

## 3. 分支命名

- 功能分支：`vk/<任务ID>-<任务简述>`

示例：`vk/e6f3-6-1-2`

## 4. 提交规范

建议沿用项目开发流程文档中的模板与要求：

- 提交消息格式与说明：`docs/开发流程文档.md`
- 原则：信息完整、可追溯（任务ID/关键改动/测试结果/冲突处理）

## 5. 构建与测试

```bash
dotnet restore ExpandScreen.sln
dotnet build ExpandScreen.sln --configuration Release
dotnet test ExpandScreen.sln --verbosity normal
```

## 6. 文档贡献

涉及协议/架构/流程的改动：

1. 同步更新 `docs/developer/*` 或相关文档；
2. 在 `docs/开发流程文档.md` 补充本次变更的记录（尤其是协议与兼容策略）。

