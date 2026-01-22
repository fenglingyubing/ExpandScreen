using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using ExpandScreen.Utils;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace ExpandScreen.Core.Capture
{
    /// <summary>
    /// 桌面复制器 - 使用DXGI Desktop Duplication API捕获屏幕
    /// </summary>
    public class DesktopDuplicator : IDisposable
    {
        private ID3D11Device? _device;
        private ID3D11DeviceContext? _deviceContext;
        private IDXGIOutputDuplication? _deskDupl;
        private IDXGIOutput1? _output;
        private ID3D11Texture2D? _stagingTexture;

        private int _width;
        private int _height;
        private int _monitorIndex;
        private bool _initialized;
        private bool _disposed;
        private long _frameNumber;

        private readonly object _lock = new object();

        /// <summary>
        /// 当前帧号
        /// </summary>
        public long FrameNumber => Interlocked.Read(ref _frameNumber);

        /// <summary>
        /// 屏幕宽度
        /// </summary>
        public int Width => _width;

        /// <summary>
        /// 屏幕高度
        /// </summary>
        public int Height => _height;

        /// <summary>
        /// 是否已初始化
        /// </summary>
        public bool IsInitialized => _initialized;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="monitorIndex">监视器索引，默认为0（主显示器）</param>
        public DesktopDuplicator(int monitorIndex = 0)
        {
            _monitorIndex = monitorIndex;
        }

        /// <summary>
        /// 初始化D3D11设备和Desktop Duplication
        /// </summary>
        public bool Initialize()
        {
            lock (_lock)
            {
                if (_initialized)
                {
                    LogHelper.Info("DesktopDuplicator已经初始化");
                    return true;
                }

                try
                {
                    LogHelper.Info($"开始初始化DesktopDuplicator，监视器索引: {_monitorIndex}");

                    // 创建D3D11设备
                    if (!CreateD3D11Device())
                    {
                        LogHelper.Error("创建D3D11设备失败");
                        return false;
                    }

                    // 获取指定的输出设备
                    if (!GetOutputDevice())
                    {
                        LogHelper.Error("获取输出设备失败");
                        Cleanup();
                        return false;
                    }

                    // 创建Desktop Duplication
                    if (!CreateDesktopDuplication())
                    {
                        LogHelper.Error("创建Desktop Duplication失败");
                        Cleanup();
                        return false;
                    }

                    // 创建Staging纹理用于CPU访问
                    if (!CreateStagingTexture())
                    {
                    LogHelper.Error("创建Staging纹理失败");
                        Cleanup();
                        return false;
                    }

                    _initialized = true;
                    LogHelper.Info($"DesktopDuplicator初始化成功，分辨率: {_width}x{_height}");
                    return true;
                }
                catch (Exception ex)
                {
                    LogHelper.Error($"初始化DesktopDuplicator异常: {ex.Message}", ex);
                    Cleanup();
                    return false;
                }
            }
        }

        /// <summary>
        /// 创建D3D11设备
        /// </summary>
        private bool CreateD3D11Device()
        {
            try
            {
                // 尝试创建硬件加速的D3D11设备
                var result = D3D11.D3D11CreateDevice(
                    null,
                    DriverType.Hardware,
                    DeviceCreationFlags.None,
                    null,
                    out _device,
                    out _deviceContext);

                if (result.Success && _device != null && _deviceContext != null)
                {
                    LogHelper.Info("D3D11硬件设备创建成功");
                    return true;
                }

                // 如果硬件失败，尝试WARP软件渲染器
                LogHelper.Warning("硬件设备创建失败，尝试WARP设备");
                result = D3D11.D3D11CreateDevice(
                    null,
                    DriverType.Warp,
                    DeviceCreationFlags.None,
                    null,
                    out _device,
                    out _deviceContext);

                if (result.Success && _device != null && _deviceContext != null)
                {
                    LogHelper.Info("D3D11 WARP设备创建成功");
                    return true;
                }

                LogHelper.Error($"创建D3D11设备失败: {result}");
                return false;
            }
            catch (Exception ex)
            {
                LogHelper.Error($"创建D3D11设备异常: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// 获取指定的输出设备（显示器）
        /// </summary>
        private bool GetOutputDevice()
        {
            try
            {
                // 获取DXGI设备
                using var dxgiDevice = _device!.QueryInterface<IDXGIDevice>();
                using var dxgiAdapter = dxgiDevice.GetParent<IDXGIAdapter>();

                // 枚举输出设备
                var outputIndex = 0;
                IDXGIOutput? output = null;

                while (dxgiAdapter.EnumOutputs(outputIndex, out output).Success)
                {
                    if (outputIndex == _monitorIndex)
                    {
                        _output = output.QueryInterface<IDXGIOutput1>();
                        var desc = output.Description;

                        _width = desc.DesktopCoordinates.Right - desc.DesktopCoordinates.Left;
                        _height = desc.DesktopCoordinates.Bottom - desc.DesktopCoordinates.Top;

                        LogHelper.Info($"找到目标输出设备: {desc.DeviceName}, 分辨率: {_width}x{_height}");
                        output.Dispose();
                        return true;
                    }

                    output?.Dispose();
                    outputIndex++;
                }

                LogHelper.Error($"未找到索引为{_monitorIndex}的输出设备");
                return false;
            }
            catch (Exception ex)
            {
                LogHelper.Error($"获取输出设备异常: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// 创建Desktop Duplication
        /// </summary>
        private bool CreateDesktopDuplication()
        {
            try
            {
                _deskDupl = _output!.DuplicateOutput(_device!);

                if (_deskDupl != null)
                {
                    LogHelper.Info("Desktop Duplication创建成功");
                    return true;
                }

                LogHelper.Error("创建Desktop Duplication失败: outputDuplication为null");
                return false;
            }
            catch (Exception ex)
            {
                LogHelper.Error($"创建Desktop Duplication异常: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// 创建Staging纹理用于CPU读取
        /// </summary>
        private bool CreateStagingTexture()
        {
            try
            {
                var textureDesc = new Texture2DDescription
                {
                    Width = _width,
                    Height = _height,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.B8G8R8A8_UNorm,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Staging,
                    BindFlags = BindFlags.None,
                    CPUAccessFlags = CpuAccessFlags.Read,
                    MiscFlags = ResourceOptionFlags.None
                };

                _stagingTexture = _device!.CreateTexture2D(textureDesc);

                if (_stagingTexture != null)
                {
                    LogHelper.Info("Staging纹理创建成功");
                    return true;
                }

                LogHelper.Error("创建Staging纹理失败");
                return false;
            }
            catch (Exception ex)
            {
                LogHelper.Error($"创建Staging纹理异常: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// 捕获一帧
        /// </summary>
        /// <param name="timeoutMs">超时时间（毫秒），0表示立即返回</param>
        /// <returns>捕获的帧，如果失败返回null</returns>
        public CapturedFrame? CaptureFrame(int timeoutMs = 0)
        {
            lock (_lock)
            {
                if (!_initialized || _disposed)
                {
                    LogHelper.Warning("DesktopDuplicator未初始化或已释放");
                    return null;
                }

                try
                {
                    // 尝试获取新帧
                    var result = _deskDupl!.AcquireNextFrame(timeoutMs, out var frameInfo, out var desktopResource);

                    if (result.Failure)
                    {
                        // 超时不算错误
                        if (result.Code == Vortice.DXGI.ResultCode.WaitTimeout)
                        {
                            return null;
                        }

                        // 如果是ACCESS_LOST，需要重新创建duplication
                        if (result.Code == Vortice.DXGI.ResultCode.AccessLost)
                        {
                            LogHelper.Warning("Desktop访问丢失，尝试重新初始化");
                            Reinitialize();
                            return null;
                        }

                        LogHelper.Error($"获取帧失败: {result}");
                        return null;
                    }

                    using (desktopResource)
                    {
                        // 如果没有新的更新，直接返回
                        if (frameInfo.LastPresentTime == 0)
                        {
                            _deskDupl.ReleaseFrame();
                            return null;
                        }

                        // 获取桌面纹理
                        using var desktopTexture = desktopResource.QueryInterface<ID3D11Texture2D>();

                        // 复制到Staging纹理
                        _deviceContext!.CopyResource(_stagingTexture!, desktopTexture);

                        // 映射纹理以读取数据
                        var mappedResource = _deviceContext.Map(_stagingTexture!, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);

                        try
                        {
                            // 创建帧数据
                            var stride = mappedResource.RowPitch;
                            var dataSize = stride * _height;
                            var frameData = new byte[dataSize];

                            // 复制数据
                            Marshal.Copy(mappedResource.DataPointer, frameData, 0, dataSize);

                            // 获取脏矩形（变化的区域）
                            Rectangle[]? dirtyRects = null;
                            if (frameInfo.TotalMetadataBufferSize > 0)
                            {
                                dirtyRects = GetDirtyRects(frameInfo);
                            }

                            // 创建捕获帧
                            var frameNumber = Interlocked.Increment(ref _frameNumber);
                            var capturedFrame = new CapturedFrame(frameData, _width, _height, stride, frameNumber)
                            {
                                DirtyRects = dirtyRects,
                                IsFullFrame = dirtyRects == null || dirtyRects.Length == 0
                            };

                            return capturedFrame;
                        }
                        finally
                        {
                            _deviceContext.Unmap(_stagingTexture!, 0);
                            _deskDupl.ReleaseFrame();
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.Error($"捕获帧异常: {ex.Message}", ex);
                    return null;
                }
            }
        }

        /// <summary>
        /// 获取脏矩形（变化的区域）
        /// </summary>
        private Rectangle[]? GetDirtyRects(OutduplFrameInfo frameInfo)
        {
            try
            {
                if (frameInfo.TotalMetadataBufferSize == 0)
                {
                    return null;
                }

                // 获取移动矩形
                var moveRectBufferSize = 0;
                _deskDupl!.GetFrameMoveRects(0, null, out moveRectBufferSize);

                // 获取脏矩形
                var dirtyRectBufferSize = 0;
                _deskDupl.GetFrameDirtyRects(0, null, out dirtyRectBufferSize);

                if (dirtyRectBufferSize == 0)
                {
                    return null;
                }

                var rectCount = dirtyRectBufferSize / Marshal.SizeOf<Vortice.RawRect>();
                var rects = new Vortice.RawRect[rectCount];

                _deskDupl.GetFrameDirtyRects(dirtyRectBufferSize, rects, out _);

                // 转换为System.Drawing.Rectangle
                var dirtyRects = new Rectangle[rectCount];
                for (int i = 0; i < rectCount; i++)
                {
                    dirtyRects[i] = new Rectangle(
                        rects[i].Left,
                        rects[i].Top,
                        rects[i].Right - rects[i].Left,
                        rects[i].Bottom - rects[i].Top
                    );
                }

                return dirtyRects;
            }
            catch (Exception ex)
            {
                LogHelper.Error($"获取脏矩形异常: {ex.Message}", ex);
                return null;
            }
        }

        /// <summary>
        /// 重新初始化（当桌面访问丢失时）
        /// </summary>
        private bool Reinitialize()
        {
            LogHelper.Info("开始重新初始化DesktopDuplicator");

            Cleanup();
            _initialized = false;

            return Initialize();
        }

        /// <summary>
        /// 清理资源
        /// </summary>
        private void Cleanup()
        {
            _stagingTexture?.Dispose();
            _stagingTexture = null;

            _deskDupl?.Dispose();
            _deskDupl = null;

            _output?.Dispose();
            _output = null;

            _deviceContext?.Dispose();
            _deviceContext = null;

            _device?.Dispose();
            _device = null;

            _initialized = false;
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                Cleanup();
                LogHelper.Info("DesktopDuplicator已释放");
            }
        }
    }
}
