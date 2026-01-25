using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using ExpandScreen.Services.Connection;

namespace ExpandScreen.IntegrationTests
{
    public class UsbConnectionFunctionalTests
    {
        private sealed class FakeAdbCommandRunner : IAdbCommandRunner
        {
            private readonly object _lock = new();
            private readonly Dictionary<string, Queue<(bool success, string output, string error)>> _responses = new(StringComparer.Ordinal);

            public List<string> Calls { get; } = new();

            public void EnqueueResponse(string arguments, bool success, string output = "", string error = "")
            {
                lock (_lock)
                {
                    if (!_responses.TryGetValue(arguments, out var queue))
                    {
                        queue = new Queue<(bool, string, string)>();
                        _responses[arguments] = queue;
                    }

                    queue.Enqueue((success, output, error));
                }
            }

            public Task<(bool success, string output, string error)> RunAsync(
                string adbPath,
                string arguments,
                int timeoutMs,
                CancellationToken cancellationToken = default)
            {
                lock (_lock)
                {
                    Calls.Add(arguments);

                    if (_responses.TryGetValue(arguments, out var queue) && queue.Count > 0)
                    {
                        return Task.FromResult(queue.Dequeue());
                    }

                    return Task.FromResult((false, "", $"Unexpected adb command args: {arguments}"));
                }
            }
        }

        [Fact]
        public async Task AdbHelper_GetDevicesAsync_ShouldParseDevices()
        {
            var runner = new FakeAdbCommandRunner();
            runner.EnqueueResponse(
                "devices -l",
                success: true,
                output: "List of devices attached\nemulator-5554\tdevice product:sdk model:Pixel_7 device:pixel_7\n0123456789ABCDEF\toffline\n");

            var adb = new AdbHelper("fake-adb.exe", runner);
            var devices = await adb.GetDevicesAsync();

            Assert.Equal(2, devices.Count);
            Assert.Equal("emulator-5554", devices[0].DeviceId);
            Assert.Equal("device", devices[0].Status);
            Assert.Equal("Pixel_7", devices[0].Model);
            Assert.Equal("pixel_7", devices[0].DeviceName);
            Assert.True(devices[0].IsAuthorized);

            Assert.Equal("0123456789ABCDEF", devices[1].DeviceId);
            Assert.Equal("offline", devices[1].Status);
            Assert.False(devices[1].IsAuthorized);
        }

        [Fact]
        public async Task DeviceDiscoveryService_ShouldRaiseConnectUpdateDisconnectEvents()
        {
            var runner = new FakeAdbCommandRunner();
            runner.EnqueueResponse(
                "devices -l",
                success: true,
                output: "List of devices attached\nabc\tdevice model:Pixel_7 device:pixel_7\n");
            runner.EnqueueResponse("-s abc shell getprop ro.product.model", success: true, output: "Pixel 7");
            runner.EnqueueResponse("-s abc shell getprop ro.product.manufacturer", success: true, output: "Google");
            runner.EnqueueResponse("-s abc shell getprop ro.build.version.release", success: true, output: "14");
            runner.EnqueueResponse("-s abc shell getprop ro.build.version.sdk", success: true, output: "34");

            runner.EnqueueResponse(
                "devices -l",
                success: true,
                output: "List of devices attached\nabc\toffline model:Pixel_7 device:pixel_7\n");

            runner.EnqueueResponse(
                "devices -l",
                success: true,
                output: "List of devices attached\n");

            var adb = new AdbHelper("fake-adb.exe", runner);
            using var discovery = new DeviceDiscoveryService(adb);

            AndroidDevice? connected = null;
            AndroidDevice? updated = null;
            AndroidDevice? disconnected = null;

            discovery.DeviceConnected += (_, device) => connected = device;
            discovery.DeviceUpdated += (_, device) => updated = device;
            discovery.DeviceDisconnected += (_, device) => disconnected = device;

            await discovery.TriggerScanAsync();
            Assert.NotNull(connected);
            Assert.Equal("abc", connected!.DeviceId);
            Assert.Equal("Google", connected.Manufacturer);
            Assert.Equal(34, connected.SdkVersion);

            await discovery.TriggerScanAsync();
            Assert.NotNull(updated);
            Assert.Equal("offline", updated!.Status);

            await discovery.TriggerScanAsync();
            Assert.NotNull(disconnected);
            Assert.Equal("abc", disconnected!.DeviceId);
        }

        [Fact]
        public async Task UsbConnection_ShouldConnectSendAndDisconnect_UsingFakeAdb()
        {
            var runner = new FakeAdbCommandRunner();
            runner.EnqueueResponse(
                "devices -l",
                success: true,
                output: "List of devices attached\nabc\tdevice\n");

            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int localPort = ((IPEndPoint)listener.LocalEndpoint).Port;

            runner.EnqueueResponse($"-s abc forward tcp:{localPort} tcp:{localPort}", success: true);
            runner.EnqueueResponse($"-s abc forward --remove tcp:{localPort}", success: true);

            var adb = new AdbHelper("fake-adb.exe", runner);
            using var usb = new UsbConnection(localPort, localPort, adb, new UsbConnectionOptions { MonitorIntervalMs = 25 });

            var acceptTask = listener.AcceptTcpClientAsync();
            Assert.True(await usb.ConnectAsync("abc"));

            using var server = await acceptTask.WaitAsync(TimeSpan.FromSeconds(2));
            using var serverStream = server.GetStream();

            byte[] payload = { 1, 2, 3, 4, 5 };
            await usb.SendAsync(payload);

            var buffer = new byte[payload.Length];
            int read = await serverStream.ReadAsync(buffer).AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(payload.Length, read);
            Assert.Equal(payload, buffer);

            await usb.DisconnectAsync();

            Assert.Contains($"-s abc forward tcp:{localPort} tcp:{localPort}", runner.Calls);
            Assert.Contains($"-s abc forward --remove tcp:{localPort}", runner.Calls);
        }

        [Fact]
        public async Task UsbConnection_AutoReconnect_ShouldReconnectAfterSocketClose()
        {
            var runner = new FakeAdbCommandRunner();
            runner.EnqueueResponse(
                "devices -l",
                success: true,
                output: "List of devices attached\nabc\tdevice\n");
            runner.EnqueueResponse(
                "devices -l",
                success: true,
                output: "List of devices attached\nabc\tdevice\n");
            runner.EnqueueResponse(
                "devices -l",
                success: true,
                output: "List of devices attached\nabc\tdevice\n");

            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int localPort = ((IPEndPoint)listener.LocalEndpoint).Port;

            runner.EnqueueResponse($"-s abc forward tcp:{localPort} tcp:{localPort}", success: true);
            runner.EnqueueResponse($"-s abc forward tcp:{localPort} tcp:{localPort}", success: true);
            runner.EnqueueResponse($"-s abc forward tcp:{localPort} tcp:{localPort}", success: true);
            runner.EnqueueResponse($"-s abc forward --remove tcp:{localPort}", success: true);
            runner.EnqueueResponse($"-s abc forward --remove tcp:{localPort}", success: true);
            runner.EnqueueResponse($"-s abc forward --remove tcp:{localPort}", success: true);

            var adb = new AdbHelper("fake-adb.exe", runner);
            using var usb = new UsbConnection(
                localPort,
                localPort,
                adb,
                new UsbConnectionOptions
                {
                    MonitorIntervalMs = 25,
                    ReconnectDelayMs = 25,
                    MaxReconnectAttempts = 3
                });

            var statuses = new List<string>();
            var reconnectedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            usb.ConnectionStatusChanged += (_, status) =>
            {
                statuses.Add(status);
                if (status == "Reconnected")
                {
                    reconnectedTcs.TrySetResult(true);
                }
            };

            var accept1Task = listener.AcceptTcpClientAsync();
            Assert.True(await usb.ConnectAsync("abc"));
            using var server1 = await accept1Task.WaitAsync(TimeSpan.FromSeconds(2));

            server1.Close();

            var accept2Task = listener.AcceptTcpClientAsync();
            var accept2 = await Task.WhenAny(accept2Task, reconnectedTcs.Task, Task.Delay(3000));
            Assert.True(accept2 == accept2Task || accept2 == reconnectedTcs.Task, "Reconnect did not happen within timeout");

            if (accept2 == accept2Task)
            {
                using var server2 = await accept2Task;
            }
            else
            {
                using var server2 = await accept2Task.WaitAsync(TimeSpan.FromSeconds(2));
            }

            await reconnectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Contains(statuses, s => s.StartsWith("Reconnecting", StringComparison.Ordinal));
            Assert.Contains("Reconnected", statuses);
        }
    }
}
