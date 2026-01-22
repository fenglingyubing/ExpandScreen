using System.Threading.Channels;

namespace ExpandScreen.Core.Capture
{
    /// <summary>
    /// 线程安全的帧缓冲队列
    /// </summary>
    public class FrameBuffer : IDisposable
    {
        private readonly Channel<CapturedFrame> _channel;
        private readonly int _capacity;
        private long _droppedFrames;
        private bool _disposed;

        /// <summary>
        /// 丢弃的帧数
        /// </summary>
        public long DroppedFrames => Interlocked.Read(ref _droppedFrames);

        /// <summary>
        /// 当前缓冲帧数
        /// </summary>
        public int Count => _channel.Reader.Count;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="capacity">队列容量，默认为3</param>
        public FrameBuffer(int capacity = 3)
        {
            _capacity = capacity;

            // 创建有界通道，使用DropOldest策略
            var options = new BoundedChannelOptions(_capacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true
            };

            _channel = Channel.CreateBounded<CapturedFrame>(options);
        }

        /// <summary>
        /// 尝试添加帧到队列
        /// </summary>
        /// <param name="frame">要添加的帧</param>
        /// <returns>是否成功添加</returns>
        public bool TryAdd(CapturedFrame frame)
        {
            if (_disposed)
            {
                frame.Dispose();
                return false;
            }

            // TryWrite会根据DropOldest策略自动丢弃旧帧
            if (_channel.Writer.TryWrite(frame))
            {
                return true;
            }
            else
            {
                // 如果写入失败，增加丢布计数
                Interlocked.Increment(ref _droppedFrames);
                frame.Dispose();
                return false;
            }
        }

        /// <summary>
        /// 异步添加帧到队列
        /// </summary>
        /// <param name="frame">要添加的帧</param>
        /// <param name="cancellationToken">取消令牌</param>
        public async Task<bool> AddAsync(CapturedFrame frame, CancellationToken cancellationToken = default)
        {
            if (_disposed)
            {
                frame.Dispose();
                return false;
            }

            try
            {
                await _channel.Writer.WriteAsync(frame, cancellationToken);
                return true;
            }
            catch (ChannelClosedException)
            {
                frame.Dispose();
                return false;
            }
            catch (OperationCanceledException)
            {
                frame.Dispose();
                return false;
            }
        }

        /// <summary>
        /// 尝试从队列中取出帧
        /// </summary>
        /// <param name="frame">取出的帧</param>
        /// <returns>是否成功取出</returns>
        public bool TryTake(out CapturedFrame? frame)
        {
            if (_disposed)
            {
                frame = null;
                return false;
            }

            return _channel.Reader.TryRead(out frame);
        }

        /// <summary>
        /// 异步从队列中取出帧
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>取出的帧</returns>
        public async Task<CapturedFrame?> TakeAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
            {
                return null;
            }

            try
            {
                return await _channel.Reader.ReadAsync(cancellationToken);
            }
            catch (ChannelClosedException)
            {
                return null;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        /// <summary>
        /// 等待可读
        /// </summary>
        public async Task<bool> WaitToReadAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
            {
                return false;
            }

            try
            {
                return await _channel.Reader.WaitToReadAsync(cancellationToken);
            }
            catch (ChannelClosedException)
            {
                return false;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        /// <summary>
        /// 清空队列
        /// </summary>
        public void Clear()
        {
            while (_channel.Reader.TryRead(out var frame))
            {
                frame?.Dispose();
            }
        }

        /// <summary>
        /// 重置丢帧计数
        /// </summary>
        public void ResetDroppedFrames()
        {
            Interlocked.Exchange(ref _droppedFrames, 0);
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

            _disposed = true;
            _channel.Writer.Complete();
            Clear();
        }
    }
}
