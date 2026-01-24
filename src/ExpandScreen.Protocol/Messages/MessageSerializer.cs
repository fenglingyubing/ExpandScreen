using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace ExpandScreen.Protocol.Messages
{
    /// <summary>
    /// 消息序列化工具类
    /// </summary>
    public static class MessageSerializer
    {
        public const uint MAGIC_NUMBER = 0x45585053; // "EXPS" in ASCII
        public const byte PROTOCOL_VERSION = 0x01;
        public const int HEADER_SIZE = 24;

        /// <summary>
        /// 序列化消息头
        /// </summary>
        public static byte[] SerializeHeader(MessageHeader header)
        {
            byte[] buffer = new byte[HEADER_SIZE];
            int offset = 0;

            // Magic (4 bytes)
            BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(offset), header.Magic);
            offset += 4;

            // Type (1 byte)
            buffer[offset++] = (byte)header.Type;

            // Version (1 byte)
            buffer[offset++] = header.Version;

            // Reserved (2 bytes)
            BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(offset), header.Reserved);
            offset += 2;

            // Timestamp (8 bytes)
            BinaryPrimitives.WriteUInt64BigEndian(buffer.AsSpan(offset), header.Timestamp);
            offset += 8;

            // PayloadLength (4 bytes)
            BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(offset), header.PayloadLength);
            offset += 4;

            // SequenceNumber (4 bytes)
            BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(offset), header.SequenceNumber);

            return buffer;
        }

        /// <summary>
        /// 反序列化消息头
        /// </summary>
        public static MessageHeader DeserializeHeader(byte[] buffer)
        {
            if (buffer.Length < HEADER_SIZE)
            {
                throw new ArgumentException($"Buffer too small. Expected at least {HEADER_SIZE} bytes, got {buffer.Length}");
            }

            int offset = 0;
            MessageHeader header = new MessageHeader();

            // Magic (4 bytes)
            header.Magic = BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(offset));
            offset += 4;

            // Type (1 byte)
            header.Type = (MessageType)buffer[offset++];

            // Version (1 byte)
            header.Version = buffer[offset++];

            // Reserved (2 bytes)
            header.Reserved = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(offset));
            offset += 2;

            // Timestamp (8 bytes)
            header.Timestamp = BinaryPrimitives.ReadUInt64BigEndian(buffer.AsSpan(offset));
            offset += 8;

            // PayloadLength (4 bytes)
            header.PayloadLength = BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(offset));
            offset += 4;

            // SequenceNumber (4 bytes)
            header.SequenceNumber = BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(offset));

            // 验证魔数
            if (header.Magic != MAGIC_NUMBER)
            {
                throw new InvalidDataException($"Invalid magic number: 0x{header.Magic:X8}, expected 0x{MAGIC_NUMBER:X8}");
            }

            return header;
        }

        /// <summary>
        /// 创建消息头
        /// </summary>
        public static MessageHeader CreateHeader(MessageType type, uint payloadLength, uint sequenceNumber, ulong? timestampMs = null)
        {
            return new MessageHeader
            {
                Magic = MAGIC_NUMBER,
                Type = type,
                Version = PROTOCOL_VERSION,
                Reserved = 0,
                Timestamp = timestampMs ?? GetTimestampMs(),
                PayloadLength = payloadLength,
                SequenceNumber = sequenceNumber
            };
        }

        /// <summary>
        /// 获取当前时间戳（毫秒）
        /// </summary>
        public static ulong GetTimestampMs()
        {
            return (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        /// <summary>
        /// 序列化JSON负载
        /// </summary>
        public static byte[] SerializeJsonPayload<T>(T payload)
        {
            return JsonSerializer.SerializeToUtf8Bytes(payload);
        }

        /// <summary>
        /// 反序列化JSON负载
        /// </summary>
        public static T? DeserializeJsonPayload<T>(byte[] data)
        {
            return JsonSerializer.Deserialize<T>(data);
        }

        /// <summary>
        /// 组合完整消息（头+负载）
        /// </summary>
        public static byte[] CombineMessage(MessageHeader header, byte[] payload)
        {
            byte[] headerBytes = SerializeHeader(header);
            byte[] message = new byte[headerBytes.Length + payload.Length];

            Buffer.BlockCopy(headerBytes, 0, message, 0, headerBytes.Length);
            Buffer.BlockCopy(payload, 0, message, headerBytes.Length, payload.Length);

            return message;
        }
    }
}
