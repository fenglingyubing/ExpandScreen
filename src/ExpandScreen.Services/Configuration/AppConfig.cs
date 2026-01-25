using System.Text.Json.Serialization;
using ExpandScreen.Services.Connection;
using ExpandScreen.Protocol.Messages;

namespace ExpandScreen.Services.Configuration
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ThemeMode
    {
        Dark,
        Light
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum VideoEncoderPreference
    {
        Auto,
        Nvenc,
        QuickSync,
        FFmpeg
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum PerformanceMode
    {
        Balanced,
        LowLatency,
        Quality
    }

    public sealed class AppConfig
    {
        public GeneralConfig General { get; set; } = new();
        public VideoConfig Video { get; set; } = new();
        public AudioConfig Audio { get; set; } = new();
        public NetworkConfig Network { get; set; } = new();
        public PerformanceConfig Performance { get; set; } = new();
        public HotkeysConfig Hotkeys { get; set; } = new();
        public UpdateConfig Update { get; set; } = new();
        public LoggingConfig Logging { get; set; } = new();
        public AnalyticsConfig Analytics { get; set; } = new();

        public static AppConfig CreateDefault() => new();
    }

    public sealed class GeneralConfig
    {
        public bool AutoStart { get; set; } = false;
        public bool MinimizeToTray { get; set; } = true;
        public ThemeMode Theme { get; set; } = ThemeMode.Dark;
    }

    public sealed class VideoConfig
    {
        public int Width { get; set; } = 1920;
        public int Height { get; set; } = 1080;
        public int FrameRate { get; set; } = 60;
        public int BitrateBps { get; set; } = 5_000_000;
        public VideoEncoderPreference Encoder { get; set; } = VideoEncoderPreference.Auto;
    }

    public sealed class AudioConfig
    {
        public bool Enabled { get; set; } = false;
        public AudioCodec Codec { get; set; } = AudioCodec.Opus;
        public int SampleRate { get; set; } = 48000;
        public int Channels { get; set; } = 2;
        public int BitrateBps { get; set; } = 64000;
        public int FrameDurationMs { get; set; } = 20;
    }

    public sealed class NetworkConfig
    {
        public int TcpPort { get; set; } = WifiConnection.DefaultTcpPort;
        public int TimeoutMs { get; set; } = 5000;
        public int ReconnectAttempts { get; set; } = 5;
        public int ReconnectDelayMs { get; set; } = 1000;
    }

    public sealed class PerformanceConfig
    {
        public PerformanceMode Mode { get; set; } = PerformanceMode.Balanced;
        public int EncodingThreadCount { get; set; } = 0;
    }

    public sealed class HotkeysConfig
    {
        public bool Enabled { get; set; } = true;
        public string ToggleMainWindow { get; set; } = "Ctrl+Alt+H";
        public string ConnectDisconnect { get; set; } = "Ctrl+Alt+C";
        public string NextDevice { get; set; } = "Ctrl+Alt+Right";
        public string TogglePerformanceMode { get; set; } = "Ctrl+Alt+P";
    }

    public sealed class UpdateConfig
    {
        public bool Enabled { get; set; } = false;
        public string? ManifestUri { get; set; } = null;
        public bool RequireManifestSignature { get; set; } = false;
        public string? TrustedManifestPublicKeyPem { get; set; } = null;
    }

    public sealed class LoggingConfig
    {
        public string MinimumLevel { get; set; } = "Information";
        public int RetentionDays { get; set; } = 14;
        public int RetainedFileCountLimit { get; set; } = 14;
        public int FileSizeLimitMb { get; set; } = 20;
        public bool RollOnFileSizeLimit { get; set; } = true;
    }

    public sealed class AnalyticsConfig
    {
        public bool Enabled { get; set; } = false;
        public bool ConsentPrompted { get; set; } = false;
        public int MaxHistoryEntries { get; set; } = 500;
        public int MaxPerformanceSamples { get; set; } = 720;
        public int PerformanceSampleIntervalSeconds { get; set; } = 10;
    }
}
