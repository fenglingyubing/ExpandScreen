# ExpandScreen

将Android设备扩展为Windows电脑的外接屏幕，支持触控交互和低延迟显示。

## 项目简介

ExpandScreen 是一个跨平台的屏幕扩展解决方案，通过USB或WiFi连接，将Android设备变成Windows电脑的第二个显示器，并支持触控操作。

### 主要特性

- 🖥️ **虚拟显示器**：在Windows上创建虚拟显示器
- ⚡ **低延迟传输**：硬件编解码支持，延迟 < 100ms
- 👆 **触控支持**：支持多点触控和手势操作
- 🔌 **双连接模式**：支持USB和WiFi连接
- 🎨 **多分辨率**：支持1080p、2K等多种分辨率
- 🔐 **安全加密**：支持设备配对和通信加密

## 项目结构

```
ExpandScreen/
├── src/
│   ├── ExpandScreen.UI/          # WPF用户界面
│   ├── ExpandScreen.Core/        # 核心功能（捕获、编码、显示）
│   ├── ExpandScreen.Services/    # 服务层（连接、网络）
│   ├── ExpandScreen.Protocol/    # 通信协议
│   └── ExpandScreen.Utils/       # 工具类
├── docs/                         # 项目文档
├── .github/workflows/            # CI/CD配置
└── ExpandScreen.sln              # Visual Studio解决方案

```

## 技术栈

### Windows客户端

- .NET 8.0
- WPF (Windows Presentation Foundation)
- DXGI Desktop Duplication API
- FFmpeg / NVENC / QuickSync
- Serilog日志框架

### Android客户端（待开发）

- Kotlin + Jetpack Compose
- MediaCodec
- OpenGL ES

## 开发环境要求

### Windows开发

- Visual Studio 2022 或更高版本
- .NET 8.0 SDK
- Windows 10/11 (支持WDDM 2.0+)
- Windows Driver Kit (WDK) - 用于虚拟显示驱动开发

### Android开发

- Android Studio 2023.1.1 或更高版本
- JDK 17
- Android SDK (API Level 29+)

## 快速开始

### 克隆仓库

```bash
git clone https://github.com/yourusername/ExpandScreen.git
cd ExpandScreen
```

### 构建Windows客户端

```bash
# 恢复依赖
dotnet restore ExpandScreen.sln

# 构建项目
dotnet build ExpandScreen.sln --configuration Release

# 运行应用
dotnet run --project src/ExpandScreen.UI/ExpandScreen.UI.csproj
```

### 运行测试

```bash
dotnet test ExpandScreen.sln
```

## 开发指南

详细的开发指南请参阅 [DEVELOPMENT.md](docs/DEVELOPMENT.md)

## 用户文档

- 用户文档（Markdown）：[docs/用户文档.md](docs/用户文档.md)
- 用户文档（HTML，可离线打开）：[docs/user-docs/index.html](docs/user-docs/index.html)

## 任务规划

项目采用敏捷开发模式，分为5个阶段：

1. **阶段一（4-6周）**：基础功能开发
2. **阶段二（4-6周）**：核心功能完善
3. **阶段三（3-4周）**：功能增强
4. **阶段四（2-3周）**：测试和优化
5. **阶段五（1-2周）**：发布准备

详细任务规划请参阅 [任务文档](docs/任务文档.md)

## 贡献指南

欢迎贡献代码！请遵循以下步骤：

1. Fork本仓库
2. 创建您的特性分支 (`git checkout -b feature/AmazingFeature`)
3. 提交您的更改 (`git commit -m 'Add some AmazingFeature'`)
4. 推送到分支 (`git push origin feature/AmazingFeature`)
5. 开启一个Pull Request

## 许可证

本项目采用 MIT 许可证 - 详见 [LICENSE](LICENSE) 文件

## 联系方式

- 项目主页：https://github.com/yourusername/ExpandScreen
- 问题反馈：https://github.com/yourusername/ExpandScreen/issues

## 致谢

感谢所有为此项目做出贡献的开发者！
