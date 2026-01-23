using System.Drawing;
using System.Net;
using System.Net.Sockets;
using ExpandScreen.Protocol.Messages;
using ExpandScreen.Protocol.Network;
using ExpandScreen.Services.Connection;
using ExpandScreen.Services.Input;

namespace ExpandScreen.IntegrationTests
{
    public class TouchInputTests
    {
        [Fact]
        public void TouchContactRegistry_ShouldAllocate_AndReleaseSlots()
        {
            var registry = new TouchContactRegistry(maxContacts: 2);

            Assert.Equal(1, registry.AllocateSlot(pointerId: 7));
            Assert.Equal(2, registry.AllocateSlot(pointerId: 8));
            Assert.Null(registry.AllocateSlot(pointerId: 9));

            Assert.Equal(1, registry.GetPrimarySlot());

            Assert.True(registry.ReleaseSlot(pointerId: 7));
            Assert.Equal(1, registry.AllocateSlot(pointerId: 9));
            Assert.False(registry.ReleaseSlot(pointerId: 7));
        }

        [Fact]
        public void TouchCoordinateMapper_ShouldMapCorners()
        {
            var mapper = new TouchCoordinateMapper();
            mapper.UpdateSourceScreen(width: 100, height: 100);
            mapper.UpdateTargetBounds(new Rectangle(x: 200, y: 300, width: 1920, height: 1080));

            Assert.Equal(new Point(200, 300), mapper.Map(0, 0));
            Assert.Equal(new Point(200 + 1919, 300 + 1079), mapper.Map(99, 99));
        }

        [Fact]
        public void TouchCoordinateMapper_ShouldApplyRotation90()
        {
            var mapper = new TouchCoordinateMapper();
            mapper.UpdateSourceScreen(width: 100, height: 100);
            mapper.UpdateTargetBounds(new Rectangle(x: 10, y: 20, width: 100, height: 100));
            mapper.UpdateRotationDegrees(90);

            Assert.Equal(new Point(10 + 99, 20 + 0), mapper.Map(0, 0));
        }

        [Fact]
        public async Task WifiConnection_ShouldForwardTouchEvent()
        {
            using var wifi = new WifiConnection(tcpPort: 0, discoveryPort: 0, manageFirewallRules: false, inputService: null);

            var receivedTcs = new TaskCompletionSource<TouchEventMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            wifi.TouchEventReceived += (s, msg) => receivedTcs.TrySetResult(msg);

            await wifi.StartAsync();

            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, wifi.TcpPort);

                using var session = new NetworkSession(client.GetStream());
                var handshake = new HandshakeMessage
                {
                    DeviceId = "android-001",
                    DeviceName = "Test Android",
                    ClientVersion = "1.0.0",
                    ScreenWidth = 1920,
                    ScreenHeight = 1080
                };

                bool ok = await session.PerformHandshakeAsync(handshake).WaitAsync(TimeSpan.FromSeconds(3));
                Assert.True(ok);

                var touch = new TouchEventMessage
                {
                    Action = 0,
                    PointerId = 1,
                    X = 10,
                    Y = 20,
                    Pressure = 0.5f
                };

                await session.SendMessageAsync(MessageType.TouchEvent, touch);

                var received = await receivedTcs.Task.WaitAsync(TimeSpan.FromSeconds(3));
                Assert.Equal(touch.Action, received.Action);
                Assert.Equal(touch.PointerId, received.PointerId);
                Assert.Equal(touch.X, received.X);
                Assert.Equal(touch.Y, received.Y);
            }
            finally
            {
                await wifi.StopAsync();
            }
        }
    }
}
