# ExpandScreen Virtual Display Driver

## 概述

ExpandScreen.Driver 是一个基于Windows IddCx（Indirect Display Driver）框架的虚拟显示驱动程序。该驱动程序创建虚拟显示器，使Windows系统能够将显示内容扩展到ExpandScreen应用程序。

## 架构

### 主要组件

1. **Driver.cpp** - 驱动入口点和设备管理
   - `DriverEntry`: 驱动初始化
   - `ExpandScreenEvtDeviceAdd`: 设备添加回调
   - 电源管理回调

2. **Adapter.cpp** - IddCx适配器管理
   - 适配器初始化
   - 适配器能力配置
   - 最多支持4个虚拟显示器

3. **Monitor.cpp** - 虚拟监视器管理
   - 监视器创建和销毁
   - 显示模式查询
   - 交换链分配

4. **SwapChain.cpp** - 帧数据处理
   - 获取渲染帧
   - 帧数据传递到用户态

5. **Edid.cpp** - EDID数据生成
   - 生成标准EDID 1.4格式
   - 支持自定义分辨率

6. **Ioctl.cpp** - 用户态通信接口
   - 创建/销毁监视器
   - 查询适配器信息

## 支持的显示模式

驱动支持以下预定义显示模式：

| 分辨率 | 刷新率 |
|--------|--------|
| 1920x1080 | 60Hz |
| 1920x1080 | 120Hz |
| 2560x1600 | 60Hz |
| 1280x720 | 60Hz |
| 3840x2160 | 60Hz |

## IOCTL接口

### IOCTL_EXPANDSCREEN_CREATE_MONITOR (0x800)
创建新的虚拟监视器

**输入**: `EXPANDSCREEN_CREATE_MONITOR_INPUT`
```c
typedef struct {
    UINT Width;
    UINT Height;
    UINT RefreshRate;
} EXPANDSCREEN_CREATE_MONITOR_INPUT;
```

**输出**: `EXPANDSCREEN_CREATE_MONITOR_OUTPUT`
```c
typedef struct {
    UINT MonitorId;
    NTSTATUS Status;
} EXPANDSCREEN_CREATE_MONITOR_OUTPUT;
```

### IOCTL_EXPANDSCREEN_GET_ADAPTER_INFO (0x802)
获取适配器信息

**输出**: `EXPANDSCREEN_ADAPTER_INFO`
```c
typedef struct {
    UINT MonitorCount;
    UINT MaxMonitors;
} EXPANDSCREEN_ADAPTER_INFO;
```

## 编译要求

### 必需工具
- Visual Studio 2022 或更高版本
- Windows Driver Kit (WDK) 10
- Windows SDK 10

### 编译步骤

1. 安装WDK和Windows SDK
2. 打开 `ExpandScreen.sln` 解决方案
3. 选择配置（Debug/Release）和平台（x64）
4. 构建 `ExpandScreen.Driver` 项目

## 安装和部署

### 开发/测试环境（测试签名）

1. 启用测试签名模式：
```cmd
bcdedit /set testsigning on
```

2. 重启计算机

3. 安装驱动：
```cmd
pnputil /add-driver ExpandScreen.inf /install
```

4. 创建设备实例：
```cmd
devcon install ExpandScreen.inf Root\ExpandScreen
```

### 生产环境（正式签名）

1. 获取代码签名证书
2. 使用SignTool签名驱动文件：
```cmd
signtool sign /v /s "My" /n "证书名称" /t http://timestamp.digicert.com ExpandScreen.sys
```

3. 创建驱动包目录文件（.cat）
4. 提交到Microsoft硬件开发中心进行认证

## 卸载

```cmd
pnputil /delete-driver ExpandScreen.inf /uninstall /force
```

## 调试

### 启用WPP跟踪

1. 使用TraceView或logman工具
2. 设置跟踪标志：
   - TRACE_DRIVER (0x00000001)
   - TRACE_ADAPTER (0x00000002)
   - TRACE_MONITOR (0x00000004)
   - TRACE_SWAPCHAIN (0x00000008)
   - TRACE_EDID (0x00000010)
   - TRACE_IOCTL (0x00000020)

### 内核调试

使用WinDbg连接到目标机器进行内核调试。

## 已知问题

1. 当前实现中，SwapChain帧处理是基础版本，需要进一步优化
2. 监视器销毁功能尚未完全实现
3. 需要实现更精细的错误处理和恢复机制

## 安全考虑

- 驱动运行在内核模式，需要严格的输入验证
- 所有用户态输入都经过验证
- 使用PAGED_CODE宏确保可分页代码在正确的IRQL运行

## 性能

- 使用IddCx框架提供的高效帧传递机制
- 支持脏矩形检测以减少不必要的帧处理
- 最小化内核态和用户态之间的数据拷贝

## 许可证

详见项目根目录的LICENSE文件。

## 贡献

欢迎提交问题和拉取请求。在提交代码前，请确保：
- 代码遵循Windows驱动开发最佳实践
- 通过静态代码分析（SDV）
- 添加适当的WPP跟踪点

## 参考资料

- [IddCx Documentation](https://docs.microsoft.com/en-us/windows-hardware/drivers/display/indirect-display-driver-model-overview)
- [Windows Driver Kit Documentation](https://docs.microsoft.com/en-us/windows-hardware/drivers/)
- [EDID Specification](https://en.wikipedia.org/wiki/Extended_Display_Identification_Data)
