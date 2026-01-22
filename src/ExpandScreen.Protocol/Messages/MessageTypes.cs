namespace ExpandScreen.Protocol.Messages
{
    /// <summary>
    /// 消息类型枚举
    /// </summary>
    public enum MessageType : byte
    {
        Handshake = 0x01,
        HandshakeAck = 0x02,
        VideoFrame = 0x03,
        TouchEvent = 0x04,
        Heartbeat = 0x05,
        HeartbeatAck = 0x06
    }

    /// <summary>
    /// 消息头结构 (24字节)
    /// </summary>
    public struct MessageHeader
    {
        public uint Magic;           // 魔数 (4字节)
        public MessageType Type;     // 消息类型 (1字节)
        public byte Version;         // 协议版本 (1字节)
        public ushort Reserved;      // 预留 (2字节)
        public ulong Timestamp;      // 时间戳 (8字节)
        public uint PayloadLength;   // 负载长度 (4字节)
        public uint SequenceNumber;  // 序列号 (4字节)
    }
}
