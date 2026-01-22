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
}
