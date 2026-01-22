namespace ExpandScreen.Services.Connection
{
    /// <summary>
    /// Android设备信息
    /// </summary>
    public class AndroidDevice
    {
        /// <summary>
        /// 设备ID (通过adb devices获取)
        /// </summary>
        public string DeviceId { get; set; } = string.Empty;

        /// <summary>
        /// 设备名称
        /// </summary>
        public string DeviceName { get; set; } = string.Empty;

        /// <summary>
        /// 设备型号
        /// </summary>
        public string Model { get; set; } = string.Empty;

        /// <summary>
        /// 设备制造商
        /// </summary>
        public string Manufacturer { get; set; } = string.Empty;

        /// <summary>
        /// Android版本
        /// </summary>
        public string AndroidVersion { get; set; } = string.Empty;

        /// <summary>
        /// SDK版本
        /// </summary>
        public int SdkVersion { get; set; }

        /// <summary>
        /// 设备状态 (device, unauthorized, offline)
        /// </summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>
        /// 是否已授权（ADB调试已启用）
        /// </summary>
        public bool IsAuthorized => Status == "device";

        /// <summary>
        /// 最后更新时间
        /// </summary>
        public DateTime LastSeen { get; set; } = DateTime.Now;

        public override string ToString()
        {
            return $"{DeviceName} ({Model}) - {DeviceId}";
        }
    }
}
