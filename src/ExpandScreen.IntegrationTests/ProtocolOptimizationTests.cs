using System.Security.Cryptography;
using ExpandScreen.Protocol.Fec;
using ExpandScreen.Protocol.Messages;
using ExpandScreen.Protocol.Network;
using ExpandScreen.Protocol.Optimization;
using Xunit;

namespace ExpandScreen.IntegrationTests
{
    public class ProtocolOptimizationTests
    {
        [Fact]
        public void ReedSolomon_CanRecoverSingleMissingDataShard()
        {
            const int data = 8;
            const int parity = 2;
            const int shardLength = 2048;

            var codec = new ReedSolomonCodec(data, parity);
            var shards = new byte[data + parity][];
            var original = new byte[data][];

            for (int i = 0; i < data; i++)
            {
                shards[i] = new byte[shardLength];
                RandomNumberGenerator.Fill(shards[i]);
                original[i] = (byte[])shards[i].Clone();
            }
            for (int i = 0; i < parity; i++)
            {
                shards[data + i] = new byte[shardLength];
            }

            codec.EncodeParity(shards, shardLength);

            // drop one data shard
            int missingIndex = 3;
            Array.Clear(shards[missingIndex], 0, shardLength);
            var present = Enumerable.Repeat(true, data + parity).ToArray();
            present[missingIndex] = false;

            codec.DecodeMissing(shards, present, shardLength);

            Assert.True(present[missingIndex]);
            Assert.Equal(original[missingIndex], shards[missingIndex]);
        }

        [Fact]
        public void FecVideoFrameGroupCodec_CanRecoverMissingFramePayload()
        {
            const int data = 8;
            const int parity = 2;

            var codec = new FecVideoFrameGroupCodec(data, parity);
            var frames = new List<byte[]>(data);
            for (int i = 0; i < data; i++)
            {
                int len = 500 + i * 137;
                var buf = new byte[len];
                RandomNumberGenerator.Fill(buf);
                frames.Add(buf);
            }

            uint firstSeq = 1000;
            int groupId = 7;
            var (meta, parities) = codec.EncodeParity(frames, firstSeq, groupId);

            // simulate receiving all but one frame
            int missing = 5;
            var received = new Dictionary<uint, byte[]>();
            for (int i = 0; i < data; i++)
            {
                if (i == missing) continue;
                received[firstSeq + (uint)i] = frames[i];
            }

            var recovered = codec.RecoverMissing(meta, received, parities);
            Assert.Single(recovered);
            Assert.True(recovered.TryGetValue(firstSeq + (uint)missing, out var recoveredPayload));
            Assert.Equal(frames[missing], recoveredPayload);
        }

        [Fact]
        public void AdaptiveBitrate_DecreasesOnLoss()
        {
            var abr = new AdaptiveBitrateController(new AdaptiveBitrateConfig
            {
                MinBitrateBps = 500_000,
                MaxBitrateBps = 10_000_000,
                IncreaseStepBps = 500_000,
                DecreaseFactor = 0.5,
                SmoothingAlpha = 1.0,
                LossDecreaseThreshold = 0.01
            }, initialBitrateBps: 6_000_000);

            var decision = abr.Update(new ProtocolFeedbackMessage
            {
                AverageRttMs = 50,
                TotalMessagesDelta = 100,
                DroppedMessagesDelta = 10,
                ReceiveRateBps = 20_000_000
            });

            Assert.True(decision.Changed);
            Assert.True(decision.TargetBitrateBps < 6_000_000);
        }

        [Fact]
        public void AdaptiveBitrate_IncreasesOnStable()
        {
            var abr = new AdaptiveBitrateController(new AdaptiveBitrateConfig
            {
                MinBitrateBps = 500_000,
                MaxBitrateBps = 10_000_000,
                IncreaseStepBps = 500_000,
                DecreaseFactor = 0.8,
                SmoothingAlpha = 1.0,
                LossDecreaseThreshold = 0.01
            }, initialBitrateBps: 2_000_000);

            var decision = abr.Update(new ProtocolFeedbackMessage
            {
                AverageRttMs = 30,
                TotalMessagesDelta = 100,
                DroppedMessagesDelta = 0,
                ReceiveRateBps = 50_000_000
            });

            Assert.True(decision.Changed);
            Assert.True(decision.TargetBitrateBps > 2_000_000);
        }

        [Fact]
        public async Task NetworkSender_DropsMediaBeforeCritical()
        {
            using var stream = new SlowSinkStream(delayMs: 250);
            using var sender = new NetworkSender(stream, maxQueueSize: 10, sendBufferSize: 16 * 1024);

            // enqueue lots of media
            for (int i = 0; i < 200; i++)
            {
                await sender.SendMessageAsync(MessageType.VideoFrame, new byte[256]);
            }

            // enqueue critical
            await sender.SendMessageAsync(MessageType.Heartbeat, new HeartbeatMessage { Timestamp = MessageSerializer.GetTimestampMs() });

            var stats = sender.GetStatistics();
            Assert.True(stats.DroppedMediaMessages > 0, "Expected some media drops");
            Assert.Equal(0, stats.DroppedCriticalMessages);
        }

        private sealed class SlowSinkStream : Stream
        {
            private readonly int _delayMs;

            public SlowSinkStream(int delayMs)
            {
                _delayMs = delayMs;
            }

            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => 0;
            public override long Position { get => 0; set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) { }

            public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            {
                await Task.Delay(_delayMs, cancellationToken);
            }

            public override async Task FlushAsync(CancellationToken cancellationToken)
            {
                await Task.Delay(_delayMs, cancellationToken);
            }
        }
    }
}

