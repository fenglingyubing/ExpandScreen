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
        private string _manufacturer = string.Empty;
        private string _model = string.Empty;
        private string _adbStatus = string.Empty;
        private string _transport = "USB";
        private string _sessionProfile = string.Empty;
        private uint? _monitorId;
        private DeviceStatus _status = DeviceStatus.Disconnected;
        private string _statusMessage = "未连接";
        private string _summaryLine = string.Empty;
        private string? _lastError;
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
            set
            {
                if (SetProperty(ref _ipAddress, value))
                {
                    UpdateSummaryLine();
                }
            }
        }

        public string Manufacturer
        {
            get => _manufacturer;
            set
            {
                if (SetProperty(ref _manufacturer, value))
                {
                    UpdateSummaryLine();
                }
            }
        }

        public string Model
        {
            get => _model;
            set
            {
                if (SetProperty(ref _model, value))
                {
                    UpdateSummaryLine();
                }
            }
        }

        public string AdbStatus
        {
            get => _adbStatus;
            set
            {
                if (SetProperty(ref _adbStatus, value))
                {
                    UpdateSummaryLine();
                }
            }
        }

        public string Transport
        {
            get => _transport;
            set
            {
                if (SetProperty(ref _transport, value))
                {
                    UpdateSummaryLine();
                }
            }
        }

        public string SessionProfile
        {
            get => _sessionProfile;
            set
            {
                if (SetProperty(ref _sessionProfile, value))
                {
                    UpdateSummaryLine();
                }
            }
        }

        public uint? MonitorId
        {
            get => _monitorId;
            set
            {
                if (SetProperty(ref _monitorId, value))
                {
                    UpdateSummaryLine();
                }
            }
        }

        public DeviceStatus Status
        {
            get => _status;
            set
            {
                if (SetProperty(ref _status, value))
                {
                    UpdateStatusMessage();
                    UpdateSummaryLine();
                }
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        public string SummaryLine
        {
            get => _summaryLine;
            private set => SetProperty(ref _summaryLine, value);
        }

        public string? LastError
        {
            get => _lastError;
            set
            {
                if (SetProperty(ref _lastError, value))
                {
                    UpdateStatusMessage();
                    UpdateSummaryLine();
                }
            }
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

        private void UpdateSummaryLine()
        {
            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(Transport))
            {
                parts.Add(Transport);
            }

            if (!string.IsNullOrWhiteSpace(Manufacturer) || !string.IsNullOrWhiteSpace(Model))
            {
                parts.Add($"{Manufacturer} {Model}".Trim());
            }

            if (!string.IsNullOrWhiteSpace(AdbStatus))
            {
                parts.Add($"ADB:{AdbStatus}");
            }

            if (!string.IsNullOrWhiteSpace(IpAddress))
            {
                parts.Add($"IP:{IpAddress}");
            }

            if (!string.IsNullOrWhiteSpace(SessionProfile))
            {
                parts.Add(SessionProfile);
            }

            if (MonitorId.HasValue)
            {
                parts.Add($"MON:{MonitorId.Value}");
            }

            if (Status == DeviceStatus.Error && !string.IsNullOrWhiteSpace(LastError))
            {
                parts.Add(LastError!);
            }

            SummaryLine = string.Join(" • ", parts);
        }
    }
}
