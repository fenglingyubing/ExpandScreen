using ExpandScreen.Protocol.Messages;
using ExpandScreen.Utils;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace ExpandScreen.Core.Audio
{
    public sealed class LoopbackAudioCapture : IDisposable
    {
        private readonly AudioCaptureConfig _config;
        private WasapiLoopbackCapture? _capture;
        private BufferedWaveProvider? _buffered;
        private IWaveProvider? _waveProvider16;
        private CancellationTokenSource? _cts;
        private Task? _readTask;
        private bool _disposed;

        public LoopbackAudioCapture(AudioCaptureConfig? config = null)
        {
            _config = config ?? new AudioCaptureConfig();
        }

        public bool IsRunning => _readTask != null && !_readTask.IsCompleted;

        public event EventHandler<AudioPcmFrameCapturedEventArgs>? FrameCaptured;

        public void Start()
        {
            ThrowIfDisposed();

            if (IsRunning)
            {
                return;
            }

            var capture = new WasapiLoopbackCapture();
            var buffered = new BufferedWaveProvider(capture.WaveFormat)
            {
                BufferDuration = _config.BufferDuration,
                DiscardOnBufferOverflow = true
            };

            capture.DataAvailable += (_, args) =>
            {
                try
                {
                    buffered.AddSamples(args.Buffer, 0, args.BytesRecorded);
                }
                catch (Exception ex)
                {
                    LogHelper.Debug($"LoopbackAudioCapture buffer append failed: {ex.Message}");
                }
            };

            capture.RecordingStopped += (_, args) =>
            {
                if (args.Exception != null)
                {
                    LogHelper.Warning($"LoopbackAudioCapture stopped: {args.Exception.Message}");
                }
            };

            ISampleProvider sampleProvider = buffered.ToSampleProvider();
            sampleProvider = EnsureSampleRate(sampleProvider, _config.SampleRate);
            sampleProvider = EnsureChannelCount(sampleProvider, _config.Channels);
            var waveProvider16 = new SampleToWaveProvider16(sampleProvider);

            _capture = capture;
            _buffered = buffered;
            _waveProvider16 = waveProvider16;

            _cts = new CancellationTokenSource();
            _readTask = Task.Run(() => ReadLoopAsync(_cts.Token));
            capture.StartRecording();
        }

        public async Task StopAsync()
        {
            if (!IsRunning)
            {
                return;
            }

            _cts?.Cancel();

            try
            {
                if (_readTask != null)
                {
                    await _readTask.WaitAsync(TimeSpan.FromSeconds(2));
                }
            }
            catch
            {
                // ignore
            }

            try
            {
                _capture?.StopRecording();
            }
            catch
            {
                // ignore
            }

            _capture?.Dispose();
            _capture = null;
            _buffered = null;
            _waveProvider16 = null;

            _cts?.Dispose();
            _cts = null;
            _readTask = null;
        }

        private async Task ReadLoopAsync(CancellationToken cancellationToken)
        {
            if (_waveProvider16 == null)
            {
                return;
            }

            var frameBytes = new byte[_config.FrameSizeBytes];
            int filled = 0;

            while (!cancellationToken.IsCancellationRequested)
            {
                int read;
                try
                {
                    read = _waveProvider16.Read(frameBytes, filled, frameBytes.Length - filled);
                }
                catch (Exception ex)
                {
                    LogHelper.Warning($"LoopbackAudioCapture read failed: {ex.Message}");
                    await Task.Delay(20, cancellationToken);
                    continue;
                }

                if (read <= 0)
                {
                    await Task.Delay(5, cancellationToken);
                    continue;
                }

                filled += read;
                if (filled < frameBytes.Length)
                {
                    continue;
                }

                var pcm = new short[_config.FrameSizeSamples];
                Buffer.BlockCopy(frameBytes, 0, pcm, 0, frameBytes.Length);

                FrameCaptured?.Invoke(this, new AudioPcmFrameCapturedEventArgs
                {
                    TimestampMs = MessageSerializer.GetTimestampMs(),
                    SampleRate = _config.SampleRate,
                    Channels = _config.Channels,
                    Pcm16Interleaved = pcm
                });

                filled = 0;
            }
        }

        private static ISampleProvider EnsureSampleRate(ISampleProvider provider, int targetSampleRate)
        {
            if (provider.WaveFormat.SampleRate == targetSampleRate)
            {
                return provider;
            }

            return new WdlResamplingSampleProvider(provider, targetSampleRate);
        }

        private static ISampleProvider EnsureChannelCount(ISampleProvider provider, int targetChannels)
        {
            int sourceChannels = provider.WaveFormat.Channels;
            if (sourceChannels == targetChannels)
            {
                return provider;
            }

            if (sourceChannels == 1 && targetChannels == 2)
            {
                return new MonoToStereoSampleProvider(provider);
            }

            if (sourceChannels == 2 && targetChannels == 1)
            {
                return new StereoToMonoSampleProvider(provider);
            }

            var mux = new MultiplexingSampleProvider(new[] { provider }, targetChannels);
            for (int outputChannel = 0; outputChannel < targetChannels; outputChannel++)
            {
                mux.ConnectInputToOutput(outputChannel % Math.Max(1, sourceChannels), outputChannel);
            }

            return mux;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(LoopbackAudioCapture));
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            StopAsync().GetAwaiter().GetResult();
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}

