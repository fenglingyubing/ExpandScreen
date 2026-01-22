namespace ExpandScreen.Core.Encode
{
    /// <summary>
    /// 视频编码器接口
    /// </summary>
    public interface IVideoEncoder
    {
        /// <summary>
        /// 初始化编码器
        /// </summary>
        void Initialize(int width, int height, int framerate, int bitrate);

        /// <summary>
        /// 编码一帧
        /// </summary>
        byte[]? Encode(byte[] frameData);

        /// <summary>
        /// 释放资源
        /// </summary>
        void Dispose();
    }
}
