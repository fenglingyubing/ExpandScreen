using System.Collections.Concurrent;
using System.Net.Sockets;
using ExpandScreen.Protocol.Messages;

namespace ExpandScreen.Protocol.Network
{
    /// <summary>
    /// 网络发送器类 - 负责TCP消息发送、队列管理和流控
    /// </summary>
    public class NetworkSender : IDisposable
    {
        private readonly NetworkStream? _networkStream;
        private readonly ConcurrentQueue<QueuedMessage> _sendQueue;
        private readonly SemaphoreSlim _sendLock;
        private readonly CancellationTokenSource _cts;
        private readonly Task _sendTask;

        private uint _sequenceNumber;
        private bool _disposed;

        // 流控参数
        private readonly int _maxQueueSize;
        private readonly int _sendBufferSize;
        private int _queuedBytes;

        /// <summary>
        /// 发送队列中的消息数量
        /// </summary>
        public int QueuedMessageCount => _sendQueue.Count;

        /// <summary>
        /// 发送队列中的字节数
        /// </summary>
        public int QueuedBytes => _queuedBytes;

        /// <summary>
        /// 消息发送失败事件
        /// </summary>
        public event EventHandler<Exception>? SendError;

        public NetworkSender(NetworkStream networkStream, int maxQueueSize = 1000, int sendBufferSize = 256 * 1024)
        {
            _networkStream = networkStream ?? throw new ArgumentNullException(nameof(networkStream));
            _sendQueue = new ConcurrentQueue<QueuedMessage>();
            _sendLock = new SemaphoreSlim(1, 1);
            _cts = new CancellationTokenSource();
            _sequenceNumber = 0;
            _maxQueueSize = maxQueueSize;
            _sendBufferSize = sendBufferSize;
            _queuedBytes = 0;

            // 启动发送循环
            _sendTask = Task.Run(() => SendLoopAsync(_cts.Token));
        }

        /// <summary>
        /// 发送消息（异步入队）
        /// </summary>
        public async Task<bool> SendMessageAsync<T>(MessageType type, T payload)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(NetworkSender));
            }

            try
            {
                // 序列化负载
                byte[] payloadBytes;
                if (payload is byte[] bytes)
                {
                    payloadBytes = bytes;
                }
                else
                {
                    payloadBytes = MessageSerializer.SerializeJsonPayload(payload);
                }

                // 创建消息头
                MessageHeader header = MessageSerializer.CreateHeader(type, (uint)payloadBytes.Length, _sequenceNumber++);

                // 组合完整消息
                byte[] message = MessageSerializer.CombineMessage(header, payloadBytes);

                // 检查队列大小（流控）
                if (_sendQueue.Count >= _maxQueueSize)
                {
                    // 队列已满，丢弃最旧的非关键消息
                    if (_sendQueue.TryDequeue(out var oldMessage))
                    {
                        Interlocked.Add(ref _queuedBytes, -oldMessage.Data.Length);
                    }
                }

                // 入队
                var queuedMessage = new QueuedMessage
                {
                    Type = type,
                    Data = message,
                    EnqueueTime = DateTime.UtcNow
                };

                _sendQueue.Enqueue(queuedMessage);
                Interlocked.Add(ref _queuedBytes, message.Length);

                return true;
            }
            catch (Exception ex)
            {
                OnSendError(ex);
                return false;
            }
        }

        /// <summary>
        /// 发送原始字节数据（同步发送，绕过队列）
        /// </summary>
        public async Task SendRawAsync(byte[] data, CancellationToken cancellationToken = default)
        {
            if (_disposed || _networkStream == null)
            {
                throw new ObjectDisposedException(nameof(NetworkSender));
            }

            await _sendLock.WaitAsync(cancellationToken);
            try
            {
                await _networkStream.WriteAsync(data, cancellationToken);
                await _networkStream.FlushAsync(cancellationToken);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        /// <summary>
        /// 发送循环（后台线程）
        /// </summary>
        private async Task SendLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (_sendQueue.TryDequeue(out var queuedMessage))
                    {
                        await SendRawAsync(queuedMessage.Data, cancellationToken);
                        Interlocked.Add(ref _queuedBytes, -queuedMessage.Data.Length);

                        // 计算排队延迟
                        var queueDelay = DateTime.UtcNow - queuedMessage.EnqueueTime;
                        if (queueDelay.TotalMilliseconds > 100)
                        {
                            // 如果排队延迟超过100ms，记录警告
                            Console.WriteLine($"[NetworkSender] High queue delay: {queueDelay.TotalMilliseconds:F2}ms");
                        }
                    }
                    else
                    {
                        // 队列为空，短暂等待
                        await Task.Delay(1, cancellationToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    OnSendError(ex);
                    await Task.Delay(100, cancellationToken); // 发生错误时短暂等待
                }
            }
        }

        /// <summary>
        /// 清空发送队列
        /// </summary>
        public void ClearQueue()
        {
            while (_sendQueue.TryDequeue(out var message))
            {
                Interlocked.Add(ref _queuedBytes, -message.Data.Length);
            }
        }

        /// <summary>
        /// 获取统计信息
        /// </summary>
        public SenderStatistics GetStatistics()
        {
            return new SenderStatistics
            {
                QueuedMessageCount = _sendQueue.Count,
                QueuedBytes = _queuedBytes,
                SequenceNumber = _sequenceNumber
            };
        }

        private void OnSendError(Exception ex)
        {
            SendError?.Invoke(this, ex);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _cts.Cancel();
                _sendTask.Wait(TimeSpan.FromSeconds(5));

                _cts.Dispose();
                _sendLock.Dispose();

                ClearQueue();

                _disposed = true;
            }

            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// 队列中的消息
        /// </summary>
        private class QueuedMessage
        {
            public MessageType Type { get; set; }
            public byte[] Data { get; set; } = Array.Empty<byte>();
            public DateTime EnqueueTime { get; set; }
        }
    }

    /// <summary>
    /// 发送器统计信息
    /// </summary>
    public class SenderStatistics
    {
        public int QueuedMessageCount { get; set; }
        public int QueuedBytes { get; set; }
        public uint SequenceNumber { get; set; }
    }
}
