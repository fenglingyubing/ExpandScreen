using ExpandScreen.Utils;

namespace ExpandScreen.Services.Connection
{
    /// <summary>
    /// 设备发现服务
    /// </summary>
    public class DeviceDiscoveryService : IDisposable
    {
        private readonly AdbHelper _adbHelper;
        private readonly Dictionary<string, AndroidDevice> _discoveredDevices = new();
        private readonly SemaphoreSlim _devicesLock = new(1, 1);

        private Timer? _scanTimer;
        private bool _isRunning;
        private bool _disposed;
        private int _scanIntervalMs = 3000; // 默认3秒扫描一次

        public event EventHandler<AndroidDevice>? DeviceConnected;
        public event EventHandler<AndroidDevice>? DeviceDisconnected;
        public event EventHandler<AndroidDevice>? DeviceUpdated;

        public bool IsRunning => _isRunning;

        public DeviceDiscoveryService(AdbHelper? adbHelper = null)
        {
            _adbHelper = adbHelper ?? new AdbHelper();
            LogHelper.Info("DeviceDiscoveryService initialized");
        }

        /// <summary>
        /// 启动设备扫描
        /// </summary>
        public void Start(int scanIntervalMs = 3000)
        {
            if (_isRunning)
            {
                LogHelper.Warning("DeviceDiscoveryService is already running");
                return;
            }

            _scanIntervalMs = scanIntervalMs;
            _isRunning = true;

            // 立即执行一次扫描
            _ = Task.Run(ScanDevicesAsync);

            // 启动定时器
            _scanTimer = new Timer(
                async _ => await ScanDevicesAsync(),
                null,
                TimeSpan.FromMilliseconds(_scanIntervalMs),
                TimeSpan.FromMilliseconds(_scanIntervalMs));

            LogHelper.Info($"DeviceDiscoveryService started (scan interval: {_scanIntervalMs}ms)");
        }

        /// <summary>
        /// 停止设备扫描
        /// </summary>
        public void Stop()
        {
            if (!_isRunning)
            {
                return;
            }

            _isRunning = false;

            if (_scanTimer != null)
            {
                _scanTimer.Dispose();
                _scanTimer = null;
            }

            LogHelper.Info("DeviceDiscoveryService stopped");
        }

        /// <summary>
        /// 扫描设备
        /// </summary>
        private async Task ScanDevicesAsync()
        {
            try
            {
                LogHelper.Debug("Scanning for devices...");

                // 获取当前连接的设备
                var currentDevices = await _adbHelper.GetDevicesAsync();

                await _devicesLock.WaitAsync();
                try
                {
                    var currentDeviceIds = currentDevices.Select(d => d.DeviceId).ToHashSet();
                    var previousDeviceIds = _discoveredDevices.Keys.ToHashSet();

                    // 检测新连接的设备
                    foreach (var device in currentDevices)
                    {
                        if (!previousDeviceIds.Contains(device.DeviceId))
                        {
                            // 新设备连接
                            LogHelper.Info($"New device connected: {device.DeviceId}");

                            // 获取设备详细信息
                            var detailedDevice = await _adbHelper.GetDeviceInfoAsync(device.DeviceId);
                            if (detailedDevice != null)
                            {
                                _discoveredDevices[device.DeviceId] = detailedDevice;
                                OnDeviceConnected(detailedDevice);
                            }
                        }
                        else
                        {
                            // 更新现有设备
                            var existingDevice = _discoveredDevices[device.DeviceId];
                            if (existingDevice.Status != device.Status)
                            {
                                LogHelper.Info($"Device status changed: {device.DeviceId} ({existingDevice.Status} -> {device.Status})");
                                existingDevice.Status = device.Status;
                                existingDevice.LastSeen = DateTime.Now;
                                OnDeviceUpdated(existingDevice);
                            }
                            else
                            {
                                // 更新最后见到的时间
                                existingDevice.LastSeen = DateTime.Now;
                            }
                        }
                    }

                    // 检测断开的设备
                    var disconnectedDeviceIds = previousDeviceIds.Except(currentDeviceIds).ToList();
                    foreach (var deviceId in disconnectedDeviceIds)
                    {
                        LogHelper.Info($"Device disconnected: {deviceId}");
                        var device = _discoveredDevices[deviceId];
                        _discoveredDevices.Remove(deviceId);
                        OnDeviceDisconnected(device);
                    }

                    if (currentDevices.Count > 0)
                    {
                        LogHelper.Debug($"Found {currentDevices.Count} device(s)");
                    }
                }
                finally
                {
                    _devicesLock.Release();
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error("Error scanning devices", ex);
            }
        }

        /// <summary>
        /// 获取所有已发现的设备
        /// </summary>
        public async Task<List<AndroidDevice>> GetDiscoveredDevicesAsync()
        {
            await _devicesLock.WaitAsync();
            try
            {
                return _discoveredDevices.Values.ToList();
            }
            finally
            {
                _devicesLock.Release();
            }
        }

        /// <summary>
        /// 获取特定设备
        /// </summary>
        public async Task<AndroidDevice?> GetDeviceAsync(string deviceId)
        {
            await _devicesLock.WaitAsync();
            try
            {
                return _discoveredDevices.TryGetValue(deviceId, out var device) ? device : null;
            }
            finally
            {
                _devicesLock.Release();
            }
        }

        /// <summary>
        /// 刷新特定设备信息
        /// </summary>
        public async Task<AndroidDevice?> RefreshDeviceInfoAsync(string deviceId)
        {
            try
            {
                var device = await _adbHelper.GetDeviceInfoAsync(deviceId);

                if (device != null)
                {
                    await _devicesLock.WaitAsync();
                    try
                    {
                        _discoveredDevices[deviceId] = device;
                        OnDeviceUpdated(device);
                    }
                    finally
                    {
                        _devicesLock.Release();
                    }
                }

                return device;
            }
            catch (Exception ex)
            {
                LogHelper.Error($"Error refreshing device info for {deviceId}", ex);
                return null;
            }
        }

        /// <summary>
        /// 手动触发设备扫描
        /// </summary>
        public async Task TriggerScanAsync()
        {
            await ScanDevicesAsync();
        }

        private void OnDeviceConnected(AndroidDevice device)
        {
            DeviceConnected?.Invoke(this, device);
        }

        private void OnDeviceDisconnected(AndroidDevice device)
        {
            DeviceDisconnected?.Invoke(this, device);
        }

        private void OnDeviceUpdated(AndroidDevice device)
        {
            DeviceUpdated?.Invoke(this, device);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                Stop();
                _devicesLock.Dispose();
                _disposed = true;
            }

            GC.SuppressFinalize(this);
        }
    }
}
