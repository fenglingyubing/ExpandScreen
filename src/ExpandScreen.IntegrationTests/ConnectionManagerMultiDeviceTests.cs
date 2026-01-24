using ExpandScreen.Services.Connection;
using ExpandScreen.Services.Driver;
using Xunit;

namespace ExpandScreen.IntegrationTests
{
    public class ConnectionManagerMultiDeviceTests
    {
        private sealed class FakePortAllocator : ILocalPortAllocator
        {
            private int _next;

            public FakePortAllocator(int startPort)
            {
                _next = startPort;
            }

            public int AllocateEphemeralPort()
            {
                return _next++;
            }
        }

        private sealed class FakeConnection : IConnectionManager
        {
            public bool IsConnected { get; private set; }

            public Task<bool> ConnectAsync(string deviceId)
            {
                IsConnected = true;
                return Task.FromResult(true);
            }

            public Task DisconnectAsync()
            {
                IsConnected = false;
                return Task.CompletedTask;
            }

            public Task SendAsync(byte[] data)
            {
                return Task.CompletedTask;
            }
        }

        private sealed class FakeVirtualDisplayDriver : IVirtualDisplayDriver
        {
            private uint _nextMonitorId = 1;
            private bool _disposed;

            public FakeVirtualDisplayDriver(uint maxMonitors)
            {
                MaxMonitors = maxMonitors;
            }

            public uint MaxMonitors { get; }

            public uint MonitorCount { get; private set; }

            public bool IsAvailable => !_disposed;

            public (uint MonitorCount, uint MaxMonitors) GetAdapterInfo()
            {
                return (MonitorCount, MaxMonitors);
            }

            public uint CreateMonitor(uint width, uint height, uint refreshRate)
            {
                if (MonitorCount >= MaxMonitors)
                {
                    throw new InvalidOperationException("No more monitors available.");
                }

                MonitorCount++;
                return _nextMonitorId++;
            }

            public bool TryDestroyMonitor(uint monitorId)
            {
                if (MonitorCount == 0)
                {
                    return false;
                }

                MonitorCount--;
                return true;
            }

            public void Dispose()
            {
                _disposed = true;
            }
        }

        [Fact]
        public async Task Connect_MultipleDevices_UsesDegradedProfileAfterThreshold()
        {
            var options = new ConnectionManagerOptions
            {
                EnableVirtualDisplays = true,
                MaxHighQualitySessions = 1,
                PrimaryProfile = new SessionVideoProfile(1920, 1080, 60, 5_000_000),
                DegradedProfile = new SessionVideoProfile(1280, 720, 60, 3_000_000),
                DefaultMaxSessions = 4
            };

            var portAllocator = new FakePortAllocator(20000);
            var driver = new FakeVirtualDisplayDriver(maxMonitors: 4);

            using var manager = new ConnectionManager(
                options: options,
                portAllocator: portAllocator,
                connectionFactory: (_, _) => new FakeConnection(),
                virtualDisplayDriverFactory: () => driver);

            var first = await manager.ConnectAsync("device-1");
            Assert.True(first.Success);
            Assert.False(first.UsedDegradedProfile);
            Assert.NotNull(first.Session);
            Assert.Equal(1920, first.Session!.VideoProfile.Width);
            Assert.Equal(20000, first.Session.LocalPort);

            var second = await manager.ConnectAsync("device-2");
            Assert.True(second.Success);
            Assert.True(second.UsedDegradedProfile);
            Assert.NotNull(second.Session);
            Assert.Equal(1280, second.Session!.VideoProfile.Width);
            Assert.Equal(20001, second.Session.LocalPort);

            Assert.Equal(2u, driver.MonitorCount);
        }

        [Fact]
        public async Task Connect_ExceedMaxMonitors_RefusesNewSession()
        {
            var options = new ConnectionManagerOptions
            {
                EnableVirtualDisplays = true,
                DefaultMaxSessions = 99
            };

            using var manager = new ConnectionManager(
                options: options,
                portAllocator: new FakePortAllocator(21000),
                connectionFactory: (_, _) => new FakeConnection(),
                virtualDisplayDriverFactory: () => new FakeVirtualDisplayDriver(maxMonitors: 2));

            Assert.True((await manager.ConnectAsync("device-1")).Success);
            Assert.True((await manager.ConnectAsync("device-2")).Success);

            var third = await manager.ConnectAsync("device-3");
            Assert.False(third.Success);
            Assert.Contains("上限", third.ErrorMessage ?? string.Empty);
        }

        [Fact]
        public async Task Disconnect_RemovesSession_AndFreesMonitor()
        {
            var driver = new FakeVirtualDisplayDriver(maxMonitors: 2);

            using var manager = new ConnectionManager(
                options: new ConnectionManagerOptions { EnableVirtualDisplays = true },
                portAllocator: new FakePortAllocator(22000),
                connectionFactory: (_, _) => new FakeConnection(),
                virtualDisplayDriverFactory: () => driver);

            Assert.True((await manager.ConnectAsync("device-1")).Success);
            Assert.Equal(1u, driver.MonitorCount);

            Assert.True(await manager.DisconnectAsync("device-1"));
            Assert.Equal(0u, driver.MonitorCount);
            Assert.Empty(manager.Sessions);
        }
    }
}

