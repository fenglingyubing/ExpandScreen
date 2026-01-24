using ExpandScreen.Protocol.Messages;

namespace ExpandScreen.Core.Audio
{
    public interface IAudioEncoder : IDisposable
    {
        AudioCodec Codec { get; }
        bool IsInitialized { get; }
        AudioEncoderConfig Config { get; }

        void Initialize(AudioEncoderConfig config);

        byte[] EncodeFrame(ReadOnlySpan<short> pcm16Interleaved);
    }
}

