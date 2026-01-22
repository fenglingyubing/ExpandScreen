namespace ExpandScreen.Services.Connection
{
    /// <summary>
    /// 连接管理器接口
    /// </summary>
    public interface IConnectionManager
    {
        /// <summary>
        /// 连接状态
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// 连接到设备
        /// </summary>
        Task<bool> ConnectAsync(string deviceId);

        /// <summary>
        /// 断开连接
        /// </summary>
        Task DisconnectAsync();

        /// <summary>
        /// 发送数据
        /// </summary>
        Task SendAsync(byte[] data);
    }
}
