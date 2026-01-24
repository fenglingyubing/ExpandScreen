namespace ExpandScreen.Core.Audio
{
    public sealed class AudioCaptureConfig
    {
        public int SampleRate { get; set; } = 48000;
        public int Channels { get; set; } = 2;
        public int FrameDurationMs { get; set; } = 20;
        public TimeSpan BufferDuration { get; set; } = TimeSpan.FromSeconds(2);

        public int FrameSizeSamplesPerChannel => SampleRate * FrameDurationMs / 1000;
        public int FrameSizeSamples => FrameSizeSamplesPerChannel * Channels;
        public int FrameSizeBytes => FrameSizeSamples * sizeof(short);
    }
}

