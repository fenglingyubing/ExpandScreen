using System.Net.Sockets;
using ExpandScreen.Utils;

namespace ExpandScreen.Services.Connection
{
    /// <summary>
    /// USB连接实现类
    /// </summary>
    public class UsbConnection : IConnectionManager, IDisposable
    {
        private readonly AdbHelper _adbHelper;
        private readonly UsbConnectionOptions _options;
        private TcpClient? _tcpClient;
        private NetworkStream? _networkStream;
        private string? _currentDeviceId;
        private int _localPort; // 本地转发端口
        private int _remotePort; // 远程端口
        private bool _disposed;

        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private CancellationTokenSource? _reconnectCts;

        private bool _autoReconnectEnabled = true;

        public bool IsConnected => IsTcpClientConnected(_tcpClient);

        public event EventHandler<string>? ConnectionStatusChanged;
        public event EventHandler<Exception>? ConnectionError;

        public UsbConnection(AdbHelper? adbHelper = null, UsbConnectionOptions? options = null)
            : this(15555, 15555, adbHelper, options)
        {
        }

        public UsbConnection(int localPort, int remotePort, AdbHelper? adbHelper = null, UsbConnectionOptions? options = null)
        {
            _adbHelper = adbHelper ?? new AdbHelper();
            _options = options ?? new UsbConnectionOptions();
            _localPort = localPort;
            _remotePort = remotePort;
            LogHelper.Info($"UsbConnection initialized (localPort={_localPort}, remotePort={_remotePort})");
        }

        /// <summary>
        /// 连接到设备
        /// </summary>
        public async Task<bool> ConnectAsync(string deviceId)
        {
            return await ConnectInternalAsync(deviceId, isReconnect: false);
        }

        private async Task<bool> ConnectInternalAsync(string deviceId, bool isReconnect)
        {
            try
            {
                LogHelper.Info($"Attempting to connect to device: {deviceId}");

                // 检查设备是否已连接
                if (!await _adbHelper.IsDeviceConnectedAsync(deviceId))
                {
                    LogHelper.Warning($"Device not found or not authorized: {deviceId}");
                    OnConnectionStatusChanged("Device not found or not authorized");
                    return false;
                }

                // 断开现有连接（重连场景不要停掉重连监控）
                if (_tcpClient != null || _networkStream != null)
                {
                    await DisconnectInternalAsync(stopReconnectMonitor: !isReconnect);
                }

                _currentDeviceId = deviceId;

                // 执行端口转发
                if (!await _adbHelper.ForwardPortAsync(deviceId, _localPort, _remotePort))
                {
                    LogHelper.Error("Failed to establish port forwarding");
                    OnConnectionStatusChanged("Port forwarding failed");
                    return false;
                }

                // 建立TCP连接
                _tcpClient = new TcpClient();
                _tcpClient.NoDelay = true; // 禁用Nagle算法以降低延迟
                _tcpClient.SendBufferSize = 256 * 1024; // 256KB发送缓冲区
                _tcpClient.ReceiveBufferSize = 256 * 1024; // 256KB接收缓冲区

                await _tcpClient.ConnectAsync("127.0.0.1", _localPort);
                _networkStream = _tcpClient.GetStream();

                LogHelper.Info($"Successfully connected to device: {deviceId}");
                OnConnectionStatusChanged("Connected");

                // 启动自动重连监控
                if (_autoReconnectEnabled)
                {
                    StartReconnectMonitor();
                }

                return true;
            }
            catch (Exception ex)
            {
                LogHelper.Error($"Error connecting to device: {deviceId}", ex);
                OnConnectionError(ex);
                OnConnectionStatusChanged($"Connection failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 断开连接
        /// </summary>
        public async Task DisconnectAsync()
        {
            await DisconnectInternalAsync(stopReconnectMonitor: true);
        }

        private async Task DisconnectInternalAsync(bool stopReconnectMonitor)
        {
            try
            {
                LogHelper.Info("Disconnecting...");

                // 停止重连监控
                if (stopReconnectMonitor)
                {
                    StopReconnectMonitor();
                }

                // 关闭TCP连接
                if (_networkStream != null)
                {
                    await _networkStream.DisposeAsync();
                    _networkStream = null;
                }

                if (_tcpClient != null)
                {
                    _tcpClient.Close();
                    _tcpClient.Dispose();
                    _tcpClient = null;
                }

                // 移除端口转发
                if (_currentDeviceId != null)
                {
                    await _adbHelper.RemoveForwardAsync(_currentDeviceId, _localPort);
                }

                LogHelper.Info("Disconnected successfully");
                OnConnectionStatusChanged("Disconnected");
            }
            catch (Exception ex)
            {
                LogHelper.Error("Error during disconnect", ex);
            }
            finally
            {
                _currentDeviceId = null;
            }
        }

        /// <summary>
        /// 发送数据
        /// </summary>
        public async Task SendAsync(byte[] data)
        {
            if (!IsConnected || _networkStream == null)
            {
                throw new InvalidOperationException("Not connected to any device");
            }

            await _sendLock.WaitAsync();
            try
            {
                await _networkStream.WriteAsync(data);
                await _networkStream.FlushAsync();
                LogHelper.Debug($"Sent {data.Length} bytes");
            }
            catch (Exception ex)
            {
                LogHelper.Error("Error sending data", ex);
                OnConnectionError(ex);
                throw;
            }
            finally
            {
                _sendLock.Release();
            }
        }

        /// <summary>
        /// 接收数据
        /// </summary>
        public async Task<int> ReceiveAsync(byte[] buffer, CancellationToken cancellationToken = default)
        {
            if (!IsConnected || _networkStream == null)
            {
                throw new InvalidOperationException("Not connected to any device");
            }

            try
            {
                int bytesRead = await _networkStream.ReadAsync(buffer, cancellationToken);
                if (bytesRead > 0)
                {
                    LogHelper.Debug($"Received {bytesRead} bytes");
                }
                return bytesRead;
            }
            catch (Exception ex)
            {
                LogHelper.Error("Error receiving data", ex);
                OnConnectionError(ex);
                throw;
            }
        }

        /// <summary>
        /// 启动重连监控
        /// </summary>
        private void StartReconnectMonitor()
        {
            if (_reconnectCts != null)
            {
                return;
            }

            _reconnectCts = new CancellationTokenSource();
            _ = Task.Run(async () => await ReconnectMonitorAsync(_reconnectCts.Token));
        }

        /// <summary>
        /// 停止重连监控
        /// </summary>
        private void StopReconnectMonitor()
        {
            if (_reconnectCts != null)
            {
                _reconnectCts.Cancel();
                _reconnectCts.Dispose();
                _reconnectCts = null;
            }
        }

        /// <summary>
        /// 重连监控循环
        /// </summary>
        private async Task ReconnectMonitorAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_options.MonitorIntervalMs, cancellationToken);

                    // 检查连接状态
                    if (!IsConnected && _currentDeviceId != null)
                    {
                        LogHelper.Warning("Connection lost, attempting to reconnect...");
                        await AttemptReconnectAsync(cancellationToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    LogHelper.Error("Error in reconnect monitor", ex);
                }
            }
        }

        /// <summary>
        /// 尝试重新连接
        /// </summary>
        private async Task AttemptReconnectAsync(CancellationToken cancellationToken)
        {
            if (_currentDeviceId == null)
            {
                return;
            }

            string deviceId = _currentDeviceId;

            for (int attempt = 1; attempt <= _options.MaxReconnectAttempts; attempt++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                LogHelper.Info($"Reconnect attempt {attempt}/{_options.MaxReconnectAttempts}");
                OnConnectionStatusChanged($"Reconnecting (attempt {attempt}/{_options.MaxReconnectAttempts})");

                bool success = await ConnectInternalAsync(deviceId, isReconnect: true);
                if (success)
                {
                    LogHelper.Info("Reconnection successful");
                    OnConnectionStatusChanged("Reconnected");
                    return;
                }

                if (attempt < _options.MaxReconnectAttempts)
                {
                    await Task.Delay(_options.ReconnectDelayMs, cancellationToken);
                }
            }

            LogHelper.Error("Failed to reconnect after maximum attempts");
            OnConnectionStatusChanged("Reconnection failed");
        }

        private static bool IsTcpClientConnected(TcpClient? client)
        {
            if (client == null)
            {
                return false;
            }

            try
            {
                if (!client.Connected)
                {
                    return false;
                }

                var socket = client.Client;
                if (socket == null)
                {
                    return false;
                }

                if (socket.Poll(0, SelectMode.SelectRead) && socket.Available == 0)
                {
                    return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 设置端口
        /// </summary>
        public void SetPorts(int localPort, int remotePort)
        {
            if (IsConnected)
            {
                throw new InvalidOperationException("Cannot change ports while connected");
            }

            _localPort = localPort;
            _remotePort = remotePort;
            LogHelper.Info($"Ports set to: local={localPort}, remote={remotePort}");
        }

        /// <summary>
        /// 启用/禁用自动重连
        /// </summary>
        public void SetAutoReconnect(bool enabled)
        {
            _autoReconnectEnabled = enabled;
            LogHelper.Info($"Auto-reconnect {(enabled ? "enabled" : "disabled")}");

            if (enabled && IsConnected)
            {
                StartReconnectMonitor();
            }
            else if (!enabled)
            {
                StopReconnectMonitor();
            }
        }

        private void OnConnectionStatusChanged(string status)
        {
            ConnectionStatusChanged?.Invoke(this, status);
        }

        private void OnConnectionError(Exception ex)
        {
            ConnectionError?.Invoke(this, ex);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                StopReconnectMonitor();
                DisconnectAsync().GetAwaiter().GetResult();
                _sendLock.Dispose();
                _disposed = true;
            }

            GC.SuppressFinalize(this);
        }
    }
}
