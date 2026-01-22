using ExpandScreen.Core.Capture;
using ExpandScreen.Core.Encode;
using Xunit;

namespace ExpandScreen.IntegrationTests
{
    /// <summary>
    /// 视频编码器测试（依赖FFmpeg原生库，默认在CI/Linux环境跳过）
    /// </summary>
    public class VideoEncoderTests
    {
        private const string RequiresFfmpegSkipReason = "Requires FFmpeg native binaries available on the runner (typically Windows + ffmpeg dlls).";

        [Fact(Skip = RequiresFfmpegSkipReason)]
        public void TestFFmpegEncoder_Initialize()
        {
            var encoder = new FFmpegEncoder();
            encoder.Initialize(1920, 1080, 60, 5_000_000);
            encoder.Dispose();
        }

        [Fact(Skip = RequiresFfmpegSkipReason)]
        public void TestFFmpegEncoder_EncodeSingleFrame()
        {
            var encoder = new FFmpegEncoder();
            encoder.Initialize(1920, 1080, 60, 5_000_000);

            var width = 1920;
            var height = 1080;
            var stride = width * 4;
            var frameData = new byte[stride * height];

            for (int i = 0; i < frameData.Length; i += 4)
            {
                frameData[i] = 0; // B
                frameData[i + 1] = 0; // G
                frameData[i + 2] = 255; // R
                frameData[i + 3] = 255; // A
            }

            var encodedData = encoder.Encode(frameData);

            Assert.NotNull(encodedData);
            Assert.True(encodedData.Length > 0);

            encoder.Dispose();
        }

        [Fact(Skip = RequiresFfmpegSkipReason)]
        public void TestFFmpegEncoder_EncodeMultipleFrames()
        {
            var encoder = new FFmpegEncoder();
            encoder.Initialize(1920, 1080, 60, 5_000_000);

            var width = 1920;
            var height = 1080;
            var stride = width * 4;
            var frameData = new byte[stride * height];

            for (int i = 0; i < 100; i++)
            {
                var encodedData = encoder.Encode(frameData);
                Assert.NotNull(encodedData);
            }

            encoder.Dispose();
        }

        [Fact(Skip = RequiresFfmpegSkipReason)]
        public void TestVideoEncoderFactory_CreateEncoder()
        {
            var encoder = VideoEncoderFactory.CreateEncoder(EncoderType.FFmpeg);

            Assert.NotNull(encoder);
            Assert.IsType<FFmpegEncoder>(encoder);

            encoder.Dispose();
        }

        [Fact(Skip = RequiresFfmpegSkipReason)]
        public async Task TestVideoEncodingService()
        {
            var config = VideoEncoderConfig.CreateLowLatency(1920, 1080, 60);
            var encoder = new FFmpegEncoder(config);
            encoder.Initialize(1920, 1080, 60, 5_000_000);

            var service = new VideoEncodingService(encoder);
            service.Start();

            var width = 1920;
            var height = 1080;
            var stride = width * 4;

            for (int i = 0; i < 10; i++)
            {
                var frame = new CapturedFrame(width, height, stride)
                {
                    FrameNumber = i
                };
                await service.EnqueueFrameAsync(frame);
            }

            await Task.Delay(2000);

            Assert.True(service.TotalFramesEncoded > 0);

            await service.StopAsync();
            service.Dispose();
        }

        [Fact(Skip = RequiresFfmpegSkipReason)]
        public void TestEncodingPerformance()
        {
            var config = VideoEncoderConfig.CreateLowLatency(1920, 1080, 60);
            var encoder = new FFmpegEncoder(config);
            encoder.Initialize(1920, 1080, 60, 5_000_000);

            var width = 1920;
            var height = 1080;
            var stride = width * 4;
            var frameData = new byte[stride * height];

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            for (int i = 0; i < 100; i++)
            {
                encoder.Encode(frameData);
            }

            stopwatch.Stop();

            var avgTimePerFrame = stopwatch.ElapsedMilliseconds / 100.0;
            var theoreticalFps = 1000.0 / avgTimePerFrame;

            Assert.True(avgTimePerFrame < 50, $"平均编码时间过长: {avgTimePerFrame}ms");
            Assert.True(theoreticalFps >= 30, $"理论FPS过低: {theoreticalFps}");

            encoder.Dispose();
        }
    }
}

