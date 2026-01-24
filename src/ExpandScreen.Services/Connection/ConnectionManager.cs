using ExpandScreen.Core.Encode;
using ExpandScreen.Services.Driver;
using ExpandScreen.Utils;

namespace ExpandScreen.Services.Connection
{
    /// <summary>
    /// 多设备连接管理器：为每个设备维护独立会话（连接/编码器/虚拟显示）。
    /// </summary>
    public sealed class ConnectionManager : IMultiConnectionManager
    {
        private sealed class DeviceSession : IDisposable
        {
            private bool _disposed;

            public DeviceSession(
                string deviceId,
                IConnectionManager connection,
                VideoEncodingService encodingService,
                int localPort,
                int remotePort,
                SessionVideoProfile profile)
            {
                DeviceId = deviceId;
                Connection = connection;
                EncodingService = encodingService;
                LocalPort = localPort;
                RemotePort = remotePort;
                Profile = profile;
            }

            public string DeviceId { get; }
            public IConnectionManager Connection { get; }
            public VideoEncodingService EncodingService { get; }
            public int LocalPort { get; }
            public int RemotePort { get; }
            public SessionVideoProfile Profile { get; }
            public uint? MonitorId { get; set; }
            public string? LastError { get; set; }
            public DeviceSessionState State { get; set; } = DeviceSessionState.Disconnected;

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                try
                {
                    Connection.DisconnectAsync().GetAwaiter().GetResult();
                }
                catch
                {
                }

                try
                {
                    EncodingService.Dispose();
                }
                catch
                {
                }

                if (Connection is IDisposable disposable)
                {
                    try
                    {
                        disposable.Dispose();
                    }
                    catch
                    {
                    }
                }

                _disposed = true;
                GC.SuppressFinalize(this);
            }
        }

        private readonly ConnectionManagerOptions _options;
        private readonly ILocalPortAllocator _portAllocator;
        private readonly Func<int, int, IConnectionManager> _connectionFactory;
        private readonly Func<IVirtualDisplayDriver?> _virtualDisplayDriverFactory;

        private readonly SemaphoreSlim _sessionsLock = new(1, 1);
        private readonly Dictionary<string, DeviceSession> _sessions = new();

        private IVirtualDisplayDriver? _virtualDisplayDriver;
        private bool _disposed;

        public IReadOnlyCollection<DeviceSessionSnapshot> Sessions => SnapshotSessions();

        public event EventHandler<DeviceSessionSnapshot>? SessionUpdated;

        public ConnectionManager(
            ConnectionManagerOptions? options = null,
            ILocalPortAllocator? portAllocator = null,
            Func<int, int, IConnectionManager>? connectionFactory = null,
            Func<IVirtualDisplayDriver?>? virtualDisplayDriverFactory = null)
        {
            _options = options ?? new ConnectionManagerOptions();
            _portAllocator = portAllocator ?? new LocalPortAllocator();
            _connectionFactory = connectionFactory ?? ((localPort, remotePort) => new UsbConnection(localPort, remotePort));
            _virtualDisplayDriverFactory = virtualDisplayDriverFactory ?? (() => new ExpandScreenVirtualDisplayDriver());
        }

        public async Task<ConnectDeviceResult> ConnectAsync(string deviceId, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (string.IsNullOrWhiteSpace(deviceId))
            {
                return new ConnectDeviceResult(false, null, false, "deviceId is required.");
            }

            DeviceSession session;
            int maxSessions = _options.DefaultMaxSessions;
            bool useDegradedProfile;

            await _sessionsLock.WaitAsync(cancellationToken);
            try
            {
                if (_sessions.TryGetValue(deviceId, out var existing))
                {
                    if (existing.State == DeviceSessionState.Connected)
                    {
                        return new ConnectDeviceResult(true, CreateSnapshot(existing), false, null);
                    }

                    _sessions.Remove(deviceId);
                    existing.Dispose();
                }

                maxSessions = TryGetDriverMaxMonitorsOrDefault(maxSessions);
                if (_sessions.Count >= maxSessions)
                {
                    return new ConnectDeviceResult(false, null, false, $"连接数已达上限（{maxSessions}）。");
                }

                useDegradedProfile = _sessions.Count >= _options.MaxHighQualitySessions;
                var profile = useDegradedProfile ? _options.DegradedProfile : _options.PrimaryProfile;

                int localPort = _portAllocator.AllocateEphemeralPort();
                int remotePort = _options.RemotePort;

                IConnectionManager connection = _connectionFactory(localPort, remotePort);

                var encoder = _options.EncoderFactory(profile) ?? new NoopVideoEncoder();
                encoder.Initialize(profile.Width, profile.Height, profile.RefreshRate, profile.BitrateBps);

                var encodingService = new VideoEncodingService(encoder);

                session = new DeviceSession(deviceId, connection, encodingService, localPort, remotePort, profile)
                {
                    State = DeviceSessionState.Connecting
                };

                _sessions[deviceId] = session;
            }
            finally
            {
                _sessionsLock.Release();
            }

            EmitSessionUpdated(CreateSnapshot(session));

            uint? monitorId = null;
            if (_options.EnableVirtualDisplays)
            {
                monitorId = TryCreateVirtualMonitor(session);
            }

            session.MonitorId = monitorId;
            EmitSessionUpdated(CreateSnapshot(session));

            try
            {
                bool connected = await session.Connection.ConnectAsync(deviceId);
                if (!connected)
                {
                    await FailAndRemoveSessionAsync(deviceId, "连接失败（返回 false）");
                    return new ConnectDeviceResult(false, null, useDegradedProfile, "连接失败。");
                }

                DeviceSessionSnapshot? connectedSnapshot = null;
                await _sessionsLock.WaitAsync(cancellationToken);
                try
                {
                    if (_sessions.TryGetValue(deviceId, out var current))
                    {
                        current.State = DeviceSessionState.Connected;
                        current.LastError = null;
                        connectedSnapshot = CreateSnapshot(current);
                    }
                }
                finally
                {
                    _sessionsLock.Release();
                }

                if (connectedSnapshot == null)
                {
                    return new ConnectDeviceResult(false, null, useDegradedProfile, "会话已被移除。");
                }

                EmitSessionUpdated(connectedSnapshot);
                return new ConnectDeviceResult(true, connectedSnapshot, useDegradedProfile, null);
            }
            catch (Exception ex)
            {
                await FailAndRemoveSessionAsync(deviceId, ex.Message);
                return new ConnectDeviceResult(false, null, useDegradedProfile, ex.Message);
            }
        }

        public async Task<bool> DisconnectAsync(string deviceId)
        {
            ThrowIfDisposed();

            if (string.IsNullOrWhiteSpace(deviceId))
            {
                return false;
            }

            DeviceSession? session = null;

            await _sessionsLock.WaitAsync();
            try
            {
                if (_sessions.TryGetValue(deviceId, out session))
                {
                    _sessions.Remove(deviceId);
                }
            }
            finally
            {
                _sessionsLock.Release();
            }

            if (session == null)
            {
                return false;
            }

            try
            {
                await session.Connection.DisconnectAsync();
            }
            catch (Exception ex)
            {
                LogHelper.Debug($"Disconnect failed for {deviceId}: {ex.Message}");
            }

            if (session.MonitorId.HasValue)
            {
                TryDestroyVirtualMonitor(session.MonitorId.Value);
            }

            session.State = DeviceSessionState.Disconnected;
            EmitSessionUpdated(CreateSnapshot(session));

            session.Dispose();
            return true;
        }

        public async Task DisconnectAllAsync()
        {
            ThrowIfDisposed();

            List<string> deviceIds;
            await _sessionsLock.WaitAsync();
            try
            {
                deviceIds = _sessions.Keys.ToList();
            }
            finally
            {
                _sessionsLock.Release();
            }

            foreach (var deviceId in deviceIds)
            {
                await DisconnectAsync(deviceId);
            }
        }

        private uint? TryCreateVirtualMonitor(DeviceSession session)
        {
            try
            {
                var driver = GetOrCreateVirtualDisplayDriver();
                if (driver == null || !driver.IsAvailable)
                {
                    return null;
                }

                return driver.CreateMonitor(
                    width: (uint)session.Profile.Width,
                    height: (uint)session.Profile.Height,
                    refreshRate: (uint)session.Profile.RefreshRate);
            }
            catch (Exception ex)
            {
                LogHelper.Warning($"CreateMonitor failed: {ex.Message}");
                return null;
            }
        }

        private void TryDestroyVirtualMonitor(uint monitorId)
        {
            try
            {
                var driver = GetOrCreateVirtualDisplayDriver();
                _ = driver?.TryDestroyMonitor(monitorId);
            }
            catch
            {
            }
        }

        private async Task FailAndRemoveSessionAsync(string deviceId, string error)
        {
            DeviceSession? session = null;
            await _sessionsLock.WaitAsync();
            try
            {
                if (_sessions.TryGetValue(deviceId, out session))
                {
                    _sessions.Remove(deviceId);
                }
            }
            finally
            {
                _sessionsLock.Release();
            }

            if (session == null)
            {
                return;
            }

            session.State = DeviceSessionState.Error;
            session.LastError = error;
            EmitSessionUpdated(CreateSnapshot(session));

            if (session.MonitorId.HasValue)
            {
                TryDestroyVirtualMonitor(session.MonitorId.Value);
            }

            session.Dispose();
        }

        private int TryGetDriverMaxMonitorsOrDefault(int fallback)
        {
            try
            {
                var driver = GetOrCreateVirtualDisplayDriver();
                if (driver == null || !driver.IsAvailable)
                {
                    return fallback;
                }

                var (_, max) = driver.GetAdapterInfo();
                return max > 0 ? (int)max : fallback;
            }
            catch
            {
                return fallback;
            }
        }

        private IVirtualDisplayDriver? GetOrCreateVirtualDisplayDriver()
        {
            if (!_options.EnableVirtualDisplays)
            {
                return null;
            }

            _virtualDisplayDriver ??= _virtualDisplayDriverFactory();
            return _virtualDisplayDriver;
        }

        private IReadOnlyCollection<DeviceSessionSnapshot> SnapshotSessions()
        {
            _sessionsLock.Wait();
            try
            {
                return _sessions.Values.Select(CreateSnapshot).ToList();
            }
            finally
            {
                _sessionsLock.Release();
            }
        }

        private static DeviceSessionSnapshot CreateSnapshot(DeviceSession session)
        {
            return new DeviceSessionSnapshot(
                session.DeviceId,
                session.State,
                session.LocalPort,
                session.RemotePort,
                session.MonitorId,
                session.Profile,
                session.LastError);
        }

        private void EmitSessionUpdated(DeviceSessionSnapshot snapshot)
        {
            SessionUpdated?.Invoke(this, snapshot);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ConnectionManager));
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            DisconnectAllAsync().GetAwaiter().GetResult();

            _virtualDisplayDriver?.Dispose();
            _virtualDisplayDriver = null;

            _sessionsLock.Dispose();
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
