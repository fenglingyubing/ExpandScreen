namespace ExpandScreen.Core.Encode
{
    /// <summary>
    /// 轻量的占位编码器：不做真实编码，仅用于会话/资源编排与测试。
    /// </summary>
    public sealed class NoopVideoEncoder : IVideoEncoder
    {
        public void Initialize(int width, int height, int framerate, int bitrate)
        {
        }

        public byte[]? Encode(byte[] frameData)
        {
            return frameData;
        }

        public void Dispose()
        {
        }
    }
}

