using FFmpeg.AutoGen;

namespace ExpandScreen.Core.Encode
{
    /// <summary>
    /// NVIDIA NVENC 硬件编码器（通过 FFmpeg 的 h264_nvenc）
    /// </summary>
    public unsafe sealed class NvencEncoder : FFmpegEncoder
    {
        public const string EncoderName = "h264_nvenc";

        public NvencEncoder(VideoEncoderConfig config) : base(config, EncoderName, AVPixelFormat.AV_PIX_FMT_YUV420P)
        {
        }

        protected override void ConfigureCodecOptions(AVCodecContext* codecContext)
        {
            // best-effort: 不同 FFmpeg/NVENC 版本选项名可能不同，失败不阻断初始化
            // 目标：低延迟倾向
            // 参考：尽量使用默认值 + 少量参数提示
            // Note: 某些版本使用 p1..p7，某些使用 ll/llhq/llhp 等。
            // 这里不强制覆盖 VideoEncoderConfig 的 preset，以避免不兼容导致行为变化。
        }
    }
}

