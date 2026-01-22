using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
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

        public MainViewModel()
        {
            // Initialize commands
            ConnectCommand = new RelayCommand(ExecuteConnect, CanExecuteConnect);
            DisconnectCommand = new RelayCommand(ExecuteDisconnect, CanExecuteDisconnect);
            RefreshDevicesCommand = new RelayCommand(ExecuteRefreshDevices);
            OpenSettingsCommand = new RelayCommand(ExecuteOpenSettings);
            ToggleThemeCommand = new RelayCommand(ExecuteToggleTheme);

            // Initialize with sample devices for demo
            InitializeSampleDevices();
        }

        #region Properties

        public ObservableCollection<DeviceViewModel> Devices { get; } = new();

        public DeviceViewModel? SelectedDevice
        {
            get => _selectedDevice;
            set
            {
                if (SetProperty(ref _selectedDevice, value))
                {
                    ((RelayCommand)ConnectCommand).RaiseCanExecuteChanged();
                    ((RelayCommand)DisconnectCommand).RaiseCanExecuteChanged();
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

        public ICommand ConnectCommand { get; }
        public ICommand DisconnectCommand { get; }
        public ICommand RefreshDevicesCommand { get; }
        public ICommand OpenSettingsCommand { get; }
        public ICommand ToggleThemeCommand { get; }

        #endregion

        #region Command Implementations

        private bool CanExecuteConnect(object? parameter)
        {
            return SelectedDevice != null && SelectedDevice.Status == DeviceStatus.Disconnected;
        }

        private async void ExecuteConnect(object? parameter)
        {
            if (SelectedDevice == null) return;

            StatusText = $"正在连接到 {SelectedDevice.DeviceName}...";
            SelectedDevice.Status = DeviceStatus.Connecting;

            // Simulate connection (replace with actual connection logic)
            await Task.Delay(2000);

            SelectedDevice.Status = DeviceStatus.Connected;
            StatusText = $"已连接到 {SelectedDevice.DeviceName}";

            ((RelayCommand)ConnectCommand).RaiseCanExecuteChanged();
            ((RelayCommand)DisconnectCommand).RaiseCanExecuteChanged();
        }

        private bool CanExecuteDisconnect(object? parameter)
        {
            return SelectedDevice != null && SelectedDevice.Status == DeviceStatus.Connected;
        }

        private async void ExecuteDisconnect(object? parameter)
        {
            if (SelectedDevice == null) return;

            StatusText = $"正在断开 {SelectedDevice.DeviceName}...";

            // Simulate disconnection (replace with actual disconnection logic)
            await Task.Delay(1000);

            SelectedDevice.Status = DeviceStatus.Disconnected;
            StatusText = "就绪";

            ((RelayCommand)ConnectCommand).RaiseCanExecuteChanged();
            ((RelayCommand)DisconnectCommand).RaiseCanExecuteChanged();
        }

        private async void ExecuteRefreshDevices()
        {
            StatusText = "正在扫描设备...";

            // Simulate device scan (replace with actual device discovery logic)
            await Task.Delay(1500);

            // For now, just refresh the sample devices
            InitializeSampleDevices();

            StatusText = $"找到 {Devices.Count} 个设备";

            await Task.Delay(2000);
            StatusText = "就绪";
        }

        private void ExecuteOpenSettings()
        {
            var settingsWindow = new SettingsWindow
            {
                Owner = Application.Current.MainWindow
            };
            settingsWindow.ShowDialog();
        }

        private void ExecuteToggleTheme()
        {
            IsDarkTheme = !IsDarkTheme;
            ApplyTheme();
        }

        #endregion

        #region Helper Methods

        private void InitializeSampleDevices()
        {
            Devices.Clear();
            Devices.Add(new DeviceViewModel
            {
                DeviceId = "device_001",
                DeviceName = "Samsung Galaxy Tab S8",
                IpAddress = "192.168.1.100",
                Status = DeviceStatus.Disconnected
            });
            Devices.Add(new DeviceViewModel
            {
                DeviceId = "device_002",
                DeviceName = "Xiaomi Pad 5",
                IpAddress = "192.168.1.101",
                Status = DeviceStatus.Disconnected
            });
        }

        private void ApplyTheme()
        {
            var app = Application.Current;
            var themeDictionary = new ResourceDictionary();

            if (IsDarkTheme)
            {
                themeDictionary.Source = new Uri("Themes/DarkTheme.xaml", UriKind.Relative);
            }
            else
            {
                themeDictionary.Source = new Uri("Themes/LightTheme.xaml", UriKind.Relative);
            }

            app.Resources.MergedDictionaries.Clear();
            app.Resources.MergedDictionaries.Add(themeDictionary);
        }

        #endregion
    }
}
