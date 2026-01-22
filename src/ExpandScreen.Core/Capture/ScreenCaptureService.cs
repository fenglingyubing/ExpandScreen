using System.Diagnostics;
using ExpandScreen.Utils;

namespace ExpandScreen.Core.Capture
{
    /// <summary>
    /// 屏幕捕获服务 - 实现IScreenCapture接口
    /// 提供60fps帧率控制的屏幕捕获功能
    /// </summary>
    public class ScreenCaptureService : IScreenCapture, IDisposable
    {
        private DesktopDuplicator? _duplicator;
        private FrameBuffer? _frameBuffer;
        private Thread? _captureThread;
        private CancellationTokenSource? _cancellationTokenSource;

        private readonly int _monitorIndex;
        private readonly int _targetFps;
        private readonly int _frameIntervalMs;

        private bool _isRunning;
        private bool _disposed;

        private long _totalFramesCaptured;
        private long _totalFramesDropped;
        private readonly Stopwatch _performanceTimer = new Stopwatch();

        /// <summary>
        /// 捕获的总帧数
        /// </summary>
        public long TotalFramesCaptured => Interlocked.Read(ref _totalFramesCaptured);

        /// <summary>
        /// 丢弃的总帧数
        /// </summary>
        public long TotalFramesDropped => Interlocked.Read(ref _totalFramesDropped);

        /// <summary>
        /// 当前FPS
        /// </summary>
        public double CurrentFps { get; private set; }

        /// <summary>
        /// 是否正在运行
        /// </summary>
        public bool IsRunning => _isRunning;

        /// <summary>
        /// 帧缓冲区
        /// </summary>
        public FrameBuffer? FrameBuffer => _frameBuffer;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="monitorIndex">监视器索引</param>
        /// <param name="targetFps">目标帧率，默认60fps</param>
        /// <param name="bufferSize">帧缓冲区大小，默认3</param>
        public ScreenCaptureService(int monitorIndex = 0, int targetFps = 60, int bufferSize = 3)
        {
            _monitorIndex = monitorIndex;
            _targetFps = targetFps;
            _frameIntervalMs = 1000 / targetFps;
            _frameBuffer = new FrameBuffer(bufferSize);
        }

        /// <summary>
        /// 开始捕获
        /// </summary>
        public void Start()
        {
            if (_isRunning)
            {
                LogHelper.Warning("屏幕捕获服务已经在运行");
                return;
            }

            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ScreenCaptureService));
            }

            try
            {
                LogHelper.Info($"启动屏幕捕获服务，监视器: {_monitorIndex}, 目标FPS: {_targetFps}");

                // 创建并初始化DesktopDuplicator
                _duplicator = new DesktopDuplicator(_monitorIndex);
                if (!_duplicator.Initialize())
                {
                    throw new InvalidOperationException("初始化DesktopDuplicator失败");
                }

                // 创建取消令牌
                _cancellationTokenSource = new CancellationTokenSource();

                // 启动捕获线程
                _captureThread = new Thread(CaptureThreadProc)
                {
                    Name = $"ScreenCapture-Monitor{_monitorIndex}",
                    Priority = ThreadPriority.AboveNormal,
                    IsBackground = true
                };

                _isRunning = true;
                _captureThread.Start();

                LogHelper.Info("屏幕捕获服务启动成功");
            }
            catch (Exception ex)
            {
                LogHelper.Error($"启动屏幕捕获服务失败: {ex.Message}", ex);
                Cleanup();
                throw;
            }
        }

        /// <summary>
        /// 停止捕获
        /// </summary>
        public void Stop()
        {
            if (!_isRunning)
            {
                return;
            }

            LogHelper.Info("停止屏幕捕获服务");

            try
            {
                _isRunning = false;

                // 取消捕获线程
                _cancellationTokenSource?.Cancel();

                // 等待线程结束（最多5秒）
                if (_captureThread != null && !_captureThread.Join(TimeSpan.FromSeconds(5)))
                {
                    LogHelper.Warning("捕获线程未能在5秒内结束");
                }

                Cleanup();

                LogHelper.Info($"屏幕捕获服务已停止，总计捕获{TotalFramesCaptured}帧，丢弃{TotalFramesDropped}帧");
            }
            catch (Exception ex)
            {
                LogHelper.Error($"停止屏幕捕获服务异常: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 获取下一帧（从缓冲区）
        /// </summary>
        public byte[]? CaptureFrame()
        {
            if (!_isRunning || _frameBuffer == null)
            {
                return null;
            }

            if (_frameBuffer.TryTake(out var frame))
            {
                using (frame)
                {
                    return frame.Data;
                }
            }

            return null;
        }

        /// <summary>
        /// 异步获取下一帧
        /// </summary>
        public async Task<CapturedFrame?> CaptureFrameAsync(CancellationToken cancellationToken = default)
        {
            if (!_isRunning || _frameBuffer == null)
            {
                return null;
            }

            return await _frameBuffer.TakeAsync(cancellationToken);
        }

        /// <summary>
        /// 捕获线程主循环
        /// </summary>
        private void CaptureThreadProc()
        {
            LogHelper.Info("捕获线程开始运行");

            var cancellationToken = _cancellationTokenSource!.Token;
            var frameTimer = Stopwatch.StartNew();
            var fpsTimer = Stopwatch.StartNew();
            var frameCount = 0;

            _performanceTimer.Start();

            try
            {
                while (!cancellationToken.IsCancellationRequested && _isRunning)
                {
                    try
                    {
                        // 记录开始时间
                        var startTime = frameTimer.ElapsedMilliseconds;

                        // 捕获一帧（立即返回，不等待）
                        var frame = _duplicator?.CaptureFrame(0);

                        if (frame != null)
                        {
                            // 尝试将帧加入缓冲区
                            if (_frameBuffer!.TryAdd(frame))
                            {
                                Interlocked.Increment(ref _totalFramesCaptured);
                                frameCount++;
                            }
                            else
                            {
                                Interlocked.Increment(ref _totalFramesDropped);
                                frame.Dispose();
                            }
                        }

                        // 更新FPS统计（每秒一次）
                        if (fpsTimer.ElapsedMilliseconds >= 1000)
                        {
                            CurrentFps = frameCount / (fpsTimer.ElapsedMilliseconds / 1000.0);
                            frameCount = 0;
                            fpsTimer.Restart();

                            // 记录缓冲区状态
                            var bufferCount = _frameBuffer?.Count ?? 0;
                            var droppedFrames = _frameBuffer?.DroppedFrames ?? 0;

                            LogHelper.Debug($"捕获统计 - FPS: {CurrentFps:F2}, 缓冲: {bufferCount}, 丢帧: {droppedFrames}");
                        }

                        // 帧率控制：确保不超过目标帧率
                        var elapsedTime = frameTimer.ElapsedMilliseconds - startTime;
                        var sleepTime = _frameIntervalMs - (int)elapsedTime;

                        if (sleepTime > 0)
                        {
                            Thread.Sleep(sleepTime);
                        }
                        else if (sleepTime < -_frameIntervalMs)
                        {
                            // 如果延迟太大，记录警告
                            LogHelper.Warning($"捕获延迟过大: {-sleepTime}ms");
                        }
                    }
                    catch (Exception ex)
                    {
                        LogHelper.Error($"捕获帧异常: {ex.Message}", ex);
                        Thread.Sleep(100); // 发生错误时稍作延迟
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error($"捕获线程异常: {ex.Message}", ex);
            }
            finally
            {
                _performanceTimer.Stop();
                LogHelper.Info($"捕获线程结束，运行时间: {_performanceTimer.Elapsed}");
            }
        }

        /// <summary>
        /// 清理资源
        /// </summary>
        private void Cleanup()
        {
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;

            _duplicator?.Dispose();
            _duplicator = null;

            _frameBuffer?.Clear();

            _captureThread = null;
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Stop();

            _frameBuffer?.Dispose();
            _frameBuffer = null;

            _disposed = true;
            LogHelper.Info("ScreenCaptureService已释放");
        }

        /// <summary>
        /// 获取性能统计信息
        /// </summary>
        public string GetPerformanceStats()
        {
            if (_frameBuffer == null)
            {
                return "未初始化";
            }

            var runTime = _performanceTimer.Elapsed;
            var avgFps = runTime.TotalSeconds > 0 ? TotalFramesCaptured / runTime.TotalSeconds : 0;

            return $"运行时间: {runTime}, 平均FPS: {avgFps:F2}, " +
                   $"总帧数: {TotalFramesCaptured}, 丢帧数: {TotalFramesDropped}, " +
                   $"当前FPS: {CurrentFps:F2}, 缓冲区: {_frameBuffer.Count}";
        }
    }
}
