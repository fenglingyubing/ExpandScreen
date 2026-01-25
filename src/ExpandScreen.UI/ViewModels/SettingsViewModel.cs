using System.Collections.ObjectModel;
using ExpandScreen.Protocol.Messages;
using ExpandScreen.Services.Configuration;

namespace ExpandScreen.UI.ViewModels
{
    public sealed class ResolutionOption
    {
        public ResolutionOption(int width, int height)
        {
            Width = width;
            Height = height;
        }

        public int Width { get; }
        public int Height { get; }

        public string DisplayName => $"{Width}×{Height}";
    }

    public sealed class SettingsViewModel : ViewModelBase
    {
        private bool _autoStart;
        private bool _minimizeToTray;
        private ThemeMode _theme;

        private ResolutionOption _resolution;
        private int _frameRate;
        private int _bitrateMbps;
        private VideoEncoderPreference _encoder;

        private bool _audioEnabled;
        private AudioCodec _audioCodec;
        private int _audioBitrateKbps;
        private int _audioFrameDurationMs;
        private int _audioSampleRate = 48000;
        private int _audioChannels = 2;

        private int _tcpPort;
        private int _timeoutSeconds;
        private int _reconnectAttempts;
        private int _reconnectDelayMs;

        private PerformanceMode _performanceMode;
        private int _encodingThreadCount;

        private bool _hotkeysEnabled;
        private string _hotkeyToggleMainWindow = string.Empty;
        private string _hotkeyConnectDisconnect = string.Empty;
        private string _hotkeyNextDevice = string.Empty;
        private string _hotkeyTogglePerformanceMode = string.Empty;

        private bool _updateEnabled;
        private string _updateManifestUri = string.Empty;
        private bool _updateRequireManifestSignature;
        private string _updateTrustedManifestPublicKeyPem = string.Empty;

        private string _configPath = string.Empty;
        private LoggingConfig _logging = new();

        public SettingsViewModel(AppConfig initial, string configPath)
        {
            ConfigPath = configPath;
            ThemeOptions = new ObservableCollection<ThemeMode>((ThemeMode[])Enum.GetValues(typeof(ThemeMode)));
            EncoderOptions = new ObservableCollection<VideoEncoderPreference>((VideoEncoderPreference[])Enum.GetValues(typeof(VideoEncoderPreference)));
            PerformanceModeOptions = new ObservableCollection<PerformanceMode>((PerformanceMode[])Enum.GetValues(typeof(PerformanceMode)));
            AudioCodecOptions = new ObservableCollection<AudioCodec>((AudioCodec[])Enum.GetValues(typeof(AudioCodec)));

            ResolutionOptions = new ObservableCollection<ResolutionOption>
            {
                new(1280, 720),
                new(1920, 1080),
                new(2560, 1600),
                new(3840, 2160)
            };

            _resolution = ResolutionOptions.First();
            LoadFrom(initial);
        }

        public ObservableCollection<ThemeMode> ThemeOptions { get; }
        public ObservableCollection<VideoEncoderPreference> EncoderOptions { get; }
        public ObservableCollection<PerformanceMode> PerformanceModeOptions { get; }
        public ObservableCollection<ResolutionOption> ResolutionOptions { get; }
        public ObservableCollection<AudioCodec> AudioCodecOptions { get; }

        public event EventHandler<ThemeMode>? ThemePreviewRequested;

        public string ConfigPath
        {
            get => _configPath;
            private set => SetProperty(ref _configPath, value);
        }

        public bool AutoStart
        {
            get => _autoStart;
            set => SetProperty(ref _autoStart, value);
        }

        public bool MinimizeToTray
        {
            get => _minimizeToTray;
            set => SetProperty(ref _minimizeToTray, value);
        }

        public ThemeMode Theme
        {
            get => _theme;
            set
            {
                if (SetProperty(ref _theme, value))
                {
                    ThemePreviewRequested?.Invoke(this, value);
                }
            }
        }

        public ResolutionOption Resolution
        {
            get => _resolution;
            set
            {
                if (SetProperty(ref _resolution, value))
                {
                    OnPropertyChanged(nameof(VideoSummary));
                }
            }
        }

        public int FrameRate
        {
            get => _frameRate;
            set
            {
                if (SetProperty(ref _frameRate, value))
                {
                    OnPropertyChanged(nameof(VideoSummary));
                }
            }
        }

        public int BitrateMbps
        {
            get => _bitrateMbps;
            set
            {
                if (SetProperty(ref _bitrateMbps, value))
                {
                    OnPropertyChanged(nameof(VideoSummary));
                }
            }
        }

        public VideoEncoderPreference Encoder
        {
            get => _encoder;
            set
            {
                if (SetProperty(ref _encoder, value))
                {
                    OnPropertyChanged(nameof(VideoSummary));
                }
            }
        }

        public bool AudioEnabled
        {
            get => _audioEnabled;
            set
            {
                if (SetProperty(ref _audioEnabled, value))
                {
                    OnPropertyChanged(nameof(AudioSummary));
                }
            }
        }

        public AudioCodec AudioCodec
        {
            get => _audioCodec;
            set
            {
                if (SetProperty(ref _audioCodec, value))
                {
                    OnPropertyChanged(nameof(AudioSummary));
                }
            }
        }

        public int AudioBitrateKbps
        {
            get => _audioBitrateKbps;
            set
            {
                if (SetProperty(ref _audioBitrateKbps, value))
                {
                    OnPropertyChanged(nameof(AudioSummary));
                }
            }
        }

        public int AudioFrameDurationMs
        {
            get => _audioFrameDurationMs;
            set
            {
                if (SetProperty(ref _audioFrameDurationMs, value))
                {
                    OnPropertyChanged(nameof(AudioSummary));
                }
            }
        }

        public int TcpPort
        {
            get => _tcpPort;
            set => SetProperty(ref _tcpPort, value);
        }

        public int TimeoutSeconds
        {
            get => _timeoutSeconds;
            set => SetProperty(ref _timeoutSeconds, value);
        }

        public int ReconnectAttempts
        {
            get => _reconnectAttempts;
            set => SetProperty(ref _reconnectAttempts, value);
        }

        public int ReconnectDelayMs
        {
            get => _reconnectDelayMs;
            set => SetProperty(ref _reconnectDelayMs, value);
        }

        public PerformanceMode PerformanceMode
        {
            get => _performanceMode;
            set => SetProperty(ref _performanceMode, value);
        }

        public int EncodingThreadCount
        {
            get => _encodingThreadCount;
            set => SetProperty(ref _encodingThreadCount, value);
        }

        public bool HotkeysEnabled
        {
            get => _hotkeysEnabled;
            set => SetProperty(ref _hotkeysEnabled, value);
        }

        public string HotkeyToggleMainWindow
        {
            get => _hotkeyToggleMainWindow;
            set => SetProperty(ref _hotkeyToggleMainWindow, value);
        }

        public string HotkeyConnectDisconnect
        {
            get => _hotkeyConnectDisconnect;
            set => SetProperty(ref _hotkeyConnectDisconnect, value);
        }

        public string HotkeyNextDevice
        {
            get => _hotkeyNextDevice;
            set => SetProperty(ref _hotkeyNextDevice, value);
        }

        public string HotkeyTogglePerformanceMode
        {
            get => _hotkeyTogglePerformanceMode;
            set => SetProperty(ref _hotkeyTogglePerformanceMode, value);
        }

        public bool UpdateEnabled
        {
            get => _updateEnabled;
            set => SetProperty(ref _updateEnabled, value);
        }

        public string UpdateManifestUri
        {
            get => _updateManifestUri;
            set => SetProperty(ref _updateManifestUri, value);
        }

        public bool UpdateRequireManifestSignature
        {
            get => _updateRequireManifestSignature;
            set => SetProperty(ref _updateRequireManifestSignature, value);
        }

        public string UpdateTrustedManifestPublicKeyPem
        {
            get => _updateTrustedManifestPublicKeyPem;
            set => SetProperty(ref _updateTrustedManifestPublicKeyPem, value);
        }

        public string VideoSummary => $"{Resolution.DisplayName} • {FrameRate}fps • {BitrateMbps}Mbps • {Encoder}";
        public string AudioSummary => AudioEnabled
            ? $"{AudioCodec} • {AudioBitrateKbps}kbps • {AudioFrameDurationMs}ms"
            : "关闭";

        public void LoadFrom(AppConfig config)
        {
            AutoStart = config.General.AutoStart;
            MinimizeToTray = config.General.MinimizeToTray;
            Theme = config.General.Theme;

            Resolution = ResolutionOptions.FirstOrDefault(r => r.Width == config.Video.Width && r.Height == config.Video.Height)
                ?? ResolutionOptions.First();
            FrameRate = config.Video.FrameRate;
            BitrateMbps = Math.Max(1, (int)Math.Round(config.Video.BitrateBps / 1_000_000.0));
            Encoder = config.Video.Encoder;

            TcpPort = config.Network.TcpPort;
            TimeoutSeconds = Math.Max(1, (int)Math.Round(config.Network.TimeoutMs / 1000.0));
            ReconnectAttempts = config.Network.ReconnectAttempts;
            ReconnectDelayMs = config.Network.ReconnectDelayMs;

            PerformanceMode = config.Performance.Mode;
            EncodingThreadCount = config.Performance.EncodingThreadCount;
            _logging = config.Logging ?? new LoggingConfig();

            HotkeysEnabled = config.Hotkeys.Enabled;
            HotkeyToggleMainWindow = config.Hotkeys.ToggleMainWindow ?? string.Empty;
            HotkeyConnectDisconnect = config.Hotkeys.ConnectDisconnect ?? string.Empty;
            HotkeyNextDevice = config.Hotkeys.NextDevice ?? string.Empty;
            HotkeyTogglePerformanceMode = config.Hotkeys.TogglePerformanceMode ?? string.Empty;

            UpdateEnabled = config.Update?.Enabled ?? false;
            UpdateManifestUri = config.Update?.ManifestUri ?? string.Empty;
            UpdateRequireManifestSignature = config.Update?.RequireManifestSignature ?? false;
            UpdateTrustedManifestPublicKeyPem = config.Update?.TrustedManifestPublicKeyPem ?? string.Empty;

            AudioEnabled = config.Audio.Enabled;
            AudioCodec = config.Audio.Codec;
            AudioBitrateKbps = Math.Max(6, (int)Math.Round(config.Audio.BitrateBps / 1000.0));
            AudioFrameDurationMs = config.Audio.FrameDurationMs;
            _audioSampleRate = config.Audio.SampleRate;
            _audioChannels = config.Audio.Channels;
        }

        public AppConfig ToConfig()
        {
            return new AppConfig
            {
                General = new GeneralConfig
                {
                    AutoStart = AutoStart,
                    MinimizeToTray = MinimizeToTray,
                    Theme = Theme
                },
                Video = new VideoConfig
                {
                    Width = Resolution.Width,
                    Height = Resolution.Height,
                    FrameRate = FrameRate,
                    BitrateBps = Math.Max(1, BitrateMbps) * 1_000_000,
                    Encoder = Encoder
                },
                Audio = new AudioConfig
                {
                    Enabled = AudioEnabled,
                    Codec = AudioCodec,
                    SampleRate = _audioSampleRate,
                    Channels = _audioChannels,
                    BitrateBps = Math.Max(6, AudioBitrateKbps) * 1000,
                    FrameDurationMs = AudioFrameDurationMs
                },
                Network = new NetworkConfig
                {
                    TcpPort = TcpPort,
                    TimeoutMs = Math.Max(1, TimeoutSeconds) * 1000,
                    ReconnectAttempts = ReconnectAttempts,
                    ReconnectDelayMs = ReconnectDelayMs
                },
                Performance = new PerformanceConfig
                {
                    Mode = PerformanceMode,
                    EncodingThreadCount = EncodingThreadCount
                },
                Hotkeys = new HotkeysConfig
                {
                    Enabled = HotkeysEnabled,
                    ToggleMainWindow = HotkeyToggleMainWindow ?? string.Empty,
                    ConnectDisconnect = HotkeyConnectDisconnect ?? string.Empty,
                    NextDevice = HotkeyNextDevice ?? string.Empty,
                    TogglePerformanceMode = HotkeyTogglePerformanceMode ?? string.Empty
                },
                Update = new UpdateConfig
                {
                    Enabled = UpdateEnabled,
                    ManifestUri = string.IsNullOrWhiteSpace(UpdateManifestUri) ? null : UpdateManifestUri.Trim(),
                    RequireManifestSignature = UpdateRequireManifestSignature,
                    TrustedManifestPublicKeyPem = string.IsNullOrWhiteSpace(UpdateTrustedManifestPublicKeyPem) ? null : UpdateTrustedManifestPublicKeyPem
                },
                Logging = new LoggingConfig
                {
                    MinimumLevel = _logging.MinimumLevel,
                    RetentionDays = _logging.RetentionDays,
                    RetainedFileCountLimit = _logging.RetainedFileCountLimit,
                    FileSizeLimitMb = _logging.FileSizeLimitMb,
                    RollOnFileSizeLimit = _logging.RollOnFileSizeLimit
                }
            };
        }

        public void RestoreDefaults()
        {
            LoadFrom(AppConfig.CreateDefault());
        }

        public void RestoreDefaultHotkeys()
        {
            var defaults = AppConfig.CreateDefault().Hotkeys;
            HotkeysEnabled = defaults.Enabled;
            HotkeyToggleMainWindow = defaults.ToggleMainWindow;
            HotkeyConnectDisconnect = defaults.ConnectDisconnect;
            HotkeyNextDevice = defaults.NextDevice;
            HotkeyTogglePerformanceMode = defaults.TogglePerformanceMode;
        }
    }
}
