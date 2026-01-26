# 开发指南

本文档提供ExpandScreen项目的详细开发指南。

## 目录

1. [开发环境配置](#开发环境配置)
2. [项目架构](#项目架构)
3. [编码规范](#编码规范)
4. [构建和调试](#构建和调试)
5. [测试指南](#测试指南)
6. [常见问题](#常见问题)

## 开发环境配置

### Windows开发环境

#### 必需软件

1. **Visual Studio 2022**
   - 工作负载：.NET桌面开发
   - 组件：.NET 8.0 SDK

2. **Windows Driver Kit (WDK)**
   - 用于虚拟显示驱动开发
   - 下载地址：https://docs.microsoft.com/zh-cn/windows-hardware/drivers/download-the-wdk

3. **Git**
   - 版本控制工具
   - 下载地址：https://git-scm.com/

#### 可选软件

- **ReSharper**：代码质量工具
- **Visual Studio Code**：轻量级编辑器
- **GitHub Desktop**：Git图形化界面

### 初始化开发环境

```bash
# 克隆仓库
git clone https://github.com/fenglingyubing/ExpandScreen.git
cd ExpandScreen

# 恢复NuGet包
dotnet restore ExpandScreen.sln

# 构建解决方案
dotnet build ExpandScreen.sln
```

## 项目架构

### 解决方案结构

```
ExpandScreen.sln
├── ExpandScreen.UI          # 用户界面层
│   ├── Views/              # XAML视图
│   ├── ViewModels/         # MVVM ViewModel
│   └── Resources/          # 资源文件
├── ExpandScreen.Core        # 核心业务逻辑
│   ├── Capture/            # 屏幕捕获
│   ├── Encode/             # 视频编码
│   └── Display/            # 显示管理
├── ExpandScreen.Services    # 服务层
│   ├── Connection/         # 连接管理
│   └── Network/            # 网络传输
├── ExpandScreen.Protocol    # 通信协议
│   └── Messages/           # 消息定义
└── ExpandScreen.Utils       # 工具类库
    ├── Logging/            # 日志工具
    └── Extensions/         # 扩展方法
```

### 分层架构

```
┌─────────────────────────────┐
│      UI Layer (WPF)         │
├─────────────────────────────┤
│    Services Layer           │
├─────────────────────────────┤
│    Core Logic Layer         │
├─────────────────────────────┤
│    Protocol Layer           │
├─────────────────────────────┤
│    Utilities Layer          │
└─────────────────────────────┘
```

## 编码规范

### C# 代码规范

#### 命名规范

- **类名**：PascalCase （例：`ScreenCapture`）
- **接口名**：PascalCase + I前缀（例：`IScreenCapture`）
- **方法名**：PascalCase（例：`CaptureFrame`）
- **属性名**：PascalCase（例：`IsConnected`）
- **私有字段**：camelCase + _前缀（例：`_frameBuffer`）
- **常量**：PascalCase（例：`MaxFrameRate`）

#### 示例代码

```csharp
namespace ExpandScreen.Core.Capture
{
    /// <summary>
    /// 屏幕捕获实现
    /// </summary>
    public class ScreenCapture : IScreenCapture
    {
        private readonly ILogger _logger;
        private bool _isRunning;

        public bool IsRunning => _isRunning;

        public ScreenCapture(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void Start()
        {
            if (_isRunning)
            {
                _logger.LogWarning("Screen capture is already running");
                return;
            }

            _isRunning = true;
            _logger.LogInformation("Screen capture started");
        }

        public void Stop()
        {
            _isRunning = false;
            _logger.LogInformation("Screen capture stopped");
        }
    }
}
```

### 注释规范

使用XML文档注释：

```csharp
/// <summary>
/// 捕获屏幕帧
/// </summary>
/// <param name="timeout">超时时间（毫秒）</param>
/// <returns>捕获的帧数据，失败返回null</returns>
/// <exception cref="InvalidOperationException">捕获未启动时抛出</exception>
public byte[]? CaptureFrame(int timeout = 1000)
{
    // 实现代码
}
```

## 构建和调试

### 构建配置

项目提供两种构建配置：

1. **Debug**：包含调试信息，日志级别为Debug
2. **Release**：优化代码，日志级别为Information

```bash
# Debug构建
dotnet build ExpandScreen.sln --configuration Debug

# Release构建
dotnet build ExpandScreen.sln --configuration Release
```

### 发布构建（Windows）

Windows 发布包/安装包的生成流程见：`docs/developer/RELEASE.md`。

### 调试技巧

#### Visual Studio调试

1. 设置断点：在代码行左侧点击或按F9
2. 开始调试：按F5
3. 单步执行：F10（跨过）/ F11（进入）
4. 查看变量：悬停在变量上或使用监视窗口

#### 日志调试

```csharp
// 使用Serilog记录日志
Log.Debug("Capturing frame {FrameNumber}", frameNumber);
Log.Information("Connection established to {DeviceId}", deviceId);
Log.Warning("Frame dropped due to timeout");
Log.Error(ex, "Failed to encode frame");
```

日志文件位置：`%LOCALAPPDATA%\\ExpandScreen\\logs\\expandscreen-<date>.log`

### 性能分析

使用Visual Studio性能分析工具：

1. 调试 → 性能探查器
2. 选择分析工具：CPU使用率、内存使用率
3. 启动应用并执行操作
4. 停止分析，查看报告

## 测试指南

### 单元测试

使用xUnit框架编写单元测试：

```csharp
public class ScreenCaptureTests
{
    [Fact]
    public void Start_ShouldSetIsRunningToTrue()
    {
        // Arrange
        var logger = new Mock<ILogger>();
        var capture = new ScreenCapture(logger.Object);

        // Act
        capture.Start();

        // Assert
        Assert.True(capture.IsRunning);
    }
}
```

运行测试：

```bash
dotnet test ExpandScreen.sln --verbosity normal
```

### 集成测试

端到端测试流程：

1. 启动Windows客户端
2. 连接Android设备（USB或WiFi）
3. 验证屏幕显示
4. 测试触控操作
5. 检查性能指标

## 常见问题

### Q: 构建失败，提示找不到.NET 8.0 SDK

**A:** 确保已安装.NET 8.0 SDK：

```bash
dotnet --version  # 应显示8.0.x
```

如未安装，从 https://dotnet.microsoft.com/ 下载安装。

### Q: NuGet包还原失败

**A:** 尝试以下步骤：

```bash
# 清理NuGet缓存
dotnet nuget locals all --clear

# 重新还原包
dotnet restore ExpandScreen.sln
```

### Q: 如何切换日志级别

**A:** 推荐通过配置文件设置（支持热更新）：

编辑配置文件（设置页“关于”可看到路径），增加/修改：

```json
{
  "logging": {
    "minimumLevel": "Information"
  }
}
```

### Q: Visual Studio无法加载项目

**A:** 检查是否安装了必需的工作负载：

1. 打开Visual Studio Installer
2. 修改现有安装
3. 确保勾选".NET桌面开发"

## 获取帮助

- **文档**：查看 [docs/](../docs/) 目录下的其他文档
- **开发者文档**：`docs/developer/README.md`
- **问题反馈**：提交Issue到GitHub
- **讨论**：参与GitHub Discussions

## 更新日志

- **2026-01-22**：初始版本，添加项目架构和基础开发指南
