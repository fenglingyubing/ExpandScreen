namespace ExpandScreen.Protocol.Messages
{
    /// <summary>
    /// 握手消息（客户端发送）
    /// </summary>
    public class HandshakeMessage
    {
        public string DeviceId { get; set; } = string.Empty;
        public string DeviceName { get; set; } = string.Empty;
        public string ClientVersion { get; set; } = "1.0.0";
        public int ScreenWidth { get; set; }
        public int ScreenHeight { get; set; }
    }

    /// <summary>
    /// 握手确认消息（服务器响应）
    /// </summary>
    public class HandshakeAckMessage
    {
        public string SessionId { get; set; } = string.Empty;
        public string ServerVersion { get; set; } = "1.0.0";
        public bool Accepted { get; set; }
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// 视频帧消息
    /// </summary>
    public class VideoFrameMessage
    {
        public int FrameNumber { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public bool IsKeyFrame { get; set; }
        public byte[] Data { get; set; } = Array.Empty<byte>();
    }

    /// <summary>
    /// 音频配置消息（用于在开始推送前协商参数）。
    /// </summary>
    public class AudioConfigMessage
    {
        public bool Enabled { get; set; }
        public AudioCodec Codec { get; set; } = AudioCodec.Opus;
        public int SampleRate { get; set; } = 48000;
        public int Channels { get; set; } = 2;
        public int BitrateBps { get; set; } = 64000;
        public int FrameDurationMs { get; set; } = 20;
    }

    /// <summary>
    /// 触控事件消息
    /// </summary>
    public class TouchEventMessage
    {
        public int Action { get; set; } // 0=Down, 1=Move, 2=Up
        public int PointerId { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Pressure { get; set; }
    }

    /// <summary>
    /// 心跳消息
    /// </summary>
    public class HeartbeatMessage
    {
        public ulong Timestamp { get; set; }
    }

    /// <summary>
    /// 心跳确认消息
    /// </summary>
    public class HeartbeatAckMessage
    {
        public ulong OriginalTimestamp { get; set; }
        public ulong ResponseTimestamp { get; set; }
    }

    /// <summary>
    /// 协议反馈消息（BOTH-302）：接收端定期上报链路质量与丢消息情况，用于自适应码率/流控。
    /// </summary>
    public class ProtocolFeedbackMessage
    {
        public ulong TimestampMs { get; set; }
        public double AverageRttMs { get; set; }
        public long TotalBytesReceived { get; set; }
        public long TotalMessagesReceived { get; set; }
        public long TotalMessagesDelta { get; set; }
        public long DroppedMessagesTotal { get; set; }
        public long DroppedMessagesDelta { get; set; }
        public double ReceiveRateBps { get; set; }
    }

    /// <summary>
    /// 自适应码率控制消息（BOTH-302）：发送端广播当前目标码率，接收端可用于展示/诊断。
    /// </summary>
    public class BitrateControlMessage
    {
        public ulong TimestampMs { get; set; }
        public int TargetBitrateBps { get; set; }
        public string Reason { get; set; } = string.Empty;
    }

    /// <summary>
    /// 关键帧请求消息（BOTH-302）：接收端在检测到丢帧/画面破损时请求发送端尽快发送I帧。
    /// </summary>
    public class KeyFrameRequestMessage
    {
        public ulong TimestampMs { get; set; }
        public string Reason { get; set; } = string.Empty;
        public uint? LastReceivedSequenceNumber { get; set; }
    }

    /// <summary>
    /// FEC配置（BOTH-302）：FEC启用与分组参数。
    /// </summary>
    public class FecConfigMessage
    {
        public bool Enabled { get; set; }
        public int DataShards { get; set; } = 8;
        public int ParityShards { get; set; } = 2;
    }

    /// <summary>
    /// FEC分片消息（BOTH-302）：用于在弱网/丢消息场景下恢复丢失的视频帧消息。
    /// </summary>
    public class FecShardMessage
    {
        public int GroupId { get; set; }
        public int ShardIndex { get; set; }
        public int DataShards { get; set; }
        public int ParityShards { get; set; }
        public bool IsParity { get; set; }
        public int OriginalLength { get; set; }
        public byte[] Data { get; set; } = Array.Empty<byte>();
    }

    /// <summary>
    /// FEC分组元数据：用于恢复缺失分片时确定每个数据分片的原始长度。
    /// </summary>
    public class FecGroupMetadataMessage
    {
        public int GroupId { get; set; }
        public MessageType ProtectedType { get; set; } = MessageType.VideoFrame;
        public uint FirstSequenceNumber { get; set; }
        public int DataShards { get; set; }
        public int ParityShards { get; set; }
        public int ShardLength { get; set; }
        public int[] DataShardLengths { get; set; } = Array.Empty<int>();
    }
}
