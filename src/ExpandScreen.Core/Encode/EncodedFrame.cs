namespace ExpandScreen.Core.Encode
{
    /// <summary>
    /// 编码后的帧数据
    /// </summary>
    public class EncodedFrame : IDisposable
    {
        /// <summary>
        /// 编码后的数据
        /// </summary>
        public byte[] Data { get; set; }

        /// <summary>
        /// 数据长度
        /// </summary>
        public int Length { get; set; }

        /// <summary>
        /// 帧序列号
        /// </summary>
        public long FrameNumber { get; set; }

        /// <summary>
        /// 编码时间戳（毫秒）
        /// </summary>
        public long Timestamp { get; set; }

        /// <summary>
        /// 是否为关键帧（I帧）
        /// </summary>
        public bool IsKeyFrame { get; set; }

        /// <summary>
        /// 帧类型（I/P/B）
        /// </summary>
        public FrameType Type { get; set; }

        /// <summary>
        /// 编码耗时（毫秒）
        /// </summary>
        public double EncodeTimeMs { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        public EncodedFrame(int capacity)
        {
            Data = new byte[capacity];
            Length = 0;
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        /// <summary>
        /// 从现有数据创建
        /// </summary>
        public EncodedFrame(byte[] data, int length, long frameNumber, bool isKeyFrame)
        {
            Data = data;
            Length = length;
            FrameNumber = frameNumber;
            IsKeyFrame = isKeyFrame;
            Type = isKeyFrame ? FrameType.I : FrameType.P;
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        /// <summary>
        /// 克隆编码帧
        /// </summary>
        public EncodedFrame Clone()
        {
            var newData = new byte[Length];
            Array.Copy(Data, newData, Length);

            return new EncodedFrame(newData, Length, FrameNumber, IsKeyFrame)
            {
                Timestamp = Timestamp,
                Type = Type,
                EncodeTimeMs = EncodeTimeMs
            };
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            Data = Array.Empty<byte>();
        }
    }

    /// <summary>
    /// 帧类型枚举
    /// </summary>
    public enum FrameType
    {
        /// <summary>
        /// 关键帧（Intra frame）
        /// </summary>
        I,

        /// <summary>
        /// 预测帧（Predicted frame）
        /// </summary>
        P,

        /// <summary>
        /// 双向预测帧（Bi-directional predicted frame）
        /// </summary>
        B
    }
}
