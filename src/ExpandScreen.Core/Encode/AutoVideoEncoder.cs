using ExpandScreen.Utils;

namespace ExpandScreen.Core.Encode
{
    /// <summary>
    /// 自动选择编码器（Initialize 时按优先级尝试，失败则回退）
    /// 优先级: NVENC > QuickSync > FFmpeg
    /// </summary>
    public sealed class AutoVideoEncoder : IVideoEncoder
    {
        private readonly VideoEncoderConfig _config;
        private IVideoEncoder? _activeEncoder;

        public AutoVideoEncoder(VideoEncoderConfig config)
        {
            _config = config ?? VideoEncoderConfig.CreateDefault();
        }

        public void Initialize(int width, int height, int framerate, int bitrate)
        {
            _activeEncoder?.Dispose();
            _activeEncoder = null;

            foreach (var (name, create) in GetCandidates())
            {
                try
                {
                    var encoder = create();
                    encoder.Initialize(width, height, framerate, bitrate);
                    _activeEncoder = encoder;
                    LogHelper.Info($"自动选择编码器成功: {name}");
                    return;
                }
                catch (Exception ex)
                {
                    LogHelper.Warning($"自动选择编码器失败({name})，回退尝试下一个: {ex.Message}");
                }
            }

            throw new InvalidOperationException("未能初始化任何可用编码器");
        }

        public byte[]? Encode(byte[] frameData)
        {
            if (_activeEncoder == null)
            {
                throw new InvalidOperationException("编码器未初始化");
            }

            return _activeEncoder.Encode(frameData);
        }

        public void Dispose()
        {
            _activeEncoder?.Dispose();
            _activeEncoder = null;
        }

        private IEnumerable<(string Name, Func<IVideoEncoder> Create)> GetCandidates()
        {
            if (FFmpegEncoderCapabilities.IsEncoderAvailable(NvencEncoder.EncoderName))
            {
                yield return ("NVENC(h264_nvenc)", () => new NvencEncoder(_config));
            }

            if (FFmpegEncoderCapabilities.IsEncoderAvailable(QuickSyncEncoder.EncoderName))
            {
                yield return ("QuickSync(h264_qsv)", () => new QuickSyncEncoder(_config));
            }

            yield return ("FFmpeg(H.264 default)", () => new FFmpegEncoder(_config));
        }
    }
}

