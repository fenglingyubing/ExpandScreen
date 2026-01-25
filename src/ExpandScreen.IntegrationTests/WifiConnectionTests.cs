using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Authentication;
using ExpandScreen.Protocol.Messages;
using ExpandScreen.Protocol.Network;
using ExpandScreen.Services.Connection;
using ExpandScreen.Services.Security;
using Xunit;

namespace ExpandScreen.IntegrationTests
{
    public class WifiConnectionTests
    {
        [Fact]
        public async Task DiscoveryResponder_ShouldReply_WithTcpPort()
        {
            using var wifi = new WifiConnection(tcpPort: 0, discoveryPort: 0, manageFirewallRules: false);
            await wifi.StartAsync();

            try
            {
                using var udp = new UdpClient(0);
                var request = new DiscoveryRequestMessage
                {
                    RequestId = Guid.NewGuid().ToString("N"),
                    ClientDeviceId = "android-001",
                    ClientDeviceName = "Test Android"
                };

                byte[] requestBytes = MessageSerializer.SerializeJsonPayload(request);
                await udp.SendAsync(requestBytes, requestBytes.Length, new IPEndPoint(IPAddress.Loopback, wifi.DiscoveryPort));

                var receiveTask = udp.ReceiveAsync();
                var completed = await Task.WhenAny(receiveTask, Task.Delay(2000));
                Assert.True(completed == receiveTask, "Discovery response not received within timeout");

                var received = await receiveTask;
                var response = MessageSerializer.DeserializeJsonPayload<DiscoveryResponseMessage>(received.Buffer);

                Assert.NotNull(response);
                Assert.Equal("DiscoveryResponse", response.MessageType);
                Assert.Equal(request.RequestId, response.RequestId);
                Assert.Equal(wifi.TcpPort, response.TcpPort);
                Assert.False(response.WebSocketSupported);
            }
            finally
            {
                await wifi.StopAsync();
            }
        }

        [Fact]
        public async Task WifiConnection_ShouldAcceptHandshake()
        {
            using var wifi = new WifiConnection(tcpPort: 0, discoveryPort: 0, manageFirewallRules: false);

            var serverHandshakeTcs = new TaskCompletionSource<HandshakeMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            wifi.HandshakeReceived += (s, handshake) => serverHandshakeTcs.TrySetResult(handshake);

            await wifi.StartAsync();

            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, wifi.TcpPort);

                using var clientSession = new NetworkSession(client.GetStream());

                var handshake = new HandshakeMessage
                {
                    DeviceId = "android-001",
                    DeviceName = "Test Android",
                    ClientVersion = "1.0.0",
                    ScreenWidth = 1920,
                    ScreenHeight = 1080
                };

                bool ok = await clientSession.PerformHandshakeAsync(handshake).WaitAsync(TimeSpan.FromSeconds(3));
                Assert.True(ok);
                Assert.True(clientSession.IsHandshakeCompleted);
                Assert.NotNull(clientSession.SessionId);

                var serverReceived = await serverHandshakeTcs.Task.WaitAsync(TimeSpan.FromSeconds(3));
                Assert.Equal(handshake.DeviceId, serverReceived.DeviceId);
                Assert.Equal(handshake.DeviceName, serverReceived.DeviceName);
            }
            finally
            {
                await wifi.StopAsync();
            }
        }

        [Fact]
        public async Task WifiConnection_Tls_ShouldAcceptHandshake_WithPinnedCertificate()
        {
            string certPath = Path.Combine(Path.GetTempPath(), $"expandscreen-test-cert-{Guid.NewGuid():N}.pfx.dpapi");
            var manager = new TlsCertificateManager(certPath);
            using var cert = manager.GetOrCreateServerCertificate();

            using var wifi = new WifiConnection(tcpPort: 0, discoveryPort: 0, manageFirewallRules: false, enableTls: true, tlsCertificate: cert);
            var serverErrorTcs = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
            wifi.ConnectionError += (_, ex) => serverErrorTcs.TrySetResult(ex);
            await wifi.StartAsync();

            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, wifi.TcpPort);

                using var ssl = new SslStream(
                    client.GetStream(),
                    leaveInnerStreamOpen: false,
                    userCertificateValidationCallback: (_, serverCert, _, _) =>
                    {
                        if (serverCert is null) return false;
                        using var remote = new System.Security.Cryptography.X509Certificates.X509Certificate2(serverCert);
                        string expected = TlsPairingCode.ToHex(TlsPairingCode.GetFingerprintSha256(cert));
                        string actual = TlsPairingCode.ToHex(TlsPairingCode.GetFingerprintSha256(remote));
                        return string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
                    });

                try
                {
                    await ssl.AuthenticateAsClientAsync(
                        targetHost: "ExpandScreen",
                        clientCertificates: null,
                        enabledSslProtocols: SslProtocols.Tls12,
                        checkCertificateRevocation: false);
                }
                catch (Exception ex)
                {
                    var completed = await Task.WhenAny(serverErrorTcs.Task, Task.Delay(1000));
                    if (completed == serverErrorTcs.Task)
                    {
                        var serverError = await serverErrorTcs.Task;
                        throw new Xunit.Sdk.XunitException($"Client TLS failed: {ex.Message}\nServer error: {serverError.GetBaseException().Message}");
                    }
                    throw;
                }

                using var clientSession = new NetworkSession(ssl);

                var handshake = new HandshakeMessage
                {
                    DeviceId = "android-001",
                    DeviceName = "Test Android",
                    ClientVersion = "1.0.0",
                    ScreenWidth = 1920,
                    ScreenHeight = 1080,
                    PairingCode = TlsPairingCode.Get6DigitCode(cert)
                };

                bool ok = await clientSession.PerformHandshakeAsync(handshake).WaitAsync(TimeSpan.FromSeconds(3));
                Assert.True(ok);
                Assert.True(clientSession.IsHandshakeCompleted);
            }
            finally
            {
                await wifi.StopAsync();
                manager.RotateCertificate();
            }
        }

        [Fact]
        public async Task WifiConnection_Tls_ShouldRejectHandshake_WhenPairingCodeInvalid()
        {
            string certPath = Path.Combine(Path.GetTempPath(), $"expandscreen-test-cert-{Guid.NewGuid():N}.pfx.dpapi");
            var manager = new TlsCertificateManager(certPath);
            using var cert = manager.GetOrCreateServerCertificate();

            using var wifi = new WifiConnection(tcpPort: 0, discoveryPort: 0, manageFirewallRules: false, enableTls: true, tlsCertificate: cert);
            await wifi.StartAsync();

            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, wifi.TcpPort);

                using var ssl = new SslStream(
                    client.GetStream(),
                    leaveInnerStreamOpen: false,
                    userCertificateValidationCallback: (_, serverCert, _, _) =>
                    {
                        if (serverCert is null) return false;
                        using var remote = new System.Security.Cryptography.X509Certificates.X509Certificate2(serverCert);
                        string expected = TlsPairingCode.ToHex(TlsPairingCode.GetFingerprintSha256(cert));
                        string actual = TlsPairingCode.ToHex(TlsPairingCode.GetFingerprintSha256(remote));
                        return string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
                    });

                await ssl.AuthenticateAsClientAsync(
                    targetHost: "ExpandScreen",
                    clientCertificates: null,
                    enabledSslProtocols: SslProtocols.Tls12,
                    checkCertificateRevocation: false);

                using var clientSession = new NetworkSession(ssl);

                var handshake = new HandshakeMessage
                {
                    DeviceId = "android-001",
                    DeviceName = "Test Android",
                    ClientVersion = "1.0.0",
                    ScreenWidth = 1920,
                    ScreenHeight = 1080,
                    PairingCode = "000000"
                };

                bool ok = await clientSession.PerformHandshakeAsync(handshake).WaitAsync(TimeSpan.FromSeconds(3));
                Assert.False(ok);
                Assert.False(clientSession.IsHandshakeCompleted);
            }
            finally
            {
                await wifi.StopAsync();
                manager.RotateCertificate();
            }
        }
    }
}
