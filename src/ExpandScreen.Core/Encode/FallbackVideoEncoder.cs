using ExpandScreen.Utils;

namespace ExpandScreen.Core.Encode
{
    /// <summary>
    /// 带回退机制的视频编码器：按候选列表顺序尝试 Initialize，失败则继续下一个。
    /// </summary>
    public sealed class FallbackVideoEncoder : IVideoEncoder
    {
        private readonly IReadOnlyList<(string Name, Func<IVideoEncoder> Create)> _candidates;
        private IVideoEncoder? _activeEncoder;

        public FallbackVideoEncoder(IReadOnlyList<(string Name, Func<IVideoEncoder> Create)> candidates)
        {
            _candidates = candidates ?? throw new ArgumentNullException(nameof(candidates));
        }

        public void Initialize(int width, int height, int framerate, int bitrate)
        {
            _activeEncoder?.Dispose();
            _activeEncoder = null;

            foreach (var (name, create) in _candidates)
            {
                IVideoEncoder? encoder = null;
                try
                {
                    encoder = create();
                    encoder.Initialize(width, height, framerate, bitrate);
                    _activeEncoder = encoder;
                    LogHelper.Info($"编码器回退链选择成功: {name}");
                    return;
                }
                catch (Exception ex)
                {
                    try
                    {
                        encoder?.Dispose();
                    }
                    catch
                    {
                    }

                    LogHelper.Warning($"编码器初始化失败({name})，回退尝试下一个: {ex.GetBaseException().Message}");
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
    }
}

