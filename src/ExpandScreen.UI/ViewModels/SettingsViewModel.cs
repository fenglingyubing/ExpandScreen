using System.Collections.ObjectModel;
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

        private int _tcpPort;
        private int _timeoutSeconds;
        private int _reconnectAttempts;
        private int _reconnectDelayMs;

        private PerformanceMode _performanceMode;
        private int _encodingThreadCount;
        private string _configPath = string.Empty;
        private LoggingConfig _logging = new();

        public SettingsViewModel(AppConfig initial, string configPath)
        {
            ConfigPath = configPath;
            ThemeOptions = new ObservableCollection<ThemeMode>((ThemeMode[])Enum.GetValues(typeof(ThemeMode)));
            EncoderOptions = new ObservableCollection<VideoEncoderPreference>((VideoEncoderPreference[])Enum.GetValues(typeof(VideoEncoderPreference)));
            PerformanceModeOptions = new ObservableCollection<PerformanceMode>((PerformanceMode[])Enum.GetValues(typeof(PerformanceMode)));

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

        public string VideoSummary => $"{Resolution.DisplayName} • {FrameRate}fps • {BitrateMbps}Mbps • {Encoder}";

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
    }
}
