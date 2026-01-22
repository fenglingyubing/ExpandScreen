namespace ExpandScreen.Core.Encode
{
    /// <summary>
    /// 视频编码器配置
    /// </summary>
    public class VideoEncoderConfig
    {
        /// <summary>
        /// 视频宽度
        /// </summary>
        public int Width { get; set; } = 1920;

        /// <summary>
        /// 视频高度
        /// </summary>
        public int Height { get; set; } = 1080;

        /// <summary>
        /// 帧率
        /// </summary>
        public int Framerate { get; set; } = 60;

        /// <summary>
        /// 码率（bps）
        /// </summary>
        public int Bitrate { get; set; } = 5_000_000; // 5 Mbps

        /// <summary>
        /// 编码预设（ultrafast, superfast, veryfast, faster, fast, medium, slow, slower, veryslow）
        /// </summary>
        public string Preset { get; set; } = "ultrafast";

        /// <summary>
        /// 编码调优（film, animation, grain, stillimage, psnr, ssim, fastdecode, zerolatency）
        /// </summary>
        public string Tune { get; set; } = "zerolatency";

        /// <summary>
        /// H.264 Profile（baseline, main, high）
        /// </summary>
        public string Profile { get; set; } = "main";

        /// <summary>
        /// 关键帧间隔（GOP大小）
        /// </summary>
        public int KeyFrameInterval { get; set; } = 60; // 每秒一个关键帧

        /// <summary>
        /// 编码线程数（0为自动）
        /// </summary>
        public int ThreadCount { get; set; } = 0;

        /// <summary>
        /// 像素格式
        /// </summary>
        public string PixelFormat { get; set; } = "yuv420p";

        /// <summary>
        /// 最大B帧数量
        /// </summary>
        public int MaxBFrames { get; set; } = 0; // 低延迟模式不使用B帧

        /// <summary>
        /// 创建默认配置
        /// </summary>
        public static VideoEncoderConfig CreateDefault()
        {
            return new VideoEncoderConfig();
        }

        /// <summary>
        /// 创建低延迟配置
        /// </summary>
        public static VideoEncoderConfig CreateLowLatency(int width, int height, int framerate)
        {
            return new VideoEncoderConfig
            {
                Width = width,
                Height = height,
                Framerate = framerate,
                Preset = "ultrafast",
                Tune = "zerolatency",
                Profile = "baseline",
                MaxBFrames = 0,
                KeyFrameInterval = framerate // 每秒一个关键帧
            };
        }

        /// <summary>
        /// 创建高质量配置
        /// </summary>
        public static VideoEncoderConfig CreateHighQuality(int width, int height, int framerate)
        {
            return new VideoEncoderConfig
            {
                Width = width,
                Height = height,
                Framerate = framerate,
                Bitrate = 10_000_000, // 10 Mbps
                Preset = "medium",
                Profile = "high",
                KeyFrameInterval = framerate * 2 // 每2秒一个关键帧
            };
        }
    }
}
