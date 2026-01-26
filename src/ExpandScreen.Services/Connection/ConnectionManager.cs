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

            int maxSessions = _options.DefaultMaxSessions;
            var baseProfile = _options.PrimaryProfile;
            bool baseUsedDegradedProfile = false;
            bool usedDegradedProfile = false;
            DeviceSession? session = null;

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

                baseUsedDegradedProfile = _sessions.Count >= _options.MaxHighQualitySessions;
                baseProfile = baseUsedDegradedProfile ? _options.DegradedProfile : _options.PrimaryProfile;
            }
            finally
            {
                _sessionsLock.Release();
            }

            Exception? lastInitError = null;
            foreach (var profile in BuildCompatibilityFallbackProfiles(baseProfile, _options.DegradedProfile))
            {
                int localPort = _portAllocator.AllocateEphemeralPort();
                int remotePort = _options.RemotePort;
                IConnectionManager connection = _connectionFactory(localPort, remotePort);

                try
                {
                    var encoder = _options.EncoderFactory(profile) ?? new NoopVideoEncoder();
                    encoder.Initialize(profile.Width, profile.Height, profile.RefreshRate, profile.BitrateBps);

                    var encodingService = new VideoEncodingService(encoder);
                    var candidateSession = new DeviceSession(deviceId, connection, encodingService, localPort, remotePort, profile)
                    {
                        State = DeviceSessionState.Connecting
                    };

                    await _sessionsLock.WaitAsync(cancellationToken);
                    try
                    {
                        maxSessions = TryGetDriverMaxMonitorsOrDefault(maxSessions);
                        if (_sessions.Count >= maxSessions)
                        {
                            candidateSession.Dispose();
                            return new ConnectDeviceResult(false, null, false, $"连接数已达上限（{maxSessions}）。");
                        }

                        if (_sessions.ContainsKey(deviceId))
                        {
                            candidateSession.Dispose();
                            return new ConnectDeviceResult(false, null, false, "会话已存在。");
                        }

                        _sessions[deviceId] = candidateSession;
                    }
                    finally
                    {
                        _sessionsLock.Release();
                    }

                    session = candidateSession;
                    usedDegradedProfile = baseUsedDegradedProfile || !Equals(profile, baseProfile);

                    if (usedDegradedProfile && !Equals(profile, baseProfile))
                    {
                        LogHelper.Warning($"兼容性回退：使用 {profile.Summary} 替代 {baseProfile.Summary}");
                    }

                    break;
                }
                catch (Exception ex)
                {
                    lastInitError = ex;
                    LogHelper.Warning($"初始化会话失败（profile={profile.Summary}）：{ex.GetBaseException().Message}");

                    try
                    {
                        if (connection is IDisposable disposable)
                        {
                            disposable.Dispose();
                        }
                    }
                    catch
                    {
                    }
                }
            }

            if (session == null)
            {
                return new ConnectDeviceResult(false, null, baseUsedDegradedProfile, lastInitError?.GetBaseException().Message ?? "会话初始化失败。");
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
                    return new ConnectDeviceResult(false, null, usedDegradedProfile, "连接失败。");
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
                    return new ConnectDeviceResult(false, null, usedDegradedProfile, "会话已被移除。");
                }

                EmitSessionUpdated(connectedSnapshot);
                return new ConnectDeviceResult(true, connectedSnapshot, usedDegradedProfile, null);
            }
            catch (Exception ex)
            {
                await FailAndRemoveSessionAsync(deviceId, ex.Message);
                return new ConnectDeviceResult(false, null, usedDegradedProfile, ex.Message);
            }
        }

        private static IReadOnlyList<SessionVideoProfile> BuildCompatibilityFallbackProfiles(
            SessionVideoProfile preferred,
            SessionVideoProfile degraded)
        {
            var profiles = new List<SessionVideoProfile>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Add(SessionVideoProfile profile)
            {
                var key = $"{profile.Width}x{profile.Height}@{profile.RefreshRate}:{profile.BitrateBps}";
                if (seen.Add(key))
                {
                    profiles.Add(profile);
                }
            }

            SessionVideoProfile WithFps(SessionVideoProfile p, int fps)
            {
                fps = Math.Max(15, fps);
                var recommended = VideoEncoderFactory.GetRecommendedConfig(p.Width, p.Height, fps).Bitrate;
                var bitrate = Math.Min(p.BitrateBps, recommended);
                return new SessionVideoProfile(p.Width, p.Height, fps, bitrate);
            }

            SessionVideoProfile ScaleDown(SessionVideoProfile p, int maxWidth, int maxHeight)
            {
                if (p.Width <= maxWidth && p.Height <= maxHeight)
                {
                    return p;
                }

                var scaleW = maxWidth / (double)p.Width;
                var scaleH = maxHeight / (double)p.Height;
                var scale = Math.Min(scaleW, scaleH);

                var width = Math.Max(640, (int)Math.Floor(p.Width * scale));
                var height = Math.Max(360, (int)Math.Floor(p.Height * scale));

                width -= width % 2;
                height -= height % 2;

                var fps = Math.Min(p.RefreshRate, 60);
                var recommended = VideoEncoderFactory.GetRecommendedConfig(width, height, fps).Bitrate;
                var bitrate = Math.Min(p.BitrateBps, recommended);

                return new SessionVideoProfile(width, height, fps, bitrate);
            }

            Add(preferred);

            if (preferred.RefreshRate > 60)
            {
                Add(WithFps(preferred, 60));
            }

            if (preferred.RefreshRate > 30)
            {
                Add(WithFps(preferred, 30));
            }

            var scaled1080 = ScaleDown(preferred, 1920, 1080);
            if (!Equals(scaled1080, preferred))
            {
                Add(scaled1080);
                if (scaled1080.RefreshRate > 60)
                {
                    Add(WithFps(scaled1080, 60));
                }
                if (scaled1080.RefreshRate > 30)
                {
                    Add(WithFps(scaled1080, 30));
                }
            }

            var scaled720 = ScaleDown(preferred, 1280, 720);
            if (!Equals(scaled720, preferred) && !Equals(scaled720, scaled1080))
            {
                Add(scaled720);
                if (scaled720.RefreshRate > 60)
                {
                    Add(WithFps(scaled720, 60));
                }
                if (scaled720.RefreshRate > 30)
                {
                    Add(WithFps(scaled720, 30));
                }
            }

            if (!Equals(degraded, preferred))
            {
                Add(degraded);
                if (degraded.RefreshRate > 30)
                {
                    Add(WithFps(degraded, 30));
                }
            }

            return profiles;
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
