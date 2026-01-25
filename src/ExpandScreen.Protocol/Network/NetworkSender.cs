using System.Collections.Concurrent;
using System.IO;
using System.Diagnostics;
using ExpandScreen.Protocol.Fec;
using ExpandScreen.Protocol.Messages;
using ExpandScreen.Utils;

namespace ExpandScreen.Protocol.Network
{
    /// <summary>
    /// 网络发送器类 - 负责TCP消息发送、队列管理和流控
    /// </summary>
    public class NetworkSender : IDisposable
    {
        private readonly Stream? _stream;
        private readonly ConcurrentQueue<QueuedMessage> _criticalQueue;
        private readonly ConcurrentQueue<QueuedMessage> _mediaQueue;
        private readonly SemaphoreSlim _sendLock;
        private readonly CancellationTokenSource _cts;
        private readonly Task _sendTask;

        private uint _sequenceNumber;
        private bool _disposed;

        // 流控参数
        private readonly int _maxQueueSize;
        private readonly int _sendBufferSize;
        private int _queuedBytes;
        private int _criticalQueuedMessages;
        private int _mediaQueuedMessages;
        private long _droppedMediaMessages;
        private long _droppedMediaBytes;
        private long _droppedCriticalMessages;
        private long _droppedCriticalBytes;

        // 发送统计
        private long _totalBytesSent;
        private long _totalMessagesSent;
        private readonly Stopwatch _sendRateStopwatch;

        // 码率控制（仅对 media 队列生效）
        private int _mediaTargetBitrateBps; // 0 = unlimited
        private long _mediaTokensBytes;
        private long _lastTokenRefillTimestamp;
        private readonly long _maxTokenBurstBytes;

        // FEC（BOTH-302）——按组为 VideoFrame 生成 parity 分片（额外消息）
        private readonly object _fecLock = new();
        private bool _fecEnabled;
        private int _fecDataShards;
        private int _fecParityShards;
        private int _fecGroupId;
        private uint _fecFirstSequenceNumber;
        private readonly List<byte[]> _fecPendingFrames = new();
        private readonly List<int> _fecPendingLengths = new();
        private FecVideoFrameGroupCodec? _fecGroupCodec;

        /// <summary>
        /// 发送队列中的消息数量
        /// </summary>
        public int QueuedMessageCount => _criticalQueuedMessages + _mediaQueuedMessages;

        /// <summary>
        /// 发送队列中的字节数
        /// </summary>
        public int QueuedBytes => _queuedBytes;

        /// <summary>
        /// 消息发送失败事件
        /// </summary>
        public event EventHandler<Exception>? SendError;

        public NetworkSender(Stream stream, int maxQueueSize = 1000, int sendBufferSize = 256 * 1024)
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            _criticalQueue = new ConcurrentQueue<QueuedMessage>();
            _mediaQueue = new ConcurrentQueue<QueuedMessage>();
            _sendLock = new SemaphoreSlim(1, 1);
            _cts = new CancellationTokenSource();
            _sequenceNumber = 0;
            _maxQueueSize = maxQueueSize;
            _sendBufferSize = sendBufferSize;
            _queuedBytes = 0;
            _criticalQueuedMessages = 0;
            _mediaQueuedMessages = 0;
            _sendRateStopwatch = Stopwatch.StartNew();

            _mediaTargetBitrateBps = 0;
            _mediaTokensBytes = 0;
            _lastTokenRefillTimestamp = Stopwatch.GetTimestamp();
            _maxTokenBurstBytes = Math.Max(64 * 1024, sendBufferSize * 2L);

            _fecEnabled = false;
            _fecDataShards = 8;
            _fecParityShards = 2;
            _fecGroupId = 0;

            // 启动发送循环
            _sendTask = Task.Run(() => SendLoopAsync(_cts.Token));
        }

        /// <summary>
        /// 设置 media 目标码率（bps），0 表示不限速。
        /// </summary>
        public void SetMediaTargetBitrateBps(int targetBitrateBps)
        {
            _mediaTargetBitrateBps = Math.Max(0, targetBitrateBps);
        }

        /// <summary>
        /// 配置 FEC parity（仅为 VideoFrame 生成额外 parity 消息；默认关闭）。
        /// </summary>
        public void ConfigureFec(bool enabled, int dataShards = 8, int parityShards = 2)
        {
            if (dataShards <= 1) throw new ArgumentOutOfRangeException(nameof(dataShards));
            if (parityShards <= 0) throw new ArgumentOutOfRangeException(nameof(parityShards));

            lock (_fecLock)
            {
                _fecEnabled = enabled;
                _fecDataShards = dataShards;
                _fecParityShards = parityShards;
                _fecGroupCodec = enabled ? new FecVideoFrameGroupCodec(dataShards, parityShards) : null;

                _fecPendingFrames.Clear();
                _fecPendingLengths.Clear();
                _fecFirstSequenceNumber = 0;
                _fecGroupId = 0;
            }
        }

        /// <summary>
        /// 发送消息（异步入队）
        /// </summary>
        public async Task<bool> SendMessageAsync<T>(MessageType type, T payload, ulong? timestampMs = null)
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
                uint sequence = _sequenceNumber++;
                MessageHeader header = MessageSerializer.CreateHeader(type, (uint)payloadBytes.Length, sequence, timestampMs);

                // 组合完整消息
                byte[] message = MessageSerializer.CombineMessage(header, payloadBytes);

                var queuedMessage = new QueuedMessage
                {
                    Type = type,
                    Data = message,
                    EnqueueTime = DateTime.UtcNow
                };

                EnqueueWithFlowControl(queuedMessage, isCritical: IsCritical(type));

                // BOTH-302: FEC parity（仅对 VideoFrame）
                if (type == MessageType.VideoFrame)
                {
                    await MaybeEmitFecParityAsync(payloadBytes, sequence);
                }

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
            if (_disposed || _stream == null)
            {
                throw new ObjectDisposedException(nameof(NetworkSender));
            }

            await _sendLock.WaitAsync(cancellationToken);
            try
            {
                await _stream.WriteAsync(data, cancellationToken);
                await _stream.FlushAsync(cancellationToken);
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
            QueuedMessage? pendingMedia = null;
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (_criticalQueue.TryDequeue(out var critical))
                    {
                        Interlocked.Decrement(ref _criticalQueuedMessages);
                        Interlocked.Add(ref _queuedBytes, -critical.Data.Length);

                        await SendRawAsync(critical.Data, cancellationToken);
                        Interlocked.Add(ref _totalBytesSent, critical.Data.Length);
                        Interlocked.Increment(ref _totalMessagesSent);

                        // 计算排队延迟
                        var queueDelay = DateTime.UtcNow - critical.EnqueueTime;
                        if (queueDelay.TotalMilliseconds > 100)
                        {
                            // 如果排队延迟超过100ms，记录警告
                            LogHelper.Warning($"[NetworkSender] High queue delay: {queueDelay.TotalMilliseconds:F2}ms");
                        }
                    }
                    else
                    {
                        if (pendingMedia == null)
                        {
                            if (_mediaQueue.TryDequeue(out var media))
                            {
                                Interlocked.Decrement(ref _mediaQueuedMessages);
                                Interlocked.Add(ref _queuedBytes, -media.Data.Length);
                                pendingMedia = media;
                            }
                        }

                        if (pendingMedia != null)
                        {
                            if (TryConsumeMediaTokens(pendingMedia.Data.Length, out int delayMs))
                            {
                                await SendRawAsync(pendingMedia.Data, cancellationToken);
                                Interlocked.Add(ref _totalBytesSent, pendingMedia.Data.Length);
                                Interlocked.Increment(ref _totalMessagesSent);

                                var queueDelay = DateTime.UtcNow - pendingMedia.EnqueueTime;
                                if (queueDelay.TotalMilliseconds > 100)
                                {
                                    LogHelper.Warning($"[NetworkSender] High queue delay: {queueDelay.TotalMilliseconds:F2}ms");
                                }

                                pendingMedia = null;
                            }
                            else
                            {
                                await Task.Delay(delayMs, cancellationToken);
                            }
                        }
                        else
                        {
                            // 队列为空，短暂等待
                            await Task.Delay(1, cancellationToken);
                        }
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
            while (_criticalQueue.TryDequeue(out var critical))
            {
                Interlocked.Add(ref _queuedBytes, -critical.Data.Length);
            }
            while (_mediaQueue.TryDequeue(out var media))
            {
                Interlocked.Add(ref _queuedBytes, -media.Data.Length);
            }

            _criticalQueuedMessages = 0;
            _mediaQueuedMessages = 0;
        }

        /// <summary>
        /// 获取统计信息
        /// </summary>
        public SenderStatistics GetStatistics()
        {
            return new SenderStatistics
            {
                QueuedMessageCount = QueuedMessageCount,
                QueuedBytes = _queuedBytes,
                SequenceNumber = _sequenceNumber,
                TotalBytesSent = _totalBytesSent,
                TotalMessagesSent = _totalMessagesSent,
                DroppedMediaMessages = _droppedMediaMessages,
                DroppedMediaBytes = _droppedMediaBytes,
                DroppedCriticalMessages = _droppedCriticalMessages,
                DroppedCriticalBytes = _droppedCriticalBytes,
                SendRateBps = _sendRateStopwatch.Elapsed.TotalSeconds > 0
                    ? (_totalBytesSent * 8.0) / _sendRateStopwatch.Elapsed.TotalSeconds
                    : 0,
                MediaTargetBitrateBps = _mediaTargetBitrateBps
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

        private static bool IsCritical(MessageType type)
        {
            return type switch
            {
                MessageType.VideoFrame => false,
                MessageType.AudioFrame => false,
                MessageType.FecGroupMetadata => false,
                MessageType.FecShard => false,
                _ => true
            };
        }

        private void EnqueueWithFlowControl(QueuedMessage message, bool isCritical)
        {
            // 1) 尝试为新消息腾出空间：优先丢 media
            while (QueuedMessageCount >= _maxQueueSize || _queuedBytes + message.Data.Length > _sendBufferSize)
            {
                if (!TryDropOldestMedia())
                {
                    // media 已空：只能退化为丢 critical（极少发生）
                    if (!TryDropOldestCritical())
                    {
                        break;
                    }
                }
            }

            if (isCritical)
            {
                _criticalQueue.Enqueue(message);
                Interlocked.Increment(ref _criticalQueuedMessages);
            }
            else
            {
                _mediaQueue.Enqueue(message);
                Interlocked.Increment(ref _mediaQueuedMessages);
            }

            Interlocked.Add(ref _queuedBytes, message.Data.Length);
        }

        private bool TryDropOldestMedia()
        {
            if (_mediaQueue.TryDequeue(out var dropped))
            {
                Interlocked.Decrement(ref _mediaQueuedMessages);
                Interlocked.Add(ref _queuedBytes, -dropped.Data.Length);
                Interlocked.Increment(ref _droppedMediaMessages);
                Interlocked.Add(ref _droppedMediaBytes, dropped.Data.Length);
                return true;
            }
            return false;
        }

        private bool TryDropOldestCritical()
        {
            if (_criticalQueue.TryDequeue(out var dropped))
            {
                Interlocked.Decrement(ref _criticalQueuedMessages);
                Interlocked.Add(ref _queuedBytes, -dropped.Data.Length);
                Interlocked.Increment(ref _droppedCriticalMessages);
                Interlocked.Add(ref _droppedCriticalBytes, dropped.Data.Length);
                LogHelper.Warning($"[NetworkSender] Critical queue overflow; dropped {dropped.Type}");
                return true;
            }
            return false;
        }

        private bool TryConsumeMediaTokens(int bytes, out int delayMs)
        {
            delayMs = 0;
            if (_mediaTargetBitrateBps <= 0)
            {
                return true;
            }

            double bytesPerSecond = _mediaTargetBitrateBps / 8.0;
            if (bytesPerSecond <= 0)
            {
                return true;
            }

            long now = Stopwatch.GetTimestamp();
            long elapsedTicks = now - _lastTokenRefillTimestamp;
            if (elapsedTicks > 0)
            {
                double elapsedSeconds = elapsedTicks / (double)Stopwatch.Frequency;
                long add = (long)(elapsedSeconds * bytesPerSecond);
                if (add > 0)
                {
                    _mediaTokensBytes = Math.Min(_maxTokenBurstBytes, _mediaTokensBytes + add);
                    _lastTokenRefillTimestamp = now;
                }
            }

            if (_mediaTokensBytes >= bytes)
            {
                _mediaTokensBytes -= bytes;
                return true;
            }

            long missingBytes = bytes - _mediaTokensBytes;
            double waitMs = (missingBytes / bytesPerSecond) * 1000.0;
            delayMs = (int)Math.Clamp(Math.Ceiling(waitMs), 1, 10);
            return false;
        }

        private async Task MaybeEmitFecParityAsync(byte[] framePayload, uint sequenceNumber)
        {
            FecVideoFrameGroupCodec? codec;
            int groupId;
            uint firstSeq;
            byte[][]? frames = null;
            int[]? lengths = null;

            lock (_fecLock)
            {
                if (!_fecEnabled || _fecGroupCodec == null)
                {
                    return;
                }

                if (_fecPendingFrames.Count == 0)
                {
                    _fecFirstSequenceNumber = sequenceNumber;
                }

                // 复制一份 payload，避免上层复用缓冲导致变化
                var copy = new byte[framePayload.Length];
                Buffer.BlockCopy(framePayload, 0, copy, 0, framePayload.Length);

                _fecPendingFrames.Add(copy);
                _fecPendingLengths.Add(copy.Length);

                if (_fecPendingFrames.Count < _fecDataShards)
                {
                    return;
                }

                codec = _fecGroupCodec;
                groupId = _fecGroupId++;
                firstSeq = _fecFirstSequenceNumber;
                frames = _fecPendingFrames.ToArray();
                lengths = _fecPendingLengths.ToArray();

                _fecPendingFrames.Clear();
                _fecPendingLengths.Clear();
            }

            // 生成 parity + 元数据并入队（使用独立消息类型，默认视为 critical）
            var (metadata, parity) = codec.EncodeParity(frames, firstSeq, groupId);
            metadata.DataShardLengths = lengths;

            await SendMessageAsync(MessageType.FecGroupMetadata, metadata);
            foreach (var p in parity)
            {
                await SendMessageAsync(MessageType.FecShard, p);
            }
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
        public long TotalBytesSent { get; set; }
        public long TotalMessagesSent { get; set; }
        public long DroppedMediaMessages { get; set; }
        public long DroppedMediaBytes { get; set; }
        public long DroppedCriticalMessages { get; set; }
        public long DroppedCriticalBytes { get; set; }
        public double SendRateBps { get; set; }
        public int MediaTargetBitrateBps { get; set; }
    }
}
