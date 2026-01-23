using FFmpeg.AutoGen;

namespace ExpandScreen.Core.Encode
{
    /// <summary>
    /// Intel Quick Sync 硬件编码器（通过 FFmpeg 的 h264_qsv）
    /// </summary>
    public unsafe sealed class QuickSyncEncoder : FFmpegEncoder
    {
        public const string EncoderName = "h264_qsv";

        public QuickSyncEncoder(VideoEncoderConfig config) : base(config, EncoderName, AVPixelFormat.AV_PIX_FMT_NV12)
        {
        }

        protected override void ConfigureCodecOptions(AVCodecContext* codecContext)
        {
            // best-effort: 选项在不同平台/构建上可能不同，失败不阻断初始化
        }
    }
}

