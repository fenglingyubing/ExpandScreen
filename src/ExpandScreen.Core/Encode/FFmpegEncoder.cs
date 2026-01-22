using FFmpeg.AutoGen;
using System.Runtime.InteropServices;
using ExpandScreen.Utils;

namespace ExpandScreen.Core.Encode
{
    /// <summary>
    /// 基于FFmpeg的H.264视频编码器
    /// </summary>
    public unsafe class FFmpegEncoder : IVideoEncoder
    {
        private AVCodec* _codec;
        private AVCodecContext* _codecContext;
        private AVFrame* _frame;
        private AVPacket* _packet;
        private SwsContext* _swsContext;

        private VideoEncoderConfig _config;
        private bool _initialized = false;
        private long _frameCounter = 0;
        private readonly object _lock = new object();

        static FFmpegEncoder()
        {
            // 设置FFmpeg库路径（需要根据实际情况调整）
            try
            {
                // Windows平台自动加载DLL
                ffmpeg.RootPath = AppDomain.CurrentDomain.BaseDirectory;
            }
            catch (Exception ex)
            {
                LogHelper.Error($"FFmpeg初始化失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        public FFmpegEncoder()
        {
            _config = VideoEncoderConfig.CreateDefault();
        }

        /// <summary>
        /// 构造函数（带配置）
        /// </summary>
        public FFmpegEncoder(VideoEncoderConfig config)
        {
            _config = config ?? VideoEncoderConfig.CreateDefault();
        }

        /// <summary>
        /// 初始化编码器
        /// </summary>
        public void Initialize(int width, int height, int framerate, int bitrate)
        {
            lock (_lock)
            {
                if (_initialized)
                {
                    LogHelper.Warning("编码器已初始化，先释放再重新初始化");
                    Dispose();
                }

                try
                {
                    // 更新配置
                    _config.Width = width;
                    _config.Height = height;
                    _config.Framerate = framerate;
                    _config.Bitrate = bitrate;

                    LogHelper.Info($"初始化FFmpeg编码器: {width}x{height}@{framerate}fps, {bitrate}bps");

                    // 查找H.264编码器
                    _codec = ffmpeg.avcodec_find_encoder(AVCodecID.AV_CODEC_ID_H264);
                    if (_codec == null)
                    {
                        throw new Exception("未找到H.264编码器");
                    }

                    // 创建编码器上下文
                    _codecContext = ffmpeg.avcodec_alloc_context3(_codec);
                    if (_codecContext == null)
                    {
                        throw new Exception("无法分配编码器上下文");
                    }

                    // 配置编码器参数
                    _codecContext->width = width;
                    _codecContext->height = height;
                    _codecContext->time_base = new AVRational { num = 1, den = framerate };
                    _codecContext->framerate = new AVRational { num = framerate, den = 1 };
                    _codecContext->bit_rate = bitrate;
                    _codecContext->gop_size = _config.KeyFrameInterval;
                    _codecContext->max_b_frames = _config.MaxBFrames;
                    _codecContext->pix_fmt = AVPixelFormat.AV_PIX_FMT_YUV420P;

                    // 线程配置
                    if (_config.ThreadCount > 0)
                    {
                        _codecContext->thread_count = _config.ThreadCount;
                    }

                    // 设置编码参数
                    ffmpeg.av_opt_set(_codecContext->priv_data, "preset", _config.Preset, 0);
                    ffmpeg.av_opt_set(_codecContext->priv_data, "tune", _config.Tune, 0);
                    ffmpeg.av_opt_set(_codecContext->priv_data, "profile", _config.Profile, 0);

                    // 打开编码器
                    int ret = ffmpeg.avcodec_open2(_codecContext, _codec, null);
                    if (ret < 0)
                    {
                        throw new Exception($"无法打开编码器: {GetFFmpegError(ret)}");
                    }

                    // 分配帧
                    _frame = ffmpeg.av_frame_alloc();
                    if (_frame == null)
                    {
                        throw new Exception("无法分配帧");
                    }

                    _frame->format = (int)_codecContext->pix_fmt;
                    _frame->width = width;
                    _frame->height = height;

                    ret = ffmpeg.av_frame_get_buffer(_frame, 0);
                    if (ret < 0)
                    {
                        throw new Exception($"无法分配帧缓冲区: {GetFFmpegError(ret)}");
                    }

                    // 分配数据包
                    _packet = ffmpeg.av_packet_alloc();
                    if (_packet == null)
                    {
                        throw new Exception("无法分配数据包");
                    }

                    // 初始化像素格式转换上下文（BGRA -> YUV420P）
                    _swsContext = ffmpeg.sws_getContext(
                        width, height, AVPixelFormat.AV_PIX_FMT_BGRA,
                        width, height, AVPixelFormat.AV_PIX_FMT_YUV420P,
                        ffmpeg.SWS_FAST_BILINEAR, null, null, null);

                    if (_swsContext == null)
                    {
                        throw new Exception("无法创建像素格式转换上下文");
                    }

                    _initialized = true;
                    LogHelper.Info("FFmpeg编码器初始化成功");
                }
                catch (Exception ex)
                {
                    LogHelper.Error($"FFmpeg编码器初始化失败: {ex.Message}");
                    Dispose();
                    throw;
                }
            }
        }

        /// <summary>
        /// 编码一帧（从BGRA格式）
        /// </summary>
        public byte[]? Encode(byte[] frameData)
        {
            if (!_initialized)
            {
                throw new InvalidOperationException("编码器未初始化");
            }

            lock (_lock)
            {
                try
                {
                    var startTime = DateTime.UtcNow;

                    // 确保帧可写
                    int ret = ffmpeg.av_frame_make_writable(_frame);
                    if (ret < 0)
                    {
                        LogHelper.Error($"帧不可写: {GetFFmpegError(ret)}");
                        return null;
                    }

                    // 像素格式转换（BGRA -> YUV420P）
                    fixed (byte* srcPtr = frameData)
                    {
                        byte*[] srcData = new byte*[] { srcPtr, null, null, null };
                        int[] srcLinesize = new int[] { _config.Width * 4, 0, 0, 0 };

                        ffmpeg.sws_scale(
                            _swsContext,
                            srcData,
                            srcLinesize,
                            0,
                            _config.Height,
                            _frame->data,
                            _frame->linesize);
                    }

                    // 设置帧时间戳
                    _frame->pts = _frameCounter++;

                    // 发送帧到编码器
                    ret = ffmpeg.avcodec_send_frame(_codecContext, _frame);
                    if (ret < 0)
                    {
                        LogHelper.Error($"发送帧失败: {GetFFmpegError(ret)}");
                        return null;
                    }

                    // 接收编码后的数据包
                    ret = ffmpeg.avcodec_receive_packet(_codecContext, _packet);
                    if (ret == ffmpeg.AVERROR(ffmpeg.EAGAIN) || ret == ffmpeg.AVERROR_EOF)
                    {
                        // 需要更多数据或已结束
                        return null;
                    }
                    else if (ret < 0)
                    {
                        LogHelper.Error($"接收数据包失败: {GetFFmpegError(ret)}");
                        return null;
                    }

                    // 复制编码数据
                    byte[] encodedData = new byte[_packet->size];
                    Marshal.Copy((IntPtr)_packet->data, encodedData, 0, _packet->size);

                    // 计算编码耗时
                    var encodeTime = (DateTime.UtcNow - startTime).TotalMilliseconds;

                    // 释放数据包
                    ffmpeg.av_packet_unref(_packet);

                    // 每100帧记录一次性能
                    if (_frameCounter % 100 == 0)
                    {
                        LogHelper.Debug($"编码性能: 帧#{_frameCounter}, 耗时:{encodeTime:F2}ms, 大小:{encodedData.Length}bytes");
                    }

                    return encodedData;
                }
                catch (Exception ex)
                {
                    LogHelper.Error($"编码帧失败: {ex.Message}");
                    return null;
                }
            }
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            lock (_lock)
            {
                if (!_initialized)
                {
                    return;
                }

                try
                {
                    // 刷新编码器
                    if (_codecContext != null)
                    {
                        ffmpeg.avcodec_send_frame(_codecContext, null);
                    }

                    // 释放资源
                    if (_swsContext != null)
                    {
                        ffmpeg.sws_freeContext(_swsContext);
                        _swsContext = null;
                    }

                    if (_packet != null)
                    {
                        fixed (AVPacket** packetPtr = &_packet)
                        {
                            ffmpeg.av_packet_free(packetPtr);
                        }
                        _packet = null;
                    }

                    if (_frame != null)
                    {
                        fixed (AVFrame** framePtr = &_frame)
                        {
                            ffmpeg.av_frame_free(framePtr);
                        }
                        _frame = null;
                    }

                    if (_codecContext != null)
                    {
                        fixed (AVCodecContext** contextPtr = &_codecContext)
                        {
                            ffmpeg.avcodec_free_context(contextPtr);
                        }
                        _codecContext = null;
                    }

                    _initialized = false;
                    _frameCounter = 0;

                    LogHelper.Info("FFmpeg编码器已释放");
                }
                catch (Exception ex)
                {
                    LogHelper.Error($"释放FFmpeg编码器失败: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 获取FFmpeg错误描述
        /// </summary>
        private string GetFFmpegError(int error)
        {
            byte[] buffer = new byte[1024];
            ffmpeg.av_strerror(error, buffer, (ulong)buffer.Length);
            return System.Text.Encoding.UTF8.GetString(buffer).TrimEnd('\0');
        }
    }
}
