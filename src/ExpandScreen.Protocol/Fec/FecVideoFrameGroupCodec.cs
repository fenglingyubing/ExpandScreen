using ExpandScreen.Protocol.Messages;

namespace ExpandScreen.Protocol.Fec
{
    /// <summary>
    /// 将一组 VideoFrame 负载编码为 FEC parity，并在缺失时恢复（BOTH-302）。
    /// </summary>
    public sealed class FecVideoFrameGroupCodec
    {
        private readonly ReedSolomonCodec _codec;

        public int DataShards => _codec.DataShards;
        public int ParityShards => _codec.ParityShards;

        public FecVideoFrameGroupCodec(int dataShards, int parityShards)
        {
            _codec = new ReedSolomonCodec(dataShards, parityShards);
        }

        public (FecGroupMetadataMessage Metadata, IReadOnlyList<FecShardMessage> ParityShards) EncodeParity(
            IReadOnlyList<byte[]> frames,
            uint firstSequenceNumber,
            int groupId)
        {
            if (frames.Count != DataShards)
            {
                throw new ArgumentException($"frames.Count must be {DataShards}", nameof(frames));
            }

            int shardLength = 0;
            var lengths = new int[DataShards];
            for (int i = 0; i < DataShards; i++)
            {
                lengths[i] = frames[i].Length;
                shardLength = Math.Max(shardLength, frames[i].Length);
            }
            if (shardLength == 0)
            {
                shardLength = 1;
            }

            var shards = new byte[_codec.TotalShards][];
            for (int i = 0; i < _codec.TotalShards; i++)
            {
                shards[i] = new byte[shardLength];
            }

            for (int i = 0; i < DataShards; i++)
            {
                Buffer.BlockCopy(frames[i], 0, shards[i], 0, frames[i].Length);
            }

            _codec.EncodeParity(shards, shardLength);

            var metadata = new FecGroupMetadataMessage
            {
                GroupId = groupId,
                ProtectedType = MessageType.VideoFrame,
                FirstSequenceNumber = firstSequenceNumber,
                DataShards = DataShards,
                ParityShards = ParityShards,
                ShardLength = shardLength,
                DataShardLengths = lengths
            };

            var parityMessages = new List<FecShardMessage>(ParityShards);
            for (int p = 0; p < ParityShards; p++)
            {
                parityMessages.Add(new FecShardMessage
                {
                    GroupId = groupId,
                    ShardIndex = DataShards + p,
                    DataShards = DataShards,
                    ParityShards = ParityShards,
                    IsParity = true,
                    OriginalLength = shardLength,
                    Data = shards[DataShards + p]
                });
            }

            return (metadata, parityMessages);
        }

        public IReadOnlyDictionary<uint, byte[]> RecoverMissing(
            FecGroupMetadataMessage metadata,
            IReadOnlyDictionary<uint, byte[]> receivedFrames,
            IReadOnlyList<FecShardMessage> receivedParity)
        {
            if (metadata.ProtectedType != MessageType.VideoFrame)
            {
                throw new ArgumentException("metadata.ProtectedType must be VideoFrame", nameof(metadata));
            }

            if (metadata.DataShards != DataShards || metadata.ParityShards != ParityShards)
            {
                throw new ArgumentException("FEC shard counts mismatch", nameof(metadata));
            }

            if (metadata.DataShardLengths.Length != DataShards)
            {
                throw new ArgumentException("metadata.DataShardLengths length mismatch", nameof(metadata));
            }

            int shardLength = metadata.ShardLength;
            if (shardLength <= 0)
            {
                throw new ArgumentException("metadata.ShardLength must be > 0", nameof(metadata));
            }

            var shards = new byte[_codec.TotalShards][];
            var present = new bool[_codec.TotalShards];
            for (int i = 0; i < _codec.TotalShards; i++)
            {
                shards[i] = new byte[shardLength];
            }

            for (int i = 0; i < DataShards; i++)
            {
                uint seq = metadata.FirstSequenceNumber + (uint)i;
                if (receivedFrames.TryGetValue(seq, out var payload))
                {
                    Buffer.BlockCopy(payload, 0, shards[i], 0, Math.Min(payload.Length, shardLength));
                    present[i] = true;
                }
            }

            foreach (var parity in receivedParity)
            {
                if (!parity.IsParity) continue;
                if (parity.GroupId != metadata.GroupId) continue;
                int idx = parity.ShardIndex;
                if (idx < DataShards || idx >= _codec.TotalShards) continue;
                Buffer.BlockCopy(parity.Data, 0, shards[idx], 0, Math.Min(parity.Data.Length, shardLength));
                present[idx] = true;
            }

            // 如果没有缺失，不必恢复
            bool anyMissing = false;
            for (int i = 0; i < DataShards; i++)
            {
                if (!present[i])
                {
                    anyMissing = true;
                    break;
                }
            }
            if (!anyMissing)
            {
                return new Dictionary<uint, byte[]>();
            }

            _codec.DecodeMissing(shards, present, shardLength);

            var recovered = new Dictionary<uint, byte[]>();
            for (int i = 0; i < DataShards; i++)
            {
                uint seq = metadata.FirstSequenceNumber + (uint)i;
                if (receivedFrames.ContainsKey(seq)) continue;

                int len = metadata.DataShardLengths[i];
                len = Math.Clamp(len, 0, shardLength);
                var payload = new byte[len];
                Buffer.BlockCopy(shards[i], 0, payload, 0, len);
                recovered[seq] = payload;
            }

            return recovered;
        }
    }
}

