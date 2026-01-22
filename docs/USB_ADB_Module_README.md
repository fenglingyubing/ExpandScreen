# USB/ADB 通信模块

## 概述

USB/ADB通信模块提供了通过ADB (Android Debug Bridge) 与Android设备建立USB连接的功能。模块包含设备发现、连接管理、自动重连等核心功能。

## 主要组件

### 1. AdbHelper
封装ADB命令行工具的调用，提供以下功能：
- 执行ADB命令
- 获取设备列表
- 获取设备详细信息
- 端口转发管理
- 设备连接状态检查

### 2. AndroidDevice
表示一个Android设备的信息，包括：
- 设备ID
- 设备名称和型号
- 制造商信息
- Android版本和SDK版本
- 设备状态（device, unauthorized, offline）
- 授权状态

### 3. UsbConnection
实现`IConnectionManager`接口，提供USB连接功能：
- 建立和断开连接
- 数据发送和接收
- 自动重连机制
- 连接状态事件通知

### 4. DeviceDiscoveryService
设备发现服务，自动扫描和管理已连接的设备：
- 定期扫描USB设备
- 设备连接/断开事件通知
- 设备信息缓存和更新

### 5. ConnectionException
连接相关的异常类型

## 使用示例

### 基本使用

```csharp
using ExpandScreen.Services.Connection;

// 1. 创建ADB帮助类
var adbHelper = new AdbHelper();

// 2. 获取设备列表
var devices = await adbHelper.GetDevicesAsync();
foreach (var device in devices)
{
    Console.WriteLine($"Found device: {device.DeviceId}");
}

// 3. 连接到设备
var connection = new UsbConnection(adbHelper);
bool connected = await connection.ConnectAsync(devices[0].DeviceId);

if (connected)
{
    // 4. 发送数据
    byte[] data = new byte[] { 0x01, 0x02, 0x03 };
    await connection.SendAsync(data);

    // 5. 接收数据
    byte[] buffer = new byte[1024];
    int bytesRead = await connection.ReceiveAsync(buffer);

    // 6. 断开连接
    await connection.DisconnectAsync();
}
```

### 使用设备发现服务

```csharp
using ExpandScreen.Services.Connection;

// 创建设备发现服务
var discoveryService = new DeviceDiscoveryService();

// 订阅设备事件
discoveryService.DeviceConnected += (sender, device) =>
{
    Console.WriteLine($"Device connected: {device}");
};

discoveryService.DeviceDisconnected += (sender, device) =>
{
    Console.WriteLine($"Device disconnected: {device}");
};

// 启动扫描（每3秒扫描一次）
discoveryService.Start(3000);

// 获取已发现的设备
var devices = await discoveryService.GetDiscoveredDevicesAsync();

// 停止扫描
discoveryService.Stop();
```

### 启用自动重连

```csharp
var connection = new UsbConnection();
connection.SetAutoReconnect(true); // 启用自动重连

connection.ConnectionStatusChanged += (sender, status) =>
{
    Console.WriteLine($"Connection status: {status}");
};

await connection.ConnectAsync(deviceId);
// 如果连接断开，会自动尝试重连
```

### 自定义端口

```csharp
var connection = new UsbConnection();
connection.SetPorts(localPort: 15555, remotePort: 15555);
await connection.ConnectAsync(deviceId);
```

## 前置条件

### ADB工具安装

模块需要ADB工具才能工作。ADB可执行文件应放置在以下位置之一：

1. 应用程序目录的`adb`子目录：
   ```
   YourApp.exe
   └── adb/
       ├── adb.exe
       ├── AdbWinApi.dll
       └── AdbWinUsbApi.dll
   ```

2. 系统PATH环境变量中的任何目录

3. 通过构造函数指定路径：
   ```csharp
   var adbHelper = new AdbHelper(@"C:\path\to\adb.exe");
   ```

### Android设备配置

1. 在Android设备上启用开发者选项
2. 启用USB调试
3. 通过USB连接设备到电脑
4. 在设备上允许USB调试授权

## 配置说明

### 端口配置
- 默认本地端口：15555
- 默认远程端口：15555
- 可通过`SetPorts()`方法自定义

### 重连配置
- 最大重连尝试次数：5次
- 重连延迟：2000ms
- 重连监控间隔：1000ms

### 扫描配置
- 默认扫描间隔：3000ms
- 可在启动时自定义

## 日志记录

模块使用`ExpandScreen.Utils.LogHelper`进行日志记录。确保在应用程序中正确配置Serilog：

```csharp
using Serilog;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console()
    .WriteTo.File("logs/expandscreen.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();
```

## 错误处理

所有连接相关的错误都会被捕获并记录。主要错误类型：

1. **设备未找到**：设备未连接或未授权
2. **端口转发失败**：ADB端口转发失败
3. **TCP连接失败**：无法建立TCP连接
4. **发送/接收错误**：数据传输错误

通过订阅`ConnectionError`事件可以接收错误通知：

```csharp
connection.ConnectionError += (sender, ex) =>
{
    Console.WriteLine($"Connection error: {ex.Message}");
};
```

## 测试

运行测试类来验证功能：

```csharp
using ExpandScreen.Services.Connection.Tests;

// 运行所有测试
await UsbConnectionTests.RunAllTestsAsync();

// 或单独运行特定测试
await UsbConnectionTests.TestDeviceDiscoveryAsync();
await UsbConnectionTests.TestUsbConnectionAsync("device_id");
```

## 注意事项

1. 确保只有一个应用程序实例使用相同的端口
2. 在应用程序退出前调用`Dispose()`清理资源
3. ADB命令可能需要一定的超时时间，默认为5秒
4. 设备必须启用USB调试并授权当前电脑
5. 某些设备可能需要特定的驱动程序

## 下一步

- 集成到主应用程序中
- 与视频编码模块连接
- 实现完整的数据传输协议
- 添加WiFi连接支持（任务2.2）
