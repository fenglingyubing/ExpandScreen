using System.Drawing;

namespace ExpandScreen.Core.Capture
{
    /// <summary>
    /// 捕获的帧数据
    /// </summary>
    public class CapturedFrame : IDisposable
    {
        /// <summary>
        /// 帧数据（BGRA格式）
        /// </summary>
        public byte[] Data { get; set; }

        /// <summary>
        /// 帧宽度
        /// </summary>
        public int Width { get; set; }

        /// <summary>
        /// 帧高度
        /// </summary>
        public int Height { get; set; }

        /// <summary>
        /// 帧步长（每行字节数）
        /// </summary>
        public int Stride { get; set; }

        /// <summary>
        /// 捕获时间戳（毫秒）
        /// </summary>
        public long Timestamp { get; set; }

        /// <summary>
        /// 帧序列号
        /// </summary>
        public long FrameNumber { get; set; }

        /// <summary>
        /// 脏矩形区域（如果有的话）
        /// </summary>
        public Rectangle[]? DirtyRects { get; set; }

        /// <summary>
        /// 是否为完整帧
        /// </summary>
        public bool IsFullFrame { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        public CapturedFrame(int width, int height, int stride)
        {
            Width = width;
            Height = height;
            Stride = stride;
            Data = new byte[stride * height];
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            IsFullFrame = true;
        }

        /// <summary>
        /// 从现有数据创建
        /// </summary>
        public CapturedFrame(byte[] data, int width, int height, int stride, long frameNumber)
        {
            Data = data;
            Width = width;
            Height = height;
            Stride = stride;
            FrameNumber = frameNumber;
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            IsFullFrame = true;
        }

        /// <summary>
        /// 获取帧数据大小（字节）
        /// </summary>
        public int GetDataSize()
        {
            return Data.Length;
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            // 帧数据会被GC自动回收
            Data = Array.Empty<byte>();
            DirtyRects = null;
        }

        /// <summary>
        /// 克隆帧
        /// </summary>
        public CapturedFrame Clone()
        {
            var newData = new byte[Data.Length];
            Array.Copy(Data, newData, Data.Length);

            return new CapturedFrame(newData, Width, Height, Stride, FrameNumber)
            {
                Timestamp = Timestamp,
                DirtyRects = DirtyRects != null ? (Rectangle[])DirtyRects.Clone() : null,
                IsFullFrame = IsFullFrame
            };
        }
    }
}
