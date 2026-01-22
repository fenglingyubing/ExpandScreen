namespace ExpandScreen.UI.ViewModels
{
    /// <summary>
    /// Represents a device connection status
    /// </summary>
    public enum DeviceStatus
    {
        Disconnected,
        Connecting,
        Connected,
        Error
    }

    /// <summary>
    /// Represents an Android device
    /// </summary>
    public class DeviceViewModel : ViewModelBase
    {
        private string _deviceId = string.Empty;
        private string _deviceName = string.Empty;
        private string _ipAddress = string.Empty;
        private DeviceStatus _status = DeviceStatus.Disconnected;
        private string _statusMessage = "未连接";
        private bool _isSelected;

        public string DeviceId
        {
            get => _deviceId;
            set => SetProperty(ref _deviceId, value);
        }

        public string DeviceName
        {
            get => _deviceName;
            set => SetProperty(ref _deviceName, value);
        }

        public string IpAddress
        {
            get => _ipAddress;
            set => SetProperty(ref _ipAddress, value);
        }

        public DeviceStatus Status
        {
            get => _status;
            set
            {
                if (SetProperty(ref _status, value))
                {
                    UpdateStatusMessage();
                }
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        private void UpdateStatusMessage()
        {
            StatusMessage = Status switch
            {
                DeviceStatus.Disconnected => "未连接",
                DeviceStatus.Connecting => "连接中...",
                DeviceStatus.Connected => "已连接",
                DeviceStatus.Error => "连接失败",
                _ => "未知状态"
            };
        }
    }
}
