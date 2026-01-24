using Concentus.Enums;
using Concentus.Structs;
using ExpandScreen.Protocol.Messages;

namespace ExpandScreen.Core.Audio
{
    public sealed class OpusAudioEncoder : IAudioEncoder
    {
        private OpusEncoder? _encoder;
        private bool _disposed;
        private readonly object _lock = new();

        public AudioCodec Codec => AudioCodec.Opus;
        public bool IsInitialized { get; private set; }
        public AudioEncoderConfig Config { get; private set; } = new();

        public void Initialize(AudioEncoderConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            lock (_lock)
            {
                ThrowIfDisposed();

                Validate(config);

                _encoder = OpusEncoder.Create(config.SampleRate, config.Channels, OpusApplication.OPUS_APPLICATION_AUDIO);
                _encoder.Bitrate = config.BitrateBps;
                _encoder.Complexity = 5;
                _encoder.SignalType = OpusSignal.OPUS_SIGNAL_MUSIC;

                Config = config;
                IsInitialized = true;
            }
        }

        public byte[] EncodeFrame(ReadOnlySpan<short> pcm16Interleaved)
        {
            lock (_lock)
            {
                ThrowIfDisposed();

                if (!IsInitialized || _encoder == null)
                {
                    throw new InvalidOperationException("Audio encoder not initialized.");
                }

                if (pcm16Interleaved.Length != Config.FrameSizeSamples)
                {
                    throw new ArgumentException(
                        $"Invalid PCM frame size. Expected {Config.FrameSizeSamples} samples, got {pcm16Interleaved.Length}.",
                        nameof(pcm16Interleaved));
                }

                // Opus maximum packet size is 1275 bytes per RFC6716, but some wrappers allow larger buffers.
                // Allocate a slightly larger buffer to be safe with headers/implementation details.
                var output = new byte[2048];
                short[] pcm = pcm16Interleaved.ToArray();

                int encodedBytes = _encoder.Encode(pcm, 0, Config.FrameSizeSamplesPerChannel, output, 0, output.Length);
                if (encodedBytes <= 0)
                {
                    return Array.Empty<byte>();
                }

                if (encodedBytes == output.Length)
                {
                    return output;
                }

                var trimmed = new byte[encodedBytes];
                Buffer.BlockCopy(output, 0, trimmed, 0, encodedBytes);
                return trimmed;
            }
        }

        private static void Validate(AudioEncoderConfig config)
        {
            if (config.SampleRate is not (8000 or 12000 or 16000 or 24000 or 48000))
            {
                throw new ArgumentOutOfRangeException(nameof(config.SampleRate), "Opus sampleRate must be 8000/12000/16000/24000/48000.");
            }

            if (config.Channels is < 1 or > 2)
            {
                throw new ArgumentOutOfRangeException(nameof(config.Channels), "Opus supports 1 or 2 channels.");
            }

            if (config.FrameDurationMs is not (10 or 20 or 40 or 60))
            {
                throw new ArgumentOutOfRangeException(nameof(config.FrameDurationMs), "FrameDurationMs must be 10/20/40/60 for this encoder.");
            }

            if (config.BitrateBps < 6000 || config.BitrateBps > 512_000)
            {
                throw new ArgumentOutOfRangeException(nameof(config.BitrateBps), "BitrateBps out of supported range (6000-512000).");
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(OpusAudioEncoder));
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed)
                {
                    return;
                }

                _encoder = null;
                _disposed = true;
            }

            GC.SuppressFinalize(this);
        }
    }
}

