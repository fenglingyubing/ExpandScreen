namespace ExpandScreen.Services.Driver
{
    /// <summary>
    /// ExpandScreen 虚拟显示驱动适配器（包装 DriverInterface）。
    /// 注意：在非 Windows 环境下 IsAvailable 恒为 false，避免触发 P/Invoke。
    /// </summary>
    public sealed class ExpandScreenVirtualDisplayDriver : IVirtualDisplayDriver
    {
        private readonly DriverInterface _driverInterface = new();
        private bool _opened;
        private bool _disposed;

        public bool IsAvailable
        {
            get
            {
                if (!OperatingSystem.IsWindows())
                {
                    return false;
                }

                try
                {
                    return DriverInterface.IsDriverInstalled();
                }
                catch
                {
                    return false;
                }
            }
        }

        public (uint MonitorCount, uint MaxMonitors) GetAdapterInfo()
        {
            EnsureOpen();
            return _driverInterface.GetAdapterInfo();
        }

        public uint CreateMonitor(uint width, uint height, uint refreshRate)
        {
            EnsureOpen();
            return _driverInterface.CreateMonitor(width, height, refreshRate);
        }

        public bool TryDestroyMonitor(uint monitorId)
        {
            try
            {
                _ = _driverInterface.DestroyMonitor(monitorId);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void EnsureOpen()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ExpandScreenVirtualDisplayDriver));
            }

            if (!IsAvailable)
            {
                throw new InvalidOperationException("Virtual display driver is not available.");
            }

            if (_opened)
            {
                return;
            }

            _driverInterface.Open();
            _opened = true;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _driverInterface.Dispose();
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}

