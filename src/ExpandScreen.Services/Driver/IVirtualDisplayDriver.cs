namespace ExpandScreen.Services.Driver
{
    public interface IVirtualDisplayDriver : IDisposable
    {
        bool IsAvailable { get; }

        (uint MonitorCount, uint MaxMonitors) GetAdapterInfo();

        uint CreateMonitor(uint width, uint height, uint refreshRate);

        bool TryDestroyMonitor(uint monitorId);
    }
}

