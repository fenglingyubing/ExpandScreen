using ExpandScreen.Core.Encode;
using ExpandScreen.Core.Capture;

namespace ExpandScreen.Tests
{
    /// <summary>
    /// 视频编码器单元测试
    /// </summary>
    public class VideoEncoderTests
    {
        /// <summary>
        /// 测试FFmpeg编码器初始化
        /// </summary>
        [Fact]
        public void TestFFmpegEncoder_Initialize()
        {
            // Arrange
            var encoder = new FFmpegEncoder();

            // Act
            encoder.Initialize(1920, 1080, 60, 5_000_000);

            // Assert - 如果没有异常则通过
            encoder.Dispose();
        }

        /// <summary>
        /// 测试编码单帧
        /// </summary>
        [Fact]
        public void TestFFmpegEncoder_EncodeSingleFrame()
        {
            // Arrange
            var encoder = new FFmpegEncoder();
            encoder.Initialize(1920, 1080, 60, 5_000_000);

            // 创建测试帧数据（BGRA格式）
            var width = 1920;
            var height = 1080;
            var stride = width * 4;
            var frameData = new byte[stride * height];

            // 填充测试数据（红色）
            for (int i = 0; i < frameData.Length; i += 4)
            {
                frameData[i] = 0;     // B
                frameData[i + 1] = 0; // G
                frameData[i + 2] = 255; // R
                frameData[i + 3] = 255; // A
            }

            // Act
            var encodedData = encoder.Encode(frameData);

            // Assert
            Assert.NotNull(encodedData);
            Assert.True(encodedData.Length > 0);

            encoder.Dispose();
        }

        /// <summary>
        /// 测试编码多帧
        /// </summary>
        [Fact]
        public void TestFFmpegEncoder_EncodeMultipleFrames()
        {
            // Arrange
            var encoder = new FFmpegEncoder();
            encoder.Initialize(1920, 1080, 60, 5_000_000);

            var width = 1920;
            var height = 1080;
            var stride = width * 4;
            var frameData = new byte[stride * height];

            // Act & Assert
            for (int i = 0; i < 100; i++)
            {
                var encodedData = encoder.Encode(frameData);
                Assert.NotNull(encodedData);
            }

            encoder.Dispose();
        }

        /// <summary>
        /// 测试编码器工厂
        /// </summary>
        [Fact]
        public void TestVideoEncoderFactory_CreateEncoder()
        {
            // Act
            var encoder = VideoEncoderFactory.CreateEncoder(EncoderType.FFmpeg);

            // Assert
            Assert.NotNull(encoder);
            Assert.IsType<FFmpegEncoder>(encoder);

            encoder.Dispose();
        }

        /// <summary>
        /// 测试编码服务
        /// </summary>
        [Fact]
        public async Task TestVideoEncodingService()
        {
            // Arrange
            var config = VideoEncoderConfig.CreateLowLatency(1920, 1080, 60);
            var encoder = new FFmpegEncoder(config);
            encoder.Initialize(1920, 1080, 60, 5_000_000);

            var service = new VideoEncodingService(encoder);
            service.Start();

            // Act
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

            // 等待编码完成
            await Task.Delay(2000);

            // Assert
            Assert.True(service.TotalFramesEncoded > 0);

            // Cleanup
            await service.StopAsync();
            service.Dispose();
        }

        /// <summary>
        /// 测试编码性能
        /// </summary>
        [Fact]
        public void TestEncodingPerformance()
        {
            // Arrange
            var config = VideoEncoderConfig.CreateLowLatency(1920, 1080, 60);
            var encoder = new FFmpegEncoder(config);
            encoder.Initialize(1920, 1080, 60, 5_000_000);

            var width = 1920;
            var height = 1080;
            var stride = width * 4;
            var frameData = new byte[stride * height];

            // Act
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            for (int i = 0; i < 100; i++)
            {
                encoder.Encode(frameData);
            }

            stopwatch.Stop();

            // Assert
            var avgTimePerFrame = stopwatch.ElapsedMilliseconds / 100.0;
            var theoreticalFps = 1000.0 / avgTimePerFrame;

            Assert.True(avgTimePerFrame < 50, $"平均编码时间过长: {avgTimePerFrame}ms");
            Assert.True(theoreticalFps >= 30, $"理论FPS过低: {theoreticalFps}");

            Console.WriteLine($"编码性能: 平均{avgTimePerFrame:F2}ms/帧, 理论FPS:{theoreticalFps:F1}");

            encoder.Dispose();
        }
    }
}
