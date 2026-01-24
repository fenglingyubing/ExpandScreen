namespace ExpandScreen.Core.Audio
{
    public sealed class AudioEncoderConfig
    {
        public int SampleRate { get; set; } = 48000;
        public int Channels { get; set; } = 2;
        public int BitrateBps { get; set; } = 64000;
        public int FrameDurationMs { get; set; } = 20;

        public int FrameSizeSamplesPerChannel => SampleRate * FrameDurationMs / 1000;
        public int FrameSizeSamples => FrameSizeSamplesPerChannel * Channels;
    }
}

