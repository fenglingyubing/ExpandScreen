using ExpandScreen.Utils;

namespace ExpandScreen.Services.Connection.Tests
{
    /// <summary>
    /// USB连接测试类
    /// </summary>
    public class UsbConnectionTests
    {
        /// <summary>
        /// 测试ADB工具和设备发现
        /// </summary>
        public static async Task TestDeviceDiscoveryAsync()
        {
            LogHelper.Info("=== Starting Device Discovery Test ===");

            try
            {
                var adbHelper = new AdbHelper();
                var discoveryService = new DeviceDiscoveryService(adbHelper);

                // 订阅事件
                discoveryService.DeviceConnected += (sender, device) =>
                {
                    LogHelper.Info($"[Event] Device Connected: {device}");
                };

                discoveryService.DeviceDisconnected += (sender, device) =>
                {
                    LogHelper.Info($"[Event] Device Disconnected: {device}");
                };

                // 启动扫描
                discoveryService.Start(3000);

                LogHelper.Info("Device discovery started. Scanning for 10 seconds...");
                await Task.Delay(10000);

                // 获取发现的设备
                var devices = await discoveryService.GetDiscoveredDevicesAsync();
                LogHelper.Info($"Found {devices.Count} device(s):");

                foreach (var device in devices)
                {
                    LogHelper.Info($"  - {device.DeviceName} (ID: {device.DeviceId})");
                    LogHelper.Info($"    Model: {device.Model}");
                    LogHelper.Info($"    Manufacturer: {device.Manufacturer}");
                    LogHelper.Info($"    Android: {device.AndroidVersion} (SDK {device.SdkVersion})");
                    LogHelper.Info($"    Status: {device.Status}");
                    LogHelper.Info($"    Authorized: {device.IsAuthorized}");
                }

                // 停止扫描
                discoveryService.Stop();
                discoveryService.Dispose();

                LogHelper.Info("=== Device Discovery Test Completed ===");
            }
            catch (Exception ex)
            {
                LogHelper.Error("Device discovery test failed", ex);
            }
        }

        /// <summary>
        /// 测试USB连接
        /// </summary>
        public static async Task TestUsbConnectionAsync(string deviceId)
        {
            LogHelper.Info($"=== Starting USB Connection Test (Device: {deviceId}) ===");

            try
            {
                var connection = new UsbConnection();

                // 订阅事件
                connection.ConnectionStatusChanged += (sender, status) =>
                {
                    LogHelper.Info($"[Event] Connection Status: {status}");
                };

                connection.ConnectionError += (sender, ex) =>
                {
                    LogHelper.Error($"[Event] Connection Error: {ex.Message}");
                };

                // 连接设备
                LogHelper.Info("Attempting to connect...");
                bool connected = await connection.ConnectAsync(deviceId);

                if (connected)
                {
                    LogHelper.Info("Connection successful!");
                    LogHelper.Info($"IsConnected: {connection.IsConnected}");

                    // 模拟发送数据（测试数据）
                    LogHelper.Info("Testing data send...");
                    byte[] testData = new byte[] { 0x01, 0x02, 0x03, 0x04 };
                    try
                    {
                        await connection.SendAsync(testData);
                        LogHelper.Info("Data sent successfully");
                    }
                    catch (Exception ex)
                    {
                        LogHelper.Warning($"Send test skipped (expected if no server listening): {ex.Message}");
                    }

                    // 等待一段时间
                    await Task.Delay(5000);

                    // 断开连接
                    LogHelper.Info("Disconnecting...");
                    await connection.DisconnectAsync();
                    LogHelper.Info("Disconnected successfully");
                }
                else
                {
                    LogHelper.Error("Failed to connect");
                }

                connection.Dispose();
                LogHelper.Info("=== USB Connection Test Completed ===");
            }
            catch (Exception ex)
            {
                LogHelper.Error("USB connection test failed", ex);
            }
        }

        /// <summary>
        /// 测试自动重连
        /// </summary>
        public static async Task TestAutoReconnectAsync(string deviceId)
        {
            LogHelper.Info($"=== Starting Auto-Reconnect Test (Device: {deviceId}) ===");

            try
            {
                var connection = new UsbConnection();
                connection.SetAutoReconnect(true);

                // 订阅事件
                connection.ConnectionStatusChanged += (sender, status) =>
                {
                    LogHelper.Info($"[Event] Connection Status: {status}");
                };

                // 连接设备
                LogHelper.Info("Connecting...");
                bool connected = await connection.ConnectAsync(deviceId);

                if (connected)
                {
                    LogHelper.Info("Connected! Now disconnect the device to test auto-reconnect...");
                    LogHelper.Info("Waiting 30 seconds for reconnect test...");
                    await Task.Delay(30000);

                    await connection.DisconnectAsync();
                }

                connection.Dispose();
                LogHelper.Info("=== Auto-Reconnect Test Completed ===");
            }
            catch (Exception ex)
            {
                LogHelper.Error("Auto-reconnect test failed", ex);
            }
        }

        /// <summary>
        /// 运行所有测试
        /// </summary>
        public static async Task RunAllTestsAsync()
        {
            LogHelper.Info("========================================");
            LogHelper.Info("  USB/ADB Connection Module Tests");
            LogHelper.Info("========================================");
            LogHelper.Info("");

            // 测试1: 设备发现
            await TestDeviceDiscoveryAsync();
            LogHelper.Info("");

            // 获取第一个可用设备进行连接测试
            try
            {
                var adbHelper = new AdbHelper();
                var devices = await adbHelper.GetDevicesAsync();

                if (devices.Count > 0)
                {
                    string deviceId = devices[0].DeviceId;

                    // 测试2: USB连接
                    await TestUsbConnectionAsync(deviceId);
                    LogHelper.Info("");

                    // 测试3: 自动重连（可选，需要手动断开设备）
                    // await TestAutoReconnectAsync(deviceId);
                }
                else
                {
                    LogHelper.Warning("No devices found. Skipping connection tests.");
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error("Error running connection tests", ex);
            }

            LogHelper.Info("========================================");
            LogHelper.Info("  All Tests Completed");
            LogHelper.Info("========================================");
        }
    }
}
