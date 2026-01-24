using System.Net;
using System.Net.Sockets;
using ExpandScreen.Core.Audio;
using ExpandScreen.Protocol.Messages;
using ExpandScreen.Protocol.Network;

namespace ExpandScreen.IntegrationTests
{
    public class AudioPipelineTests
    {
        [Fact]
        public void OpusEncoder_EncodesNonEmptyPayload()
        {
            var config = new AudioEncoderConfig
            {
                SampleRate = 48000,
                Channels = 2,
                BitrateBps = 64000,
                FrameDurationMs = 20
            };

            using var encoder = new OpusAudioEncoder();
            encoder.Initialize(config);

            short[] pcm = GenerateSineWave(config, 440);
            byte[] encoded = encoder.EncodeFrame(pcm);

            Assert.NotNull(encoded);
            Assert.NotEmpty(encoded);
        }

        [Fact]
        public async Task NetworkSender_CanSendAudioConfigAndFrames_WithTimestampOverride()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;

            TcpClient? serverTcp = null;
            TcpClient? clientTcp = null;

            try
            {
                var connectTask = Task.Run(async () =>
                {
                    var client = new TcpClient();
                    await client.ConnectAsync(IPAddress.Loopback, port);
                    return client;
                });

                serverTcp = await listener.AcceptTcpClientAsync();
                clientTcp = await connectTask;

                using var sender = new NetworkSender(clientTcp.GetStream());
                using var receiver = new NetworkReceiver(serverTcp.GetStream());

                var received = new List<(MessageHeader Header, byte[] Payload)>();
                var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                receiver.MessageReceived += (_, e) =>
                {
                    received.Add((e.Header, e.Payload));
                    if (received.Count >= 2)
                    {
                        tcs.TrySetResult(true);
                    }
                };

                var audioConfig = new AudioConfigMessage
                {
                    Enabled = true,
                    Codec = AudioCodec.Opus,
                    SampleRate = 48000,
                    Channels = 2,
                    BitrateBps = 64000,
                    FrameDurationMs = 20
                };

                await sender.SendMessageAsync(MessageType.AudioConfig, audioConfig);

                var encoderConfig = new AudioEncoderConfig
                {
                    SampleRate = 48000,
                    Channels = 2,
                    BitrateBps = 64000,
                    FrameDurationMs = 20
                };

                using var encoder = new OpusAudioEncoder();
                encoder.Initialize(encoderConfig);

                short[] pcm = GenerateSineWave(encoderConfig, 220);
                byte[] encoded = encoder.EncodeFrame(pcm);

                const ulong audioTimestampMs = 1234567890;
                await sender.SendMessageAsync(MessageType.AudioFrame, encoded, audioTimestampMs);

                var completed = await Task.WhenAny(tcs.Task, Task.Delay(3000));
                Assert.Equal(tcs.Task, completed);

                Assert.Equal(2, received.Count);

                Assert.Equal(MessageType.AudioConfig, received[0].Header.Type);
                var parsedConfig = MessageSerializer.DeserializeJsonPayload<AudioConfigMessage>(received[0].Payload);
                Assert.NotNull(parsedConfig);
                Assert.True(parsedConfig!.Enabled);
                Assert.Equal(AudioCodec.Opus, parsedConfig.Codec);

                Assert.Equal(MessageType.AudioFrame, received[1].Header.Type);
                Assert.Equal(audioTimestampMs, received[1].Header.Timestamp);
                Assert.Equal(encoded, received[1].Payload);
            }
            finally
            {
                serverTcp?.Close();
                clientTcp?.Close();
                listener.Stop();
            }
        }

        private static short[] GenerateSineWave(AudioEncoderConfig config, double frequencyHz)
        {
            int frameSizeSamplesPerChannel = config.FrameSizeSamplesPerChannel;
            int channels = config.Channels;
            var pcm = new short[frameSizeSamplesPerChannel * channels];

            double amplitude = 0.2 * short.MaxValue;
            for (int i = 0; i < frameSizeSamplesPerChannel; i++)
            {
                double t = i / (double)config.SampleRate;
                short sample = (short)(Math.Sin(2 * Math.PI * frequencyHz * t) * amplitude);
                for (int ch = 0; ch < channels; ch++)
                {
                    pcm[i * channels + ch] = sample;
                }
            }

            return pcm;
        }
    }
}

