using System.Net;
using System.Net.Sockets;
using ExpandScreen.Protocol.Messages;
using ExpandScreen.Utils;

namespace ExpandScreen.Services.Connection
{
    /// <summary>
    /// WiFi 设备发现响应器（Windows端）：监听UDP发现请求并回包发现响应。
    /// </summary>
    public sealed class WifiDiscoveryResponder : IDisposable
    {
        public const int DefaultDiscoveryPort = 15556;

        private readonly int _tcpPort;
        private readonly string _serverName;
        private readonly string _serverId;
        private readonly bool _manageFirewallRules;
        private readonly int _udpPort;

        private UdpClient? _udp;
        private CancellationTokenSource? _cts;
        private Task? _loopTask;
        private bool _disposed;

        public int UdpPort { get; private set; }

        public WifiDiscoveryResponder(
            int tcpPort,
            int udpPort = DefaultDiscoveryPort,
            string? serverName = null,
            string? serverId = null,
            bool manageFirewallRules = false)
        {
            _tcpPort = tcpPort;
            _udpPort = udpPort;
            _serverName = serverName ?? Environment.MachineName;
            _serverId = serverId ?? Environment.MachineName;
            _manageFirewallRules = manageFirewallRules;
        }

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (_udp != null)
            {
                return;
            }

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            var udp = new UdpClient(AddressFamily.InterNetwork);
            udp.Client.ExclusiveAddressUse = false;
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.EnableBroadcast = true;

            udp.Client.Bind(new IPEndPoint(IPAddress.Any, _udpPort));

            UdpPort = ((IPEndPoint)udp.Client.LocalEndPoint!).Port;
            _udp = udp;

            if (_manageFirewallRules && WindowsFirewallRuleManager.IsSupported)
            {
                _ = await WindowsFirewallRuleManager.TryEnsureInboundPortRuleAsync(
                    ruleName: $"ExpandScreen Discovery UDP {UdpPort}",
                    port: UdpPort,
                    protocol: "UDP");
            }

            _loopTask = Task.Run(() => ReceiveLoopAsync(_cts.Token));

            LogHelper.Info($"WifiDiscoveryResponder started (udpPort={UdpPort}, tcpPort={_tcpPort})");
        }

        public async Task StopAsync()
        {
            if (_udp == null)
            {
                return;
            }

            _cts?.Cancel();

            try
            {
                if (_loopTask != null)
                {
                    await _loopTask.WaitAsync(TimeSpan.FromSeconds(2));
                }
            }
            catch
            {
                // ignore
            }

            _udp.Close();
            _udp.Dispose();
            _udp = null;

            if (_manageFirewallRules && WindowsFirewallRuleManager.IsSupported)
            {
                _ = await WindowsFirewallRuleManager.TryDeleteRuleAsync($"ExpandScreen Discovery UDP {UdpPort}");
            }

            _cts?.Dispose();
            _cts = null;
            _loopTask = null;
        }

        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            if (_udp == null)
            {
                return;
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    UdpReceiveResult received = await _udp.ReceiveAsync(cancellationToken);
                    if (received.Buffer.Length == 0)
                    {
                        continue;
                    }

                    var request = MessageSerializer.DeserializeJsonPayload<DiscoveryRequestMessage>(received.Buffer);
                    if (request == null || request.MessageType != "DiscoveryRequest")
                    {
                        continue;
                    }

                    var response = new DiscoveryResponseMessage
                    {
                        RequestId = request.RequestId,
                        ServerId = _serverId,
                        ServerName = _serverName,
                        TcpPort = _tcpPort,
                        WebSocketSupported = false,
                        ServerVersion = "1.0.0"
                    };

                    byte[] payload = MessageSerializer.SerializeJsonPayload(response);
                    await _udp.SendAsync(payload, payload.Length, received.RemoteEndPoint);

                    LogHelper.Debug($"Discovery request from {received.RemoteEndPoint} (requestId={request.RequestId})");
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (SocketException ex)
                {
                    LogHelper.Warning($"Discovery socket error: {ex.SocketErrorCode}");
                }
                catch (Exception ex)
                {
                    LogHelper.Error("Error in discovery responder loop", ex);
                }
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(WifiDiscoveryResponder));
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            StopAsync().GetAwaiter().GetResult();
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}

