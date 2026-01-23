using System.Net;
using System.Net.Sockets;
using ExpandScreen.Protocol.Messages;
using ExpandScreen.Protocol.Network;
using ExpandScreen.Services.Input;
using ExpandScreen.Utils;

namespace ExpandScreen.Services.Connection
{
    /// <summary>
    /// WiFi连接（Windows端）：TCP Server + 会话管理 + UDP发现响应。
    /// </summary>
    public sealed class WifiConnection : IDisposable
    {
        public const int DefaultTcpPort = 15555;

        private readonly int _tcpPort;
        private readonly int _discoveryPort;
        private readonly bool _manageFirewallRules;
        private readonly InputService? _inputService;
        private WifiDiscoveryResponder? _discoveryResponder;

        private TcpListener? _listener;
        private CancellationTokenSource? _cts;
        private Task? _acceptLoopTask;
        private NetworkSession? _currentSession;
        private TcpClient? _currentClient;

        private readonly SemaphoreSlim _sessionLock = new(1, 1);
        private bool _disposed;

        public int TcpPort { get; private set; }
        public int DiscoveryPort => _discoveryResponder?.UdpPort ?? 0;

        public bool IsRunning => _listener != null;

        public event EventHandler<IPEndPoint>? ClientConnected;
        public event EventHandler? ClientDisconnected;
        public event EventHandler<Exception>? ConnectionError;
        public event EventHandler<HandshakeMessage>? HandshakeReceived;
        public event EventHandler<TouchEventMessage>? TouchEventReceived;

        public WifiConnection(
            int tcpPort = DefaultTcpPort,
            int discoveryPort = WifiDiscoveryResponder.DefaultDiscoveryPort,
            bool manageFirewallRules = false,
            InputService? inputService = null)
        {
            _tcpPort = tcpPort;
            _discoveryPort = discoveryPort;
            _manageFirewallRules = manageFirewallRules;
            _inputService = inputService;
        }

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (_listener != null)
            {
                return;
            }

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            var listener = new TcpListener(IPAddress.Any, _tcpPort);
            listener.Server.NoDelay = true;
            listener.Start();

            TcpPort = ((IPEndPoint)listener.LocalEndpoint).Port;
            _listener = listener;

            if (_manageFirewallRules && WindowsFirewallRuleManager.IsSupported)
            {
                _ = await WindowsFirewallRuleManager.TryEnsureInboundPortRuleAsync(
                    ruleName: $"ExpandScreen WiFi TCP {TcpPort}",
                    port: TcpPort,
                    protocol: "TCP");
            }

            _discoveryResponder = new WifiDiscoveryResponder(
                tcpPort: TcpPort,
                udpPort: _discoveryPort,
                manageFirewallRules: _manageFirewallRules);
            await _discoveryResponder.StartAsync(_cts.Token);

            _acceptLoopTask = Task.Run(() => AcceptLoopAsync(_cts.Token));

            LogHelper.Info($"WifiConnection started (tcpPort={TcpPort}, udpDiscoveryPort={_discoveryResponder.UdpPort})");
        }

        public async Task StopAsync()
        {
            if (_listener == null)
            {
                return;
            }

            _cts?.Cancel();

            try
            {
                if (_acceptLoopTask != null)
                {
                    await _acceptLoopTask.WaitAsync(TimeSpan.FromSeconds(2));
                }
            }
            catch
            {
                // ignore
            }

            if (_discoveryResponder != null)
            {
                await _discoveryResponder.StopAsync();
                _discoveryResponder = null;
            }

            _listener.Stop();
            _listener = null;

            await ClearCurrentSessionAsync();

            if (_manageFirewallRules && WindowsFirewallRuleManager.IsSupported)
            {
                _ = await WindowsFirewallRuleManager.TryDeleteRuleAsync($"ExpandScreen WiFi TCP {TcpPort}");
            }

            _cts?.Dispose();
            _cts = null;
            _acceptLoopTask = null;
        }

        public async Task<bool> SendMessageAsync<T>(MessageType type, T payload)
        {
            await _sessionLock.WaitAsync();
            try
            {
                if (_currentSession == null)
                {
                    return false;
                }

                return await _currentSession.SendMessageAsync(type, payload);
            }
            finally
            {
                _sessionLock.Release();
            }
        }

        private async Task AcceptLoopAsync(CancellationToken cancellationToken)
        {
            if (_listener == null)
            {
                return;
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync(cancellationToken);
                    client.NoDelay = true;
                    client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

                    var remote = client.Client.RemoteEndPoint as IPEndPoint;
                    if (remote != null)
                    {
                        ClientConnected?.Invoke(this, remote);
                    }

                    await ReplaceCurrentSessionAsync(client, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    ConnectionError?.Invoke(this, ex);
                    LogHelper.Error("Error in WiFi accept loop", ex);
                }
            }
        }

        private async Task ReplaceCurrentSessionAsync(TcpClient client, CancellationToken cancellationToken)
        {
            await _sessionLock.WaitAsync(cancellationToken);
            try
            {
                ClearCurrentSessionNoLock();

                _currentClient = client;
                var session = new NetworkSession(
                    client.GetStream(),
                    handshakeRequestHandler: request =>
                    {
                        HandshakeReceived?.Invoke(this, request);
                        if (request.ScreenWidth > 0 && request.ScreenHeight > 0)
                        {
                            _inputService?.UpdateRemoteScreenSize(request.ScreenWidth, request.ScreenHeight);
                        }
                        return Task.FromResult((Accept: true, ErrorMessage: (string?)null));
                    });

                session.MessageReceived += SessionOnMessageReceived;
                session.ConnectionClosed += (s, e) => FireAndForget(ClearCurrentSessionAsync(), "ConnectionClosed");

                session.HeartbeatTimeout += (s, e) => FireAndForget(ClearCurrentSessionAsync(), "HeartbeatTimeout");

                session.SessionError += (s, ex) =>
                {
                    ConnectionError?.Invoke(this, ex);
                    FireAndForget(ClearCurrentSessionAsync(), "SessionError");
                };

                _currentSession = session;
            }
            finally
            {
                _sessionLock.Release();
            }
        }

        private void SessionOnMessageReceived(object? sender, MessageReceivedEventArgs e)
        {
            if (e.Header.Type != MessageType.TouchEvent)
            {
                return;
            }

            var touch = MessageSerializer.DeserializeJsonPayload<TouchEventMessage>(e.Payload);
            if (touch == null)
            {
                return;
            }

            TouchEventReceived?.Invoke(this, touch);
            _inputService?.HandleTouchEvent(touch);
        }

        private void ClearCurrentSessionNoLock()
        {
            if (_currentSession != null)
            {
                _currentSession.MessageReceived -= SessionOnMessageReceived;
            }

            _currentSession?.Dispose();
            _currentSession = null;

            if (_currentClient != null)
            {
                _currentClient.Close();
                _currentClient.Dispose();
                _currentClient = null;
            }
        }

        private async Task ClearCurrentSessionAsync()
        {
            try
            {
                await _sessionLock.WaitAsync();
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            try
            {
                ClearCurrentSessionNoLock();

                ClientDisconnected?.Invoke(this, EventArgs.Empty);
            }
            finally
            {
                try
                {
                    _sessionLock.Release();
                }
                catch (ObjectDisposedException)
                {
                    // ignore: Dispose() may have already disposed the semaphore while an async cleanup was in-flight
                }
            }
        }

        private static void FireAndForget(Task task, string context)
        {
            _ = task.ContinueWith(
                t => LogHelper.Debug($"WifiConnection background task failed ({context}): {t.Exception?.GetBaseException().Message}"),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(WifiConnection));
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            StopAsync().GetAwaiter().GetResult();
            _sessionLock.Dispose();
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
