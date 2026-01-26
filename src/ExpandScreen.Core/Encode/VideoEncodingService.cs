using System.Threading.Channels;
using System.Diagnostics;
using ExpandScreen.Core.Capture;
using ExpandScreen.Utils;

namespace ExpandScreen.Core.Encode
{
    /// <summary>
    /// 视频编码服务
    /// 管理编码线程和队列
    /// </summary>
    public class VideoEncodingService : IDisposable
    {
        private IVideoEncoder _encoder;
        private Channel<CapturedFrame> _inputQueue;
        private Channel<EncodedFrame> _outputQueue;
        private CancellationTokenSource _cancellationTokenSource;
        private Task? _encodingTask;
        private bool _isRunning = false;

        // 性能统计
        private long _totalFramesEncoded = 0;
        private long _totalEncodingTimeMs = 0;
        private readonly Stopwatch _performanceTimer = new();

        /// <summary>
        /// 输入队列容量
        /// </summary>
        public int InputQueueCapacity { get; set; } = 10;

        /// <summary>
        /// 输出队列容量
        /// </summary>
        public int OutputQueueCapacity { get; set; } = 30;

        /// <summary>
        /// 是否正在运行
        /// </summary>
        public bool IsRunning => _isRunning;

        /// <summary>
        /// 获取平均编码时间（毫秒）
        /// </summary>
        public double AverageEncodingTimeMs => _totalFramesEncoded > 0
            ? (double)_totalEncodingTimeMs / _totalFramesEncoded
            : 0;

        /// <summary>
        /// 获取编码帧总数
        /// </summary>
        public long TotalFramesEncoded => _totalFramesEncoded;

        /// <summary>
        /// 构造函数
        /// </summary>
        public VideoEncodingService(IVideoEncoder encoder)
        {
            _encoder = encoder ?? throw new ArgumentNullException(nameof(encoder));
            _cancellationTokenSource = new CancellationTokenSource();

            // 创建输入输出队列
            _inputQueue = Channel.CreateBounded<CapturedFrame>(new BoundedChannelOptions(InputQueueCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest // 队列满时丢弃最旧的帧
            });

            _outputQueue = Channel.CreateBounded<EncodedFrame>(new BoundedChannelOptions(OutputQueueCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest
            });
        }

        /// <summary>
        /// 启动编码服务
        /// </summary>
        public void Start()
        {
            if (_isRunning)
            {
                LogHelper.Warning("编码服务已在运行");
                return;
            }

            LogHelper.Info("启动视频编码服务");

            _cancellationTokenSource = new CancellationTokenSource();
            _encodingTask = Task.Run(EncodingLoop, _cancellationTokenSource.Token);
            _isRunning = true;
            _performanceTimer.Start();
        }

        /// <summary>
        /// 停止编码服务
        /// </summary>
        public async Task StopAsync()
        {
            if (!_isRunning)
            {
                return;
            }

            LogHelper.Info("停止视频编码服务");

            try
            {
                _cancellationTokenSource?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception ex)
            {
                LogHelper.Warning($"停止编码服务：取消失败（{ex.GetBaseException().Message}）");
            }

            try
            {
                _inputQueue.Writer.Complete();
            }
            catch
            {
            }

            if (_encodingTask != null)
            {
                try
                {
                    await _encodingTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    LogHelper.Error($"编码线程异常退出: {ex.Message}", ex);
                }
            }

            try
            {
                _outputQueue.Writer.Complete();
            }
            catch
            {
            }
            _isRunning = false;
            _performanceTimer.Stop();

            LogHelper.Info($"编码服务已停止. 总帧数: {_totalFramesEncoded}, 平均编码时间: {AverageEncodingTimeMs:F2}ms");
        }

        /// <summary>
        /// 添加帧到编码队列
        /// </summary>
        public async Task<bool> EnqueueFrameAsync(CapturedFrame frame)
        {
            if (!_isRunning)
            {
                return false;
            }

            try
            {
                await _inputQueue.Writer.WriteAsync(frame);
                return true;
            }
            catch (ChannelClosedException)
            {
                return false;
            }
        }

        /// <summary>
        /// 从输出队列获取编码帧
        /// </summary>
        public async Task<EncodedFrame?> DequeueEncodedFrameAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                return await _outputQueue.Reader.ReadAsync(cancellationToken);
            }
            catch (ChannelClosedException)
            {
                return null;
            }
        }

        /// <summary>
        /// 尝试从输出队列获取编码帧（非阻塞）
        /// </summary>
        public bool TryDequeueEncodedFrame(out EncodedFrame? frame)
        {
            return _outputQueue.Reader.TryRead(out frame);
        }

        /// <summary>
        /// 获取输入队列当前数量
        /// </summary>
        public int GetInputQueueCount()
        {
            return _inputQueue.Reader.Count;
        }

        /// <summary>
        /// 获取输出队列当前数量
        /// </summary>
        public int GetOutputQueueCount()
        {
            return _outputQueue.Reader.Count;
        }

        /// <summary>
        /// 编码循环（在独立线程中运行）
        /// </summary>
        private async Task EncodingLoop()
        {
            LogHelper.Info("编码线程启动");

            try
            {
                await foreach (var capturedFrame in _inputQueue.Reader.ReadAllAsync(_cancellationTokenSource.Token))
                {
                    try
                    {
                        var startTime = Stopwatch.GetTimestamp();

                        // 编码帧
                        byte[]? encodedData = _encoder.Encode(capturedFrame.Data);

                        var encodeTimeMs = (Stopwatch.GetTimestamp() - startTime) * 1000.0 / Stopwatch.Frequency;

                        if (encodedData != null && encodedData.Length > 0)
                        {
                            // 创建编码帧对象
                            var encodedFrame = new EncodedFrame(encodedData, encodedData.Length, capturedFrame.FrameNumber, false)
                            {
                                Timestamp = capturedFrame.Timestamp,
                                EncodeTimeMs = encodeTimeMs
                            };

                            // 添加到输出队列
                            await _outputQueue.Writer.WriteAsync(encodedFrame, _cancellationTokenSource.Token);

                            // 更新统计
                            _totalFramesEncoded++;
                            _totalEncodingTimeMs += (long)encodeTimeMs;

                            // 每100帧输出统计
                            if (_totalFramesEncoded % 100 == 0)
                            {
                                var avgTime = AverageEncodingTimeMs;
                                var fps = 1000.0 / avgTime;
                                LogHelper.Debug($"编码统计: 帧#{_totalFramesEncoded}, 平均:{avgTime:F2}ms, 理论FPS:{fps:F1}, " +
                                               $"输入队列:{GetInputQueueCount()}, 输出队列:{GetOutputQueueCount()}");
                            }
                        }

                        // 释放捕获的帧
                        capturedFrame.Dispose();
                    }
                    catch (Exception ex)
                    {
                        LogHelper.Error($"编码帧失败: {ex.Message}", ex);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                LogHelper.Info("编码线程被取消");
            }
            catch (Exception ex)
            {
                LogHelper.Error($"编码循环异常: {ex.Message}", ex);
            }
            finally
            {
                LogHelper.Info("编码线程退出");
            }
        }

        /// <summary>
        /// 获取性能报告
        /// </summary>
        public string GetPerformanceReport()
        {
            var elapsed = _performanceTimer.Elapsed.TotalSeconds;
            var avgFps = elapsed > 0 ? _totalFramesEncoded / elapsed : 0;

            return $"编码性能报告:\n" +
                   $"  运行时间: {elapsed:F1}秒\n" +
                   $"  编码帧数: {_totalFramesEncoded}\n" +
                   $"  平均编码时间: {AverageEncodingTimeMs:F2}ms\n" +
                   $"  平均FPS: {avgFps:F1}\n" +
                   $"  输入队列: {GetInputQueueCount()}/{InputQueueCapacity}\n" +
                   $"  输出队列: {GetOutputQueueCount()}/{OutputQueueCapacity}";
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            try
            {
                StopAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                LogHelper.Error("VideoEncodingService dispose: StopAsync failed.", ex);
            }

            try
            {
                _cancellationTokenSource?.Dispose();
            }
            catch
            {
            }

            try
            {
                _encoder?.Dispose();
            }
            catch
            {
            }
        }
    }
}
