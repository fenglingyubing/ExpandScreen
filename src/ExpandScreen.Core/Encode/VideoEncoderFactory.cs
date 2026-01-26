using ExpandScreen.Utils;

namespace ExpandScreen.Core.Encode
{
    /// <summary>
    /// 编码器类型枚举
    /// </summary>
    public enum EncoderType
    {
        /// <summary>
        /// FFmpeg软件编码
        /// </summary>
        FFmpeg,

        /// <summary>
        /// NVIDIA NVENC硬件编码（通过FFmpeg h264_nvenc）
        /// </summary>
        NVENC,

        /// <summary>
        /// Intel QuickSync硬件编码（通过FFmpeg h264_qsv）
        /// </summary>
        QuickSync,

        /// <summary>
        /// 自动选择（优先硬件编码）
        /// </summary>
        Auto
    }

    /// <summary>
    /// 视频编码器工厂
    /// 根据系统硬件和配置创建最优的编码器
    /// </summary>
    public static class VideoEncoderFactory
    {
        /// <summary>
        /// 创建视频编码器
        /// </summary>
        /// <param name="type">编码器类型</param>
        /// <param name="config">编码器配置</param>
        /// <returns>视频编码器实例</returns>
        public static IVideoEncoder CreateEncoder(EncoderType type = EncoderType.Auto, VideoEncoderConfig? config = null)
        {
            config ??= VideoEncoderConfig.CreateDefault();

            switch (type)
            {
                case EncoderType.FFmpeg:
                    LogHelper.Info("创建FFmpeg软件编码器");
                    return new FFmpegEncoder(config);

                case EncoderType.NVENC:
                    return CreateHardwarePreferredEncoder(
                        preferred: ("NVENC(h264_nvenc)", NvencEncoder.EncoderName, () => new NvencEncoder(config)),
                        secondary: ("QuickSync(h264_qsv)", QuickSyncEncoder.EncoderName, () => new QuickSyncEncoder(config)),
                        fallback: ("FFmpeg(H.264 default)", () => new FFmpegEncoder(config)));

                case EncoderType.QuickSync:
                    return CreateHardwarePreferredEncoder(
                        preferred: ("QuickSync(h264_qsv)", QuickSyncEncoder.EncoderName, () => new QuickSyncEncoder(config)),
                        secondary: ("NVENC(h264_nvenc)", NvencEncoder.EncoderName, () => new NvencEncoder(config)),
                        fallback: ("FFmpeg(H.264 default)", () => new FFmpegEncoder(config)));

                case EncoderType.Auto:
                    return CreateAutoEncoder(config);

                default:
                    LogHelper.Warning($"未知编码器类型: {type}，使用FFmpeg");
                    return new FFmpegEncoder(config);
            }
        }

        /// <summary>
        /// 自动选择最优编码器
        /// 优先级: NVENC > QuickSync > FFmpeg
        /// </summary>
        private static IVideoEncoder CreateAutoEncoder(VideoEncoderConfig config)
        {
            LogHelper.Info("创建自动选择编码器（Initialize时按优先级尝试并回退）");
            return new AutoVideoEncoder(config);
        }

        private static IVideoEncoder CreateHardwarePreferredEncoder(
            (string Label, string EncoderName, Func<IVideoEncoder> Create) preferred,
            (string Label, string EncoderName, Func<IVideoEncoder> Create) secondary,
            (string Label, Func<IVideoEncoder> Create) fallback)
        {
            var candidates = new List<(string Name, Func<IVideoEncoder> Create)>();

            if (FFmpegEncoderCapabilities.IsEncoderAvailable(preferred.EncoderName))
            {
                candidates.Add((preferred.Label, preferred.Create));
            }
            else
            {
                LogHelper.Warning($"{preferred.Label} 不可用（FFmpeg未启用或环境不支持）。");
            }

            if (FFmpegEncoderCapabilities.IsEncoderAvailable(secondary.EncoderName))
            {
                candidates.Add((secondary.Label, secondary.Create));
            }

            candidates.Add((fallback.Label, fallback.Create));

            if (candidates.Count == 1)
            {
                LogHelper.Warning("硬件编码器不可用（FFmpeg未启用或环境不支持），使用FFmpeg");
                return fallback.Create();
            }

            LogHelper.Info($"创建硬件优先编码器（失败将回退到 {fallback.Label}）");
            return new FallbackVideoEncoder(candidates);
        }

        /// <summary>
        /// 获取系统推荐的编码器配置
        /// </summary>
        public static VideoEncoderConfig GetRecommendedConfig(int width, int height, int framerate)
        {
            // 根据分辨率推荐码率
            int bitrate;
            if (width >= 3840) // 4K
            {
                bitrate = 20_000_000; // 20 Mbps
            }
            else if (width >= 2560) // 2K
            {
                bitrate = 10_000_000; // 10 Mbps
            }
            else if (width >= 1920) // 1080p
            {
                bitrate = 5_000_000; // 5 Mbps
            }
            else // 720p或更低
            {
                bitrate = 3_000_000; // 3 Mbps
            }

            // 根据帧率调整码率
            if (framerate >= 120)
            {
                bitrate = (int)(bitrate * 1.5);
            }
            else if (framerate >= 90)
            {
                bitrate = (int)(bitrate * 1.3);
            }

            var config = VideoEncoderConfig.CreateLowLatency(width, height, framerate);
            config.Bitrate = bitrate;
            return config;
        }
    }
}
