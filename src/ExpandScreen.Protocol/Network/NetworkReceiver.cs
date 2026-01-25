using System.IO;
using System.Diagnostics;
using ExpandScreen.Protocol.Messages;
using ExpandScreen.Utils;

namespace ExpandScreen.Protocol.Network
{
    /// <summary>
    /// 网络接收器类 - 负责TCP消息接收、解包和回调处理
    /// </summary>
    public class NetworkReceiver : IDisposable
    {
        private readonly Stream? _stream;
        private readonly CancellationTokenSource _cts;
        private readonly Task _receiveTask;
        private bool _disposed;

        // 接收缓冲区
        private readonly byte[] _headerBuffer;
        private readonly int _maxPayloadSize;

        // 统计信息
        private long _totalBytesReceived;
        private long _totalMessagesReceived;
        private uint _lastSequenceNumber;
        private long _droppedMessages;
        private readonly Stopwatch _rateStopwatch;

        /// <summary>
        /// 消息接收事件
        /// </summary>
        public event EventHandler<MessageReceivedEventArgs>? MessageReceived;

        /// <summary>
        /// 接收错误事件
        /// </summary>
        public event EventHandler<Exception>? ReceiveError;

        /// <summary>
        /// 连接断开事件
        /// </summary>
        public event EventHandler? ConnectionClosed;

        /// <summary>
        /// 检测到序列号跳变（可能丢消息）
        /// </summary>
        public event EventHandler<MessageGapDetectedEventArgs>? MessageGapDetected;

        public NetworkReceiver(Stream stream, int maxPayloadSize = 10 * 1024 * 1024) // 默认最大10MB
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            _cts = new CancellationTokenSource();
            _headerBuffer = new byte[MessageSerializer.HEADER_SIZE];
            _maxPayloadSize = maxPayloadSize;
            _totalBytesReceived = 0;
            _totalMessagesReceived = 0;
            _lastSequenceNumber = 0;
            _droppedMessages = 0;
            _rateStopwatch = Stopwatch.StartNew();

            // 启动接收循环
            _receiveTask = Task.Run(() => ReceiveLoopAsync(_cts.Token));
        }

        /// <summary>
        /// 接收循环（后台线程）
        /// </summary>
        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // 1. 接收消息头
                    MessageHeader header = await ReceiveHeaderAsync(cancellationToken);

                    // 2. 验证负载大小
                    if (header.PayloadLength > _maxPayloadSize)
                    {
                        throw new InvalidDataException($"Payload too large: {header.PayloadLength} bytes (max: {_maxPayloadSize})");
                    }

                    // 3. 接收负载
                    byte[] payload = await ReceivePayloadAsync((int)header.PayloadLength, cancellationToken);

                    // 4. 更新统计信息
                    Interlocked.Add(ref _totalBytesReceived, MessageSerializer.HEADER_SIZE + payload.Length);
                    Interlocked.Increment(ref _totalMessagesReceived);

                    // 检测序列号跳变（可能丢包）
                    if (_lastSequenceNumber > 0 && header.SequenceNumber != _lastSequenceNumber + 1)
                    {
                        long dropped = header.SequenceNumber - _lastSequenceNumber - 1;
                        if (dropped > 0)
                        {
                            Interlocked.Add(ref _droppedMessages, dropped);
                            MessageGapDetected?.Invoke(this, new MessageGapDetectedEventArgs
                            {
                                DroppedMessages = dropped,
                                LastSequenceNumber = _lastSequenceNumber,
                                CurrentSequenceNumber = header.SequenceNumber
                            });
                        }
                        LogHelper.Warning($"[NetworkReceiver] Detected {dropped} dropped message(s)");
                    }
                    _lastSequenceNumber = header.SequenceNumber;

                    // 5. 触发消息接收事件
                    OnMessageReceived(header, payload);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (IOException)
                {
                    // 连接关闭
                    OnConnectionClosed();
                    break;
                }
                catch (Exception ex)
                {
                    OnReceiveError(ex);

                    // 严重错误时退出循环
                    if (ex is InvalidDataException)
                    {
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// 接收消息头
        /// </summary>
        private async Task<MessageHeader> ReceiveHeaderAsync(CancellationToken cancellationToken)
        {
            if (_stream == null)
            {
                throw new ObjectDisposedException(nameof(NetworkReceiver));
            }

            int totalRead = 0;
            while (totalRead < MessageSerializer.HEADER_SIZE)
            {
                int bytesRead = await _stream.ReadAsync(
                    _headerBuffer.AsMemory(totalRead, MessageSerializer.HEADER_SIZE - totalRead),
                    cancellationToken
                );

                if (bytesRead == 0)
                {
                    throw new IOException("Connection closed while reading header");
                }

                totalRead += bytesRead;
            }

            return MessageSerializer.DeserializeHeader(_headerBuffer);
        }

        /// <summary>
        /// 接收负载
        /// </summary>
        private async Task<byte[]> ReceivePayloadAsync(int payloadLength, CancellationToken cancellationToken)
        {
            if (_stream == null)
            {
                throw new ObjectDisposedException(nameof(NetworkReceiver));
            }

            if (payloadLength == 0)
            {
                return Array.Empty<byte>();
            }

            byte[] payload = new byte[payloadLength];
            int totalRead = 0;

            while (totalRead < payloadLength)
            {
                int bytesRead = await _stream.ReadAsync(
                    payload.AsMemory(totalRead, payloadLength - totalRead),
                    cancellationToken
                );

                if (bytesRead == 0)
                {
                    throw new IOException("Connection closed while reading payload");
                }

                totalRead += bytesRead;
            }

            return payload;
        }

        /// <summary>
        /// 获取统计信息
        /// </summary>
        public ReceiverStatistics GetStatistics()
        {
            return new ReceiverStatistics
            {
                TotalBytesReceived = _totalBytesReceived,
                TotalMessagesReceived = _totalMessagesReceived,
                LastSequenceNumber = _lastSequenceNumber,
                DroppedMessages = _droppedMessages,
                ReceiveRateBps = _rateStopwatch.Elapsed.TotalSeconds > 0
                    ? (_totalBytesReceived * 8.0) / _rateStopwatch.Elapsed.TotalSeconds
                    : 0
            };
        }

        private void OnMessageReceived(MessageHeader header, byte[] payload)
        {
            MessageReceived?.Invoke(this, new MessageReceivedEventArgs
            {
                Header = header,
                Payload = payload
            });
        }

        private void OnReceiveError(Exception ex)
        {
            ReceiveError?.Invoke(this, ex);
        }

        private void OnConnectionClosed()
        {
            ConnectionClosed?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _cts.Cancel();
                _receiveTask.Wait(TimeSpan.FromSeconds(5));

                _cts.Dispose();

                _disposed = true;
            }

            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// 消息接收事件参数
    /// </summary>
    public class MessageReceivedEventArgs : EventArgs
    {
        public MessageHeader Header { get; set; }
        public byte[] Payload { get; set; } = Array.Empty<byte>();
    }

    /// <summary>
    /// 接收器统计信息
    /// </summary>
    public class ReceiverStatistics
    {
        public long TotalBytesReceived { get; set; }
        public long TotalMessagesReceived { get; set; }
        public uint LastSequenceNumber { get; set; }
        public long DroppedMessages { get; set; }
        public double ReceiveRateBps { get; set; }
    }

    public class MessageGapDetectedEventArgs : EventArgs
    {
        public long DroppedMessages { get; set; }
        public uint LastSequenceNumber { get; set; }
        public uint CurrentSequenceNumber { get; set; }
    }
}
