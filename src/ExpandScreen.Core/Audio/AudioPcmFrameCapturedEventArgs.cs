namespace ExpandScreen.Core.Audio
{
    public sealed class AudioPcmFrameCapturedEventArgs : EventArgs
    {
        public required ulong TimestampMs { get; init; }
        public required int SampleRate { get; init; }
        public required int Channels { get; init; }
        public required short[] Pcm16Interleaved { get; init; }
    }
}

