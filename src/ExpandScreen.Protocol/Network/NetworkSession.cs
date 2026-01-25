using System.IO;
using ExpandScreen.Protocol.Messages;
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
            Func<HandshakeMessage, Task<(bool Accept, string? ErrorMessage)>>? handshakeRequestHandler = null)
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            _sender = new NetworkSender(stream);
            _receiver = new NetworkReceiver(stream);

            _heartbeatIntervalMs = heartbeatIntervalMs;
            _heartbeatTimeoutMs = heartbeatTimeoutMs;
            _lastHeartbeatReceived = DateTime.UtcNow;
            _isHandshakeCompleted = false;
            _handshakeRequestHandler = handshakeRequestHandler;

            // 订阅接收器事件
            _receiver.MessageReceived += OnMessageReceived;
            _receiver.ReceiveError += OnReceiveError;
            _receiver.ConnectionClosed += OnConnectionClosed;

            _heartbeatCts = new CancellationTokenSource();
            _heartbeatTask = Task.Run(() => HeartbeatLoopAsync(_heartbeatCts.Token));
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
                var decision = await _handshakeRequestHandler(request);
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
                _heartbeatCts.Cancel();
                _heartbeatTask?.Wait(TimeSpan.FromSeconds(5));

                _sender.Dispose();
                _receiver.Dispose();
                _heartbeatCts.Dispose();

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
}
