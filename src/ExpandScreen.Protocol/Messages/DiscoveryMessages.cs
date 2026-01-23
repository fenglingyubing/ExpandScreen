namespace ExpandScreen.Protocol.Messages
{
    /// <summary>
    /// 设备发现请求（UDP广播/组播）
    /// </summary>
    public class DiscoveryRequestMessage
    {
        /// <summary>
        /// 消息类型（用于快速过滤与调试）
        /// </summary>
        public string MessageType { get; set; } = "DiscoveryRequest";

        /// <summary>
        /// 请求ID（用于匹配响应）
        /// </summary>
        public string RequestId { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>
        /// 协议版本（发现协议独立于TCP消息头的版本）
        /// </summary>
        public int DiscoveryProtocolVersion { get; set; } = 1;

        /// <summary>
        /// 客户端设备ID（Android设备标识，若可用）
        /// </summary>
        public string? ClientDeviceId { get; set; }

        /// <summary>
        /// 客户端设备名称（若可用）
        /// </summary>
        public string? ClientDeviceName { get; set; }
    }

    /// <summary>
    /// 设备发现响应（Windows返回）
    /// </summary>
    public class DiscoveryResponseMessage
    {
        public string MessageType { get; set; } = "DiscoveryResponse";
        public string RequestId { get; set; } = string.Empty;
        public int DiscoveryProtocolVersion { get; set; } = 1;

        /// <summary>
        /// Windows端标识（可使用机器名或GUID）
        /// </summary>
        public string ServerId { get; set; } = string.Empty;

        /// <summary>
        /// Windows端名称（默认机器名）
        /// </summary>
        public string ServerName { get; set; } = string.Empty;

        /// <summary>
        /// TCP监听端口（Android通过该端口建立连接）
        /// </summary>
        public int TcpPort { get; set; }

        /// <summary>
        /// WebSocket是否可用（当前为预留）
        /// </summary>
        public bool WebSocketSupported { get; set; }

        /// <summary>
        /// 服务端版本信息（用于兼容性判断）
        /// </summary>
        public string ServerVersion { get; set; } = "1.0.0";
    }
}

