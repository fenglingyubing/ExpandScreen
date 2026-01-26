using System.IO;
using ExpandScreen.Protocol.Messages;
using ExpandScreen.Protocol.Fec;
using ExpandScreen.Protocol.Optimization;
using ExpandScreen.Utils;

namespace ExpandScreen.Protocol.Network
{
    /// <summary>
    /// 网络会话类 - 集成握手、心跳和消息收发
    /// </summary>
    public class NetworkSession : IDisposable
    {
        private readonly Stream _stream;
        private readonly NetworkSender _sender;
        private readonly NetworkReceiver _receiver;
        private readonly CancellationTokenSource _heartbeatCts;
        private readonly Task? _heartbeatTask;
        private readonly CancellationTokenSource _optimizationCts;
        private readonly Task? _optimizationTask;
        private readonly Func<HandshakeMessage, Task<(bool Accept, string? ErrorMessage)>>? _handshakeRequestHandler;

        private bool _isHandshakeCompleted;
        private bool _disposed;
        private string? _sessionId;

        // 心跳配置
        private readonly int _heartbeatIntervalMs;
        private readonly int _heartbeatTimeoutMs;
        private DateTime _lastHeartbeatReceived;
        private readonly object _rttLock = new();
        private double _lastHeartbeatRttMs;
        private double _averageHeartbeatRttMs;
        private int _heartbeatRttSamples;

        // BOTH-302: 协议优化
        private readonly AdaptiveBitrateController _adaptiveBitrate;
        private readonly TimeSpan _feedbackInterval;
        private long _lastFeedbackTotalBytes;
        private long _lastFeedbackTotalMessages;
        private long _lastFeedbackDroppedMessages;
        private DateTime _lastKeyFrameRequestSentAt;
        private readonly TimeSpan _minKeyFrameRequestInterval;

        private readonly object _fecLock = new();
        private readonly Dictionary<int, FecGroupState> _fecGroups = new();
        private readonly Dictionary<(int Data, int Parity), FecVideoFrameGroupCodec> _fecCodecs = new();
        private readonly Dictionary<uint, byte[]> _fecVideoFrameBuffer = new();
        private const int MaxBufferedVideoFrames = 512;

        /// <summary>
        /// 会话ID
        /// </summary>
        public string? SessionId => _sessionId;

        /// <summary>
        /// 是否已完成握手
        /// </summary>
        public bool IsHandshakeCompleted => _isHandshakeCompleted;

        /// <summary>
        /// 握手完成事件
        /// </summary>
        public event EventHandler<HandshakeCompletedEventArgs>? HandshakeCompleted;

        /// <summary>
        /// 心跳超时事件
        /// </summary>
        public event EventHandler? HeartbeatTimeout;

        /// <summary>
        /// 消息接收事件
        /// </summary>
        public event EventHandler<MessageReceivedEventArgs>? MessageReceived;

        /// <summary>
        /// BOTH-302：建议调整发送端目标码率（由 ProtocolFeedback 驱动）。
        /// </summary>
        public event EventHandler<BitrateSuggestedEventArgs>? BitrateSuggested;

        /// <summary>
        /// BOTH-302：收到对端关键帧请求。
        /// </summary>
        public event EventHandler<KeyFrameRequestedEventArgs>? KeyFrameRequested;

        /// <summary>
        /// 连接关闭事件
        /// </summary>
        public event EventHandler? ConnectionClosed;

        /// <summary>
        /// 会话错误事件
        /// </summary>
        public event EventHandler<Exception>? SessionError;

        public NetworkSession(
            Stream stream,
            int heartbeatIntervalMs = 5000,
            int heartbeatTimeoutMs = 15000,
            Func<HandshakeMessage, Task<(bool Accept, string? ErrorMessage)>>? handshakeRequestHandler = null,
            int protocolFeedbackIntervalMs = 1000,
            int minKeyFrameRequestIntervalMs = 1000)
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            _sender = new NetworkSender(stream);
            _receiver = new NetworkReceiver(stream);

            _heartbeatIntervalMs = heartbeatIntervalMs;
            _heartbeatTimeoutMs = heartbeatTimeoutMs;
            _lastHeartbeatReceived = DateTime.UtcNow;
            _isHandshakeCompleted = false;
            _handshakeRequestHandler = handshakeRequestHandler;
            _adaptiveBitrate = new AdaptiveBitrateController();
            _feedbackInterval = TimeSpan.FromMilliseconds(Math.Max(200, protocolFeedbackIntervalMs));
            _lastKeyFrameRequestSentAt = DateTime.MinValue;
            _minKeyFrameRequestInterval = TimeSpan.FromMilliseconds(Math.Max(200, minKeyFrameRequestIntervalMs));

            // 订阅接收器事件
            _receiver.MessageReceived += OnMessageReceived;
            _receiver.ReceiveError += OnReceiveError;
            _receiver.ConnectionClosed += OnConnectionClosed;
            _receiver.MessageGapDetected += OnMessageGapDetected;

            _heartbeatCts = new CancellationTokenSource();
            _heartbeatTask = Task.Run(() => HeartbeatLoopAsync(_heartbeatCts.Token));

            _optimizationCts = new CancellationTokenSource();
            _optimizationTask = Task.Run(() => OptimizationLoopAsync(_optimizationCts.Token));
        }

        /// <summary>
        /// 执行握手（客户端）
        /// </summary>
        public async Task<bool> PerformHandshakeAsync(HandshakeMessage handshakeMessage, int timeoutMs = 5000)
        {
            try
            {
                // 发送握手消息
                await _sender.SendMessageAsync(MessageType.Handshake, handshakeMessage);

                // 等待握手确认（带超时）
                var tcs = new TaskCompletionSource<bool>();
                using var cts = new CancellationTokenSource(timeoutMs);

                void handler(object? sender, HandshakeCompletedEventArgs e)
                {
                    tcs.TrySetResult(e.Accepted);
                }

                HandshakeCompleted += handler;

                try
                {
                    cts.Token.Register(() => tcs.TrySetCanceled());
                    return await tcs.Task;
                }
                finally
                {
                    HandshakeCompleted -= handler;
                }
            }
            catch (Exception ex)
            {
                OnSessionError(ex);
                return false;
            }
        }

        /// <summary>
        /// 处理握手请求（服务器端）
        /// </summary>
        public async Task<bool> RespondToHandshakeAsync(HandshakeMessage request, bool accept, string? errorMessage = null)
        {
            try
            {
                _sessionId = Guid.NewGuid().ToString();

                var response = new HandshakeAckMessage
                {
                    SessionId = _sessionId,
                    ServerVersion = "1.0.0",
                    Accepted = accept,
                    ErrorMessage = errorMessage
                };

                await _sender.SendMessageAsync(MessageType.HandshakeAck, response);

                if (accept)
                {
                    _isHandshakeCompleted = true;
                }

                return true;
            }
            catch (Exception ex)
            {
                OnSessionError(ex);
                return false;
            }
        }

        /// <summary>
        /// 发送消息
        /// </summary>
        public async Task<bool> SendMessageAsync<T>(MessageType type, T payload, ulong? timestampMs = null)
        {
            if (!_isHandshakeCompleted && type != MessageType.Handshake && type != MessageType.HandshakeAck)
            {
                throw new InvalidOperationException("Handshake not completed");
            }

            return await _sender.SendMessageAsync(type, payload, timestampMs);
        }

        /// <summary>
        /// 发送心跳
        /// </summary>
        private async Task SendHeartbeatAsync()
        {
            var heartbeat = new HeartbeatMessage
            {
                Timestamp = MessageSerializer.GetTimestampMs()
            };

            await _sender.SendMessageAsync(MessageType.Heartbeat, heartbeat);
        }

        /// <summary>
        /// 心跳循环
        /// </summary>
        private async Task HeartbeatLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_heartbeatIntervalMs, cancellationToken);

                    // 检查心跳超时
                    if (_isHandshakeCompleted)
                    {
                        var timeSinceLastHeartbeat = DateTime.UtcNow - _lastHeartbeatReceived;
                        if (timeSinceLastHeartbeat.TotalMilliseconds > _heartbeatTimeoutMs)
                        {
                            OnHeartbeatTimeout();
                            break;
                        }

                        // 发送心跳
                        await SendHeartbeatAsync();
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    OnSessionError(ex);
                }
            }
        }

        /// <summary>
        /// 处理接收到的消息
        /// </summary>
        private void OnMessageReceived(object? sender, MessageReceivedEventArgs e)
        {
            try
            {
                switch (e.Header.Type)
                {
                    case MessageType.Handshake:
                        if (_handshakeRequestHandler != null)
                        {
                            HandleHandshake(e.Payload);
                        }
                        else
                        {
                            MessageReceived?.Invoke(this, e);
                        }
                        break;

                    case MessageType.HandshakeAck:
                        HandleHandshakeAck(e.Payload);
                        break;

                    case MessageType.Heartbeat:
                        HandleHeartbeat(e.Payload);
                        break;

                    case MessageType.HeartbeatAck:
                        HandleHeartbeatAck(e.Payload);
                        break;

                    case MessageType.VideoFrame:
                        BufferVideoFrame(e.Header.SequenceNumber, e.Payload);
                        MessageReceived?.Invoke(this, e);
                        break;

                    case MessageType.ProtocolFeedback:
                        HandleProtocolFeedback(e.Payload);
                        break;

                    case MessageType.BitrateControl:
                        HandleBitrateControl(e.Payload);
                        MessageReceived?.Invoke(this, e);
                        break;

                    case MessageType.KeyFrameRequest:
                        HandleKeyFrameRequest(e.Payload);
                        break;

                    case MessageType.FecGroupMetadata:
                        HandleFecGroupMetadata(e.Payload);
                        break;

                    case MessageType.FecShard:
                        HandleFecShard(e.Payload);
                        break;

                    default:
                        // 转发其他消息到应用层
                        MessageReceived?.Invoke(this, e);
                        break;
                }
            }
            catch (Exception ex)
            {
                OnSessionError(ex);
            }
        }

        private void BufferVideoFrame(uint sequenceNumber, byte[] payload)
        {
            lock (_fecLock)
            {
                if (_fecVideoFrameBuffer.Count >= MaxBufferedVideoFrames)
                {
                    _fecVideoFrameBuffer.Clear();
                }

                // NetworkReceiver 为每条消息创建独立 payload 缓冲区；这里直接缓存引用即可，避免额外拷贝。
                _fecVideoFrameBuffer[sequenceNumber] = payload;
            }
        }

        private void OnMessageGapDetected(object? sender, MessageGapDetectedEventArgs e)
        {
            if (!_isHandshakeCompleted)
            {
                return;
            }

            // 丢消息后节流请求关键帧，避免风暴
            var now = DateTime.UtcNow;
            if (now - _lastKeyFrameRequestSentAt < _minKeyFrameRequestInterval)
            {
                return;
            }

            _lastKeyFrameRequestSentAt = now;
            _ = _sender.SendMessageAsync(MessageType.KeyFrameRequest, new KeyFrameRequestMessage
            {
                TimestampMs = MessageSerializer.GetTimestampMs(),
                Reason = $"gap_detected(dropped={e.DroppedMessages})",
                LastReceivedSequenceNumber = e.LastSequenceNumber
            });
        }

        private async void HandleHandshake(byte[] payload)
        {
            var request = MessageSerializer.DeserializeJsonPayload<HandshakeMessage>(payload);
            if (request == null)
            {
                await RespondToHandshakeAsync(new HandshakeMessage(), accept: false, errorMessage: "Invalid handshake payload");
                return;
            }

            try
            {
                var handler = _handshakeRequestHandler;
                if (handler == null)
                {
                    await RespondToHandshakeAsync(request, accept: false, errorMessage: "Handshake handler not configured");
                    return;
                }

                var decision = await handler(request);
                await RespondToHandshakeAsync(request, accept: decision.Accept, errorMessage: decision.ErrorMessage);
            }
            catch (Exception ex)
            {
                OnSessionError(ex);
                await RespondToHandshakeAsync(request, accept: false, errorMessage: ex.Message);
            }
        }

        /// <summary>
        /// 处理握手确认
        /// </summary>
        private void HandleHandshakeAck(byte[] payload)
        {
            var ackMessage = MessageSerializer.DeserializeJsonPayload<HandshakeAckMessage>(payload);
            if (ackMessage != null)
            {
                _sessionId = ackMessage.SessionId;
                _isHandshakeCompleted = ackMessage.Accepted;

                HandshakeCompleted?.Invoke(this, new HandshakeCompletedEventArgs
                {
                    Accepted = ackMessage.Accepted,
                    SessionId = ackMessage.SessionId,
                    ErrorMessage = ackMessage.ErrorMessage
                });
            }
        }

        /// <summary>
        /// 处理心跳消息
        /// </summary>
        private async void HandleHeartbeat(byte[] payload)
        {
            var heartbeat = MessageSerializer.DeserializeJsonPayload<HeartbeatMessage>(payload);
            if (heartbeat != null)
            {
                // 更新最后心跳时间
                _lastHeartbeatReceived = DateTime.UtcNow;

                // 发送心跳确认
                var ack = new HeartbeatAckMessage
                {
                    OriginalTimestamp = heartbeat.Timestamp,
                    ResponseTimestamp = MessageSerializer.GetTimestampMs()
                };

                await _sender.SendMessageAsync(MessageType.HeartbeatAck, ack);
            }
        }

        /// <summary>
        /// 处理心跳确认
        /// </summary>
        private void HandleHeartbeatAck(byte[] payload)
        {
            var ack = MessageSerializer.DeserializeJsonPayload<HeartbeatAckMessage>(payload);
            if (ack != null)
            {
                // 更新最后心跳时间
                _lastHeartbeatReceived = DateTime.UtcNow;

                // 计算RTT（往返时间）
                ulong currentTime = MessageSerializer.GetTimestampMs();
                ulong rtt = currentTime - ack.OriginalTimestamp;

                lock (_rttLock)
                {
                    _lastHeartbeatRttMs = rtt;
                    _averageHeartbeatRttMs = (_averageHeartbeatRttMs * _heartbeatRttSamples + rtt) / (_heartbeatRttSamples + 1);
                    _heartbeatRttSamples++;
                }

                if (rtt > 100)
                {
                    LogHelper.Warning($"[NetworkSession] High heartbeat RTT: {rtt}ms");
                }
                else
                {
                    LogHelper.Debug($"[NetworkSession] Heartbeat RTT: {rtt}ms");
                }
            }
        }

        private void HandleProtocolFeedback(byte[] payload)
        {
            var feedback = MessageSerializer.DeserializeJsonPayload<ProtocolFeedbackMessage>(payload);
            if (feedback == null)
            {
                return;
            }

            var decision = _adaptiveBitrate.Update(feedback);
            if (!decision.Changed)
            {
                return;
            }

            _sender.SetMediaTargetBitrateBps(decision.TargetBitrateBps);
            BitrateSuggested?.Invoke(this, new BitrateSuggestedEventArgs
            {
                TargetBitrateBps = decision.TargetBitrateBps,
                Reason = decision.Reason,
                LossRatio = decision.LossRatio,
                EstimatedBandwidthBps = decision.EstimatedBandwidthBps,
                AverageRttMs = decision.AverageRttMs
            });
        }

        private void HandleBitrateControl(byte[] payload)
        {
            // 目前仅用于诊断：不自动调整本端
            _ = MessageSerializer.DeserializeJsonPayload<BitrateControlMessage>(payload);
        }

        private void HandleKeyFrameRequest(byte[] payload)
        {
            var msg = MessageSerializer.DeserializeJsonPayload<KeyFrameRequestMessage>(payload);
            if (msg == null) return;

            KeyFrameRequested?.Invoke(this, new KeyFrameRequestedEventArgs
            {
                Reason = msg.Reason,
                LastReceivedSequenceNumber = msg.LastReceivedSequenceNumber
            });
        }

        private void HandleFecGroupMetadata(byte[] payload)
        {
            var meta = MessageSerializer.DeserializeJsonPayload<FecGroupMetadataMessage>(payload);
            if (meta == null) return;

            lock (_fecLock)
            {
                _fecGroups[meta.GroupId] = new FecGroupState(meta);
            }

            TryRecoverFecGroup(meta.GroupId);
        }

        private void HandleFecShard(byte[] payload)
        {
            var shard = MessageSerializer.DeserializeJsonPayload<FecShardMessage>(payload);
            if (shard == null) return;
            if (!shard.IsParity) return;

            lock (_fecLock)
            {
                if (!_fecGroups.TryGetValue(shard.GroupId, out var state))
                {
                    // metadata 可能稍后到，先创建占位
                    state = new FecGroupState(null);
                    _fecGroups[shard.GroupId] = state;
                }
                state.Parity.Add(shard);
            }

            TryRecoverFecGroup(shard.GroupId);
        }

        private void TryRecoverFecGroup(int groupId)
        {
            FecGroupMetadataMessage? meta;
            List<FecShardMessage> parity;
            Dictionary<uint, byte[]> received;
            FecVideoFrameGroupCodec? codec;

            lock (_fecLock)
            {
                if (!_fecGroups.TryGetValue(groupId, out var state) || state.Metadata == null)
                {
                    return;
                }

                meta = state.Metadata;
                parity = state.Parity.ToList();
                codec = GetFecCodec(meta.DataShards, meta.ParityShards);

                // 收集当前已收到的 data frames（按 seq）
                received = new Dictionary<uint, byte[]>();
                for (int i = 0; i < meta.DataShards; i++)
                {
                    uint seq = meta.FirstSequenceNumber + (uint)i;
                    if (_fecVideoFrameBuffer.TryGetValue(seq, out var payload))
                    {
                        received[seq] = payload;
                    }
                }
            }

            if (meta == null || codec == null) return;

            IReadOnlyDictionary<uint, byte[]> recovered;
            try
            {
                recovered = codec.RecoverMissing(meta, received, parity);
            }
            catch (Exception ex)
            {
                LogHelper.Warning($"[NetworkSession] FEC recover failed for group {groupId}: {ex.Message}");
                return;
            }

            if (recovered.Count == 0)
            {
                return;
            }

            foreach (var kv in recovered.OrderBy(k => k.Key))
            {
                var header = MessageSerializer.CreateHeader(MessageType.VideoFrame, (uint)kv.Value.Length, kv.Key);
                MessageReceived?.Invoke(this, new MessageReceivedEventArgs
                {
                    Header = header,
                    Payload = kv.Value
                });

                lock (_fecLock)
                {
                    _fecVideoFrameBuffer[kv.Key] = kv.Value;
                }
            }

            lock (_fecLock)
            {
                // group 完成后清理
                if (_fecGroups.TryGetValue(groupId, out var state) && state.Metadata != null)
                {
                    for (int i = 0; i < state.Metadata.DataShards; i++)
                    {
                        uint seq = state.Metadata.FirstSequenceNumber + (uint)i;
                        _fecVideoFrameBuffer.Remove(seq);
                    }
                }
                _fecGroups.Remove(groupId);
            }
        }

        private FecVideoFrameGroupCodec GetFecCodec(int dataShards, int parityShards)
        {
            var key = (Data: dataShards, Parity: parityShards);
            lock (_fecLock)
            {
                if (_fecCodecs.TryGetValue(key, out var codec))
                {
                    return codec;
                }

                codec = new FecVideoFrameGroupCodec(dataShards, parityShards);
                _fecCodecs[key] = codec;
                return codec;
            }
        }

        /// <summary>
        /// 获取统计信息
        /// </summary>
        public SessionStatistics GetStatistics()
        {
            double lastRtt;
            double avgRtt;
            lock (_rttLock)
            {
                lastRtt = _lastHeartbeatRttMs;
                avgRtt = _averageHeartbeatRttMs;
            }

            return new SessionStatistics
            {
                SessionId = _sessionId,
                IsHandshakeCompleted = _isHandshakeCompleted,
                SenderStats = _sender.GetStatistics(),
                ReceiverStats = _receiver.GetStatistics(),
                TimeSinceLastHeartbeat = (DateTime.UtcNow - _lastHeartbeatReceived).TotalMilliseconds,
                LastHeartbeatRttMs = lastRtt,
                AverageHeartbeatRttMs = avgRtt
            };
        }

        private async Task OptimizationLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_feedbackInterval, cancellationToken);

                    if (!_isHandshakeCompleted)
                    {
                        continue;
                    }

                    var stats = _receiver.GetStatistics();
                    long bytes = stats.TotalBytesReceived;
                    long messages = stats.TotalMessagesReceived;
                    long dropped = stats.DroppedMessages;

                    long deltaBytes = bytes - _lastFeedbackTotalBytes;
                    long deltaMessages = messages - _lastFeedbackTotalMessages;
                    long deltaDropped = dropped - _lastFeedbackDroppedMessages;

                    _lastFeedbackTotalBytes = bytes;
                    _lastFeedbackTotalMessages = messages;
                    _lastFeedbackDroppedMessages = dropped;

                    double avgRtt;
                    lock (_rttLock)
                    {
                        avgRtt = _averageHeartbeatRttMs;
                    }

                    var feedback = new ProtocolFeedbackMessage
                    {
                        TimestampMs = MessageSerializer.GetTimestampMs(),
                        AverageRttMs = avgRtt,
                        TotalBytesReceived = bytes,
                        TotalMessagesReceived = messages,
                        TotalMessagesDelta = deltaMessages,
                        DroppedMessagesTotal = dropped,
                        DroppedMessagesDelta = deltaDropped,
                        ReceiveRateBps = stats.ReceiveRateBps
                    };

                    await _sender.SendMessageAsync(MessageType.ProtocolFeedback, feedback);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    OnSessionError(ex);
                }
            }
        }

        private void OnReceiveError(object? sender, Exception e)
        {
            OnSessionError(e);
        }

        private void OnConnectionClosed(object? sender, EventArgs e)
        {
            LogHelper.Info("[NetworkSession] Connection closed");
            ConnectionClosed?.Invoke(this, EventArgs.Empty);
        }

        private void OnHeartbeatTimeout()
        {
            LogHelper.Warning("[NetworkSession] Heartbeat timeout");
            HeartbeatTimeout?.Invoke(this, EventArgs.Empty);
        }

        private void OnSessionError(Exception ex)
        {
            LogHelper.Error("[NetworkSession] Session error", ex);
            SessionError?.Invoke(this, ex);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                try
                {
                    _heartbeatCts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
                catch (Exception ex)
                {
                    LogHelper.Debug($"[NetworkSession] Heartbeat cancel failed: {ex.GetBaseException().Message}");
                }

                try
                {
                    _heartbeatTask?.Wait(TimeSpan.FromSeconds(5));
                }
                catch (Exception ex)
                {
                    LogHelper.Debug($"[NetworkSession] Heartbeat task wait failed: {ex.GetBaseException().Message}");
                }

                try
                {
                    _optimizationCts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
                catch (Exception ex)
                {
                    LogHelper.Debug($"[NetworkSession] Optimization cancel failed: {ex.GetBaseException().Message}");
                }

                try
                {
                    _optimizationTask?.Wait(TimeSpan.FromSeconds(5));
                }
                catch (Exception ex)
                {
                    LogHelper.Debug($"[NetworkSession] Optimization task wait failed: {ex.GetBaseException().Message}");
                }

                try
                {
                    _optimizationCts.Dispose();
                }
                catch
                {
                }

                try
                {
                    _sender.Dispose();
                }
                catch
                {
                }

                try
                {
                    _receiver.Dispose();
                }
                catch
                {
                }

                try
                {
                    _heartbeatCts.Dispose();
                }
                catch
                {
                }

                _disposed = true;
            }

            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// 握手完成事件参数
    /// </summary>
    public class HandshakeCompletedEventArgs : EventArgs
    {
        public bool Accepted { get; set; }
        public string SessionId { get; set; } = string.Empty;
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// 会话统计信息
    /// </summary>
    public class SessionStatistics
    {
        public string? SessionId { get; set; }
        public bool IsHandshakeCompleted { get; set; }
        public SenderStatistics? SenderStats { get; set; }
        public ReceiverStatistics? ReceiverStats { get; set; }
        public double TimeSinceLastHeartbeat { get; set; }
        public double LastHeartbeatRttMs { get; set; }
        public double AverageHeartbeatRttMs { get; set; }
    }

    public class BitrateSuggestedEventArgs : EventArgs
    {
        public int TargetBitrateBps { get; set; }
        public string Reason { get; set; } = string.Empty;
        public double LossRatio { get; set; }
        public double EstimatedBandwidthBps { get; set; }
        public double AverageRttMs { get; set; }
    }

    public class KeyFrameRequestedEventArgs : EventArgs
    {
        public string Reason { get; set; } = string.Empty;
        public uint? LastReceivedSequenceNumber { get; set; }
    }

    internal sealed class FecGroupState
    {
        public FecGroupMetadataMessage? Metadata { get; set; }
        public List<FecShardMessage> Parity { get; } = new();

        public FecGroupState(FecGroupMetadataMessage? metadata)
        {
            Metadata = metadata;
        }
    }
}
