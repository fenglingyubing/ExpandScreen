using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using ExpandScreen.Core.Encode;
using ExpandScreen.Services.Configuration;
using ExpandScreen.Services.Connection;
using ExpandScreen.UI.Services;
using ExpandScreen.UI.Views;

namespace ExpandScreen.UI.ViewModels
{
    /// <summary>
    /// Main window ViewModel
    /// </summary>
    public class MainViewModel : ViewModelBase
    {
        private bool _isDarkTheme = true;
        private DeviceViewModel? _selectedDevice;
        private string _statusText = "就绪";

        private readonly DeviceDiscoveryService _deviceDiscoveryService;
        private readonly ConnectionManager _connectionManager;

        public MainViewModel()
        {
            // Initialize commands
            ConnectDeviceCommand = new RelayCommand(ExecuteConnectDevice, CanExecuteConnectDevice);
            DisconnectDeviceCommand = new RelayCommand(ExecuteDisconnectDevice, CanExecuteDisconnectDevice);
            RefreshDevicesCommand = new RelayCommand(ExecuteRefreshDevices);
            OpenSettingsCommand = new RelayCommand(ExecuteOpenSettings);
            OpenAnalyticsCommand = new RelayCommand(ExecuteOpenAnalytics);
            OpenPerformanceTestCommand = new RelayCommand(ExecuteOpenPerformanceTest);
            ToggleThemeCommand = new RelayCommand(ExecuteToggleTheme);

            if (Application.Current is App app)
            {
                IsDarkTheme = app.ConfigService.GetSnapshot().General.Theme == ThemeMode.Dark;
            }

            _deviceDiscoveryService = new DeviceDiscoveryService();
            _deviceDiscoveryService.DeviceConnected += (_, device) => UpsertDevice(device);
            _deviceDiscoveryService.DeviceUpdated += (_, device) => UpsertDevice(device);
            _deviceDiscoveryService.DeviceDisconnected += (_, device) => RemoveDevice(device.DeviceId);
            _deviceDiscoveryService.Start();

            _connectionManager = new ConnectionManager(CreateConnectionOptionsFromConfig());
            _connectionManager.SessionUpdated += (_, session) => ApplySessionSnapshot(session);

            _ = Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                await _deviceDiscoveryService.TriggerScanAsync();
            });

            if (Application.Current != null)
            {
                Application.Current.Exit += (_, _) =>
                {
                    try
                    {
                        _deviceDiscoveryService.Dispose();
                    }
                    catch
                    {
                    }

                    try
                    {
                        _connectionManager.Dispose();
                    }
                    catch
                    {
                    }
                };
            }
        }

        #region Properties

        public ObservableCollection<DeviceViewModel> Devices { get; } = new();

        public DeviceViewModel? SelectedDevice
        {
            get => _selectedDevice;
            set
            {
                if (ReferenceEquals(_selectedDevice, value))
                {
                    return;
                }

                var previous = _selectedDevice;
                if (SetProperty(ref _selectedDevice, value))
                {
                    if (previous != null)
                    {
                        previous.IsSelected = false;
                    }

                    if (value != null)
                    {
                        value.IsSelected = true;
                    }

                    ((RelayCommand)ConnectDeviceCommand).RaiseCanExecuteChanged();
                    ((RelayCommand)DisconnectDeviceCommand).RaiseCanExecuteChanged();
                }
            }
        }

        public bool IsDarkTheme
        {
            get => _isDarkTheme;
            set => SetProperty(ref _isDarkTheme, value);
        }

        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        #endregion

        #region Commands

        public ICommand ConnectDeviceCommand { get; }
        public ICommand DisconnectDeviceCommand { get; }
        public ICommand RefreshDevicesCommand { get; }
        public ICommand OpenSettingsCommand { get; }
        public ICommand OpenAnalyticsCommand { get; }
        public ICommand OpenPerformanceTestCommand { get; }
        public ICommand ToggleThemeCommand { get; }

        #endregion

        #region Command Implementations

        private bool CanExecuteConnectDevice(object? parameter)
        {
            var device = parameter as DeviceViewModel ?? SelectedDevice;
            if (device == null)
            {
                return false;
            }

            return device.Status == DeviceStatus.Disconnected || device.Status == DeviceStatus.Error;
        }

        private async void ExecuteConnectDevice(object? parameter)
        {
            var device = parameter as DeviceViewModel ?? SelectedDevice;
            if (device == null)
            {
                return;
            }

            device.LastError = null;
            device.Status = DeviceStatus.Connecting;
            StatusText = $"正在连接到 {device.DeviceName}...";

            var result = await _connectionManager.ConnectAsync(device.DeviceId);

            if (!result.Success)
            {
                device.Status = DeviceStatus.Error;
                device.LastError = result.ErrorMessage;
                StatusText = $"连接失败：{device.DeviceName}";
                return;
            }

            device.Status = DeviceStatus.Connected;
            if (result.Session != null)
            {
                device.SessionProfile = result.Session.VideoProfile.Summary;
                device.MonitorId = result.Session.MonitorId;
            }

            TryTrackConnected(device.DeviceId);
            StatusText = $"已连接到 {device.DeviceName}";
        }

        private bool CanExecuteDisconnectDevice(object? parameter)
        {
            var device = parameter as DeviceViewModel ?? SelectedDevice;
            if (device == null)
            {
                return false;
            }

            return device.Status == DeviceStatus.Connected;
        }

        private async void ExecuteDisconnectDevice(object? parameter)
        {
            var device = parameter as DeviceViewModel ?? SelectedDevice;
            if (device == null)
            {
                return;
            }

            StatusText = $"正在断开 {device.DeviceName}...";

            _ = await _connectionManager.DisconnectAsync(device.DeviceId);

            device.Status = DeviceStatus.Disconnected;
            device.MonitorId = null;
            device.SessionProfile = string.Empty;
            device.LastError = null;
            TryTrackDisconnected(device.DeviceId);
            StatusText = "就绪";

            ((RelayCommand)ConnectDeviceCommand).RaiseCanExecuteChanged();
            ((RelayCommand)DisconnectDeviceCommand).RaiseCanExecuteChanged();
        }

        private async void ExecuteRefreshDevices()
        {
            StatusText = "正在扫描设备...";

            await _deviceDiscoveryService.TriggerScanAsync();
            StatusText = $"找到 {Devices.Count} 个设备";
            TryTrackFeature("RefreshDevices");
        }

        private void ExecuteOpenSettings()
        {
            TryTrackFeature("OpenSettings");
            var settingsWindow = new SettingsWindow
            {
                Owner = System.Windows.Application.Current.MainWindow
            };
            settingsWindow.ShowDialog();
        }

        private void ExecuteOpenAnalytics()
        {
            TryTrackFeature("OpenAnalytics");
            var window = new AnalyticsWindow
            {
                Owner = System.Windows.Application.Current.MainWindow
            };
            window.ShowDialog();
        }

        private void ExecuteOpenPerformanceTest()
        {
            TryTrackFeature("OpenPerformanceTest");
            var window = new PerformanceTestWindow
            {
                Owner = System.Windows.Application.Current.MainWindow
            };
            window.ShowDialog();
        }

        private async void ExecuteToggleTheme()
        {
            ThemeMode newTheme = IsDarkTheme ? ThemeMode.Light : ThemeMode.Dark;
            ThemeManager.ApplyTheme(newTheme);
            IsDarkTheme = newTheme == ThemeMode.Dark;

            if (Application.Current is not App app)
            {
                return;
            }

            var updated = app.ConfigService.GetSnapshot();
            updated.General.Theme = newTheme;
            await app.ConfigService.SaveAsync(updated);
            TryTrackFeature("ToggleTheme");
        }

        #endregion

        #region Helper Methods

        private static void TryTrackFeature(string name)
        {
            if (Application.Current is not App app)
            {
                return;
            }

            app.AnalyticsService.TrackFeatureUsed(name);
        }

        private static void TryTrackConnected(string deviceId)
        {
            if (Application.Current is not App app)
            {
                return;
            }

            app.AnalyticsService.TrackConnected(deviceId);
        }

        private static void TryTrackDisconnected(string deviceId)
        {
            if (Application.Current is not App app)
            {
                return;
            }

            app.AnalyticsService.TrackDisconnected(deviceId);
        }

        private static ConnectionManagerOptions CreateConnectionOptionsFromConfig()
        {
            if (Application.Current is not App app)
            {
                return new ConnectionManagerOptions();
            }

            var config = app.ConfigService.GetSnapshot();
            var primary = new SessionVideoProfile(
                config.Video.Width,
                config.Video.Height,
                config.Video.FrameRate,
                config.Video.BitrateBps);

            var degraded = new SessionVideoProfile(
                1280,
                720,
                60,
                Math.Min(config.Video.BitrateBps, 3_000_000));

            return new ConnectionManagerOptions
            {
                RemotePort = 15555,
                DefaultMaxSessions = 4,
                MaxHighQualitySessions = 1,
                PrimaryProfile = primary,
                DegradedProfile = degraded,
                EnableVirtualDisplays = true,
                EncoderFactory = profile =>
                {
                    var snapshot = app.ConfigService.GetSnapshot();
                    var encoderType = snapshot.Video.Encoder switch
                    {
                        VideoEncoderPreference.Nvenc => EncoderType.NVENC,
                        VideoEncoderPreference.QuickSync => EncoderType.QuickSync,
                        VideoEncoderPreference.FFmpeg => EncoderType.FFmpeg,
                        _ => EncoderType.Auto
                    };

                    var encoderConfig = snapshot.Performance.Mode switch
                    {
                        PerformanceMode.Quality => VideoEncoderConfig.CreateHighQuality(profile.Width, profile.Height, profile.RefreshRate),
                        PerformanceMode.LowLatency => VideoEncoderConfig.CreateLowLatency(profile.Width, profile.Height, profile.RefreshRate),
                        _ => VideoEncoderFactory.GetRecommendedConfig(profile.Width, profile.Height, profile.RefreshRate)
                    };

                    encoderConfig.Bitrate = profile.BitrateBps;
                    encoderConfig.ThreadCount = snapshot.Performance.EncodingThreadCount;

                    return VideoEncoderFactory.CreateEncoder(encoderType, encoderConfig);
                }
            };
        }

        public void ToggleConnectDisconnectSelected()
        {
            var device = SelectedDevice ?? Devices.FirstOrDefault();
            if (device == null)
            {
                StatusText = "未发现设备";
                return;
            }

            if (device.Status is DeviceStatus.Disconnected or DeviceStatus.Error)
            {
                if (ConnectDeviceCommand.CanExecute(device))
                {
                    ConnectDeviceCommand.Execute(device);
                }

                return;
            }

            if (DisconnectDeviceCommand.CanExecute(device))
            {
                DisconnectDeviceCommand.Execute(device);
            }
        }

        public void SelectNextDevice()
        {
            if (Devices.Count == 0)
            {
                StatusText = "未发现设备";
                return;
            }

            if (SelectedDevice == null)
            {
                SelectedDevice = Devices[0];
                return;
            }

            int index = Devices.IndexOf(SelectedDevice);
            int nextIndex = index < 0 ? 0 : (index + 1) % Devices.Count;
            SelectedDevice = Devices[nextIndex];
        }

        public async Task CyclePerformanceModeAsync()
        {
            if (Application.Current is not App app)
            {
                return;
            }

            var snapshot = app.ConfigService.GetSnapshot();
            snapshot.Performance.Mode = snapshot.Performance.Mode switch
            {
                PerformanceMode.Balanced => PerformanceMode.LowLatency,
                PerformanceMode.LowLatency => PerformanceMode.Quality,
                _ => PerformanceMode.Balanced
            };

            await app.ConfigService.SaveAsync(snapshot);

            StatusText = $"性能模式：{snapshot.Performance.Mode switch { PerformanceMode.Balanced => \"均衡\", PerformanceMode.LowLatency => \"低延迟\", _ => \"高质量\" }}";
        }

        private void UpsertDevice(AndroidDevice device)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var existing = Devices.FirstOrDefault(d => d.DeviceId == device.DeviceId);
                if (existing == null)
                {
                    existing = new DeviceViewModel
                    {
                        DeviceId = device.DeviceId,
                        DeviceName = string.IsNullOrWhiteSpace(device.DeviceName) ? device.DeviceId : device.DeviceName,
                        Manufacturer = device.Manufacturer,
                        Model = device.Model,
                        AdbStatus = device.Status,
                        Transport = "USB"
                    };
                    Devices.Add(existing);
                }
                else
                {
                    existing.DeviceName = string.IsNullOrWhiteSpace(device.DeviceName) ? existing.DeviceName : device.DeviceName;
                    existing.Manufacturer = device.Manufacturer;
                    existing.Model = device.Model;
                    existing.AdbStatus = device.Status;
                }

                if (SelectedDevice == null)
                {
                    SelectedDevice = existing;
                }

                ((RelayCommand)ConnectDeviceCommand).RaiseCanExecuteChanged();
                ((RelayCommand)DisconnectDeviceCommand).RaiseCanExecuteChanged();
            });
        }

        private void RemoveDevice(string deviceId)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var existing = Devices.FirstOrDefault(d => d.DeviceId == deviceId);
                if (existing == null)
                {
                    return;
                }

                if (SelectedDevice == existing)
                {
                    SelectedDevice = null;
                }

                Devices.Remove(existing);
            });
        }

        private void ApplySessionSnapshot(DeviceSessionSnapshot session)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var device = Devices.FirstOrDefault(d => d.DeviceId == session.DeviceId);
                if (device == null)
                {
                    return;
                }

                if (session.State == DeviceSessionState.Disconnected)
                {
                    device.Status = DeviceStatus.Disconnected;
                    device.SessionProfile = string.Empty;
                    device.MonitorId = null;
                    device.LastError = null;
                    return;
                }

                device.SessionProfile = session.VideoProfile.Summary;
                device.MonitorId = session.MonitorId;

                device.Status = session.State switch
                {
                    DeviceSessionState.Connecting => DeviceStatus.Connecting,
                    DeviceSessionState.Connected => DeviceStatus.Connected,
                    DeviceSessionState.Error => DeviceStatus.Error,
                    _ => device.Status
                };

                device.LastError = session.LastError;
            });
        }

        #endregion
    }
}
