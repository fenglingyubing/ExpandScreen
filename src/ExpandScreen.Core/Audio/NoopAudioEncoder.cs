using ExpandScreen.Protocol.Messages;

namespace ExpandScreen.Core.Audio
{
    public sealed class NoopAudioEncoder : IAudioEncoder
    {
        private bool _disposed;

        public AudioCodec Codec => AudioCodec.Opus;
        public bool IsInitialized { get; private set; }
        public AudioEncoderConfig Config { get; private set; } = new();

        public void Initialize(AudioEncoderConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            Config = config;
            IsInitialized = true;
        }

        public byte[] EncodeFrame(ReadOnlySpan<short> pcm16Interleaved)
        {
            if (!IsInitialized)
            {
                throw new InvalidOperationException("Audio encoder not initialized.");
            }

            return Array.Empty<byte>();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}

