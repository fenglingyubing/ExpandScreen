namespace ExpandScreen.Services.Connection
{
    public sealed class UsbConnectionOptions
    {
        public int MaxReconnectAttempts { get; init; } = 5;
        public int ReconnectDelayMs { get; init; } = 2000;
        public int MonitorIntervalMs { get; init; } = 1000;
    }
}

