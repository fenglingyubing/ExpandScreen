namespace ExpandScreen.Services.Connection
{
    public enum DeviceSessionState
    {
        Disconnected,
        Connecting,
        Connected,
        Error
    }

    public sealed record SessionVideoProfile(int Width, int Height, int RefreshRate, int BitrateBps)
    {
        public string Summary
        {
            get
            {
                var mbps = BitrateBps / 1_000_000d;
                return $"{Width}×{Height}@{RefreshRate} • {mbps:0.#}Mbps";
            }
        }
    }

    public sealed record DeviceSessionSnapshot(
        string DeviceId,
        DeviceSessionState State,
        int LocalPort,
        int RemotePort,
        uint? MonitorId,
        SessionVideoProfile VideoProfile,
        string? LastError);

    public sealed record ConnectDeviceResult(
        bool Success,
        DeviceSessionSnapshot? Session,
        bool UsedDegradedProfile,
        string? ErrorMessage);
}

