using ExpandScreen.Core.Encode;

namespace ExpandScreen.Services.Connection
{
    public sealed record ConnectionManagerOptions
    {
        public int RemotePort { get; init; } = 15555;

        public int DefaultMaxSessions { get; init; } = 4;

        public int MaxHighQualitySessions { get; init; } = 1;

        public SessionVideoProfile PrimaryProfile { get; init; } = new(1920, 1080, 60, 5_000_000);

        public SessionVideoProfile DegradedProfile { get; init; } = new(1280, 720, 60, 3_000_000);

        public bool EnableVirtualDisplays { get; init; } = true;

        public Func<SessionVideoProfile, IVideoEncoder> EncoderFactory { get; init; } = profile => new NoopVideoEncoder();
    }
}

