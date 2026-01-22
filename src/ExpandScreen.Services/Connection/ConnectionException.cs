namespace ExpandScreen.Services.Connection
{
    /// <summary>
    /// 连接相关异常
    /// </summary>
    public class ConnectionException : Exception
    {
        public string? DeviceId { get; set; }

        public ConnectionException(string message) : base(message)
        {
        }

        public ConnectionException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        public ConnectionException(string message, string deviceId)
            : base(message)
        {
            DeviceId = deviceId;
        }

        public ConnectionException(string message, string deviceId, Exception innerException)
            : base(message, innerException)
        {
            DeviceId = deviceId;
        }
    }
}
