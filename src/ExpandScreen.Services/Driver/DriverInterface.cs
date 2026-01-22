using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ExpandScreen.Services.Driver
{
    /// <summary>
    /// ExpandScreen虚拟显示驱动通信接口
    /// </summary>
    public class DriverInterface : IDisposable
    {
        private const string DEVICE_PATH = @"\\.\ExpandScreen";
        private SafeFileHandle? _deviceHandle;
        private bool _disposed = false;

        // IOCTL控制码定义
        private const uint FILE_DEVICE_VIDEO = 0x00000023;
        private const uint METHOD_BUFFERED = 0;
        private const uint FILE_ANY_ACCESS = 0;

        private const uint IOCTL_EXPANDSCREEN_CREATE_MONITOR =
            (FILE_DEVICE_VIDEO << 16) | (FILE_ANY_ACCESS << 14) | (0x800 << 2) | METHOD_BUFFERED;

        private const uint IOCTL_EXPANDSCREEN_DESTROY_MONITOR =
            (FILE_DEVICE_VIDEO << 16) | (FILE_ANY_ACCESS << 14) | (0x801 << 2) | METHOD_BUFFERED;

        private const uint IOCTL_EXPANDSCREEN_GET_ADAPTER_INFO =
            (FILE_DEVICE_VIDEO << 16) | (FILE_ANY_ACCESS << 14) | (0x802 << 2) | METHOD_BUFFERED;

        #region Native Structs

        [StructLayout(LayoutKind.Sequential)]
        private struct CreateMonitorInput
        {
            public uint Width;
            public uint Height;
            public uint RefreshRate;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CreateMonitorOutput
        {
            public uint MonitorId;
            public int Status;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AdapterInfo
        {
            public uint MonitorCount;
            public uint MaxMonitors;
        }

        #endregion

        #region Native Methods

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern SafeFileHandle CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(
            SafeFileHandle hDevice,
            uint dwIoControlCode,
            IntPtr lpInBuffer,
            uint nInBufferSize,
            IntPtr lpOutBuffer,
            uint nOutBufferSize,
            out uint lpBytesReturned,
            IntPtr lpOverlapped);

        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;

        #endregion

        /// <summary>
        /// 打开驱动设备
        /// </summary>
        /// <returns>是否成功打开</returns>
        public bool Open()
        {
            if (_deviceHandle != null && !_deviceHandle.IsInvalid)
            {
                return true;
            }

            _deviceHandle = CreateFile(
                DEVICE_PATH,
                GENERIC_READ | GENERIC_WRITE,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero,
                OPEN_EXISTING,
                FILE_ATTRIBUTE_NORMAL,
                IntPtr.Zero);

            if (_deviceHandle.IsInvalid)
            {
                var error = Marshal.GetLastWin32Error();
                throw new InvalidOperationException(
                    $"无法打开驱动设备。错误代码: {error}。请确保驱动已正确安装。");
            }

            return true;
        }

        /// <summary>
        /// 创建虚拟监视器
        /// </summary>
        /// <param name="width">显示宽度</param>
        /// <param name="height">显示高度</param>
        /// <param name="refreshRate">刷新率</param>
        /// <returns>监视器ID，失败返回0</returns>
        public uint CreateMonitor(uint width, uint height, uint refreshRate)
        {
            if (_deviceHandle == null || _deviceHandle.IsInvalid)
            {
                throw new InvalidOperationException("驱动设备未打开");
            }

            var input = new CreateMonitorInput
            {
                Width = width,
                Height = height,
                RefreshRate = refreshRate
            };

            var output = new CreateMonitorOutput();

            IntPtr inputPtr = Marshal.AllocHGlobal(Marshal.SizeOf<CreateMonitorInput>());
            IntPtr outputPtr = Marshal.AllocHGlobal(Marshal.SizeOf<CreateMonitorOutput>());

            try
            {
                Marshal.StructureToPtr(input, inputPtr, false);

                bool success = DeviceIoControl(
                    _deviceHandle,
                    IOCTL_EXPANDSCREEN_CREATE_MONITOR,
                    inputPtr,
                    (uint)Marshal.SizeOf<CreateMonitorInput>(),
                    outputPtr,
                    (uint)Marshal.SizeOf<CreateMonitorOutput>(),
                    out uint bytesReturned,
                    IntPtr.Zero);

                if (!success)
                {
                    var error = Marshal.GetLastWin32Error();
                    throw new InvalidOperationException($"创建监视器失败。错误代码: {error}");
                }

                output = Marshal.PtrToStructure<CreateMonitorOutput>(outputPtr);

                if (output.Status != 0)  // STATUS_SUCCESS = 0
                {
                    throw new InvalidOperationException($"驱动返回错误。状态码: 0x{output.Status:X8}");
                }

                return output.MonitorId;
            }
            finally
            {
                Marshal.FreeHGlobal(inputPtr);
                Marshal.FreeHGlobal(outputPtr);
            }
        }

        /// <summary>
        /// 销毁虚拟监视器
        /// </summary>
        /// <param name="monitorId">监视器ID</param>
        /// <returns>是否成功</returns>
        public bool DestroyMonitor(uint monitorId)
        {
            if (_deviceHandle == null || _deviceHandle.IsInvalid)
            {
                throw new InvalidOperationException("驱动设备未打开");
            }

            // TODO: 实现销毁逻辑（驱动端需要先实现）
            throw new NotImplementedException("监视器销毁功能尚未实现");
        }

        /// <summary>
        /// 获取适配器信息
        /// </summary>
        /// <returns>适配器信息</returns>
        public (uint MonitorCount, uint MaxMonitors) GetAdapterInfo()
        {
            if (_deviceHandle == null || _deviceHandle.IsInvalid)
            {
                throw new InvalidOperationException("驱动设备未打开");
            }

            IntPtr outputPtr = Marshal.AllocHGlobal(Marshal.SizeOf<AdapterInfo>());

            try
            {
                bool success = DeviceIoControl(
                    _deviceHandle,
                    IOCTL_EXPANDSCREEN_GET_ADAPTER_INFO,
                    IntPtr.Zero,
                    0,
                    outputPtr,
                    (uint)Marshal.SizeOf<AdapterInfo>(),
                    out uint bytesReturned,
                    IntPtr.Zero);

                if (!success)
                {
                    var error = Marshal.GetLastWin32Error();
                    throw new InvalidOperationException($"获取适配器信息失败。错误代码: {error}");
                }

                var info = Marshal.PtrToStructure<AdapterInfo>(outputPtr);
                return (info.MonitorCount, info.MaxMonitors);
            }
            finally
            {
                Marshal.FreeHGlobal(outputPtr);
            }
        }

        /// <summary>
        /// 检查驱动是否已安装
        /// </summary>
        /// <returns>是否已安装</returns>
        public static bool IsDriverInstalled()
        {
            try
            {
                var handle = CreateFile(
                    DEVICE_PATH,
                    GENERIC_READ,
                    FILE_SHARE_READ | FILE_SHARE_WRITE,
                    IntPtr.Zero,
                    OPEN_EXISTING,
                    FILE_ATTRIBUTE_NORMAL,
                    IntPtr.Zero);

                if (handle.IsInvalid)
                {
                    return false;
                }

                handle.Dispose();
                return true;
            }
            catch
            {
                return false;
            }
        }

        #region IDisposable

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _deviceHandle?.Dispose();
                }

                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}
