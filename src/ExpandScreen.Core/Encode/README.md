# 视频编码模块使用说明

## 概述

视频编码模块负责将屏幕捕获的BGRA格式帧数据编码为H.264视频流,支持低延迟编码和高性能传输。

## 核心组件

### 1. IVideoEncoder 接口
视频编码器的统一接口,定义了编码器的基本操作。

### 2. FFmpegEncoder 类
基于FFmpeg库的H.264编码器实现,支持:
- H.264编码
- BGRA → YUV420P像素格式转换
- 低延迟配置(ultrafast preset, zerolatency tune)
- 可配置的码率、帧率、分辨率
- 线程安全

### 3. VideoEncoderConfig 类
编码器配置类,包含:
- 分辨率配置
- 帧率配置
- 码率配置
- 编码预设(preset)
- 编码调优(tune)
- Profile配置

### 4. VideoEncoderFactory 类
编码器工厂,支持:
- 自动选择最优编码器
- 为未来硬件编码(NVENC/QuickSync)做准备
- 根据系统推荐配置

### 5. VideoEncodingService 类
编码服务,提供:
- 异步编码队列管理
- 独立编码线程
- 性能统计
- 自动帧丢弃(队列满时)

### 6. EncodedFrame 类
编码后的帧数据结构,包含:
- 编码数据
- 帧序列号
- 时间戳
- 帧类型(I/P/B)
- 编码耗时统计

## 使用示例

### 基础使用

```csharp
// 1. 创建编码器配置
var config = VideoEncoderConfig.CreateLowLatency(1920, 1080, 60);

// 2. 创建编码器
var encoder = new FFmpegEncoder(config);

// 3. 初始化编码器
encoder.Initialize(1920, 1080, 60, 5_000_000);

// 4. 编码帧
byte[] frameData = ...; // BGRA格式的帧数据
byte[] encodedData = encoder.Encode(frameData);

// 5. 释放资源
encoder.Dispose();
```

### 使用编码器工厂

```csharp
// 自动选择最优编码器
var config = VideoEncoderFactory.GetRecommendedConfig(1920, 1080, 60);
var encoder = VideoEncoderFactory.CreateEncoder(EncoderType.Auto, config);

encoder.Initialize(1920, 1080, 60, config.Bitrate);

// 使用编码器...

encoder.Dispose();
```

### 使用编码服务

```csharp
// 1. 创建编码器
var config = VideoEncoderConfig.CreateLowLatency(1920, 1080, 60);
var encoder = new FFmpegEncoder(config);
encoder.Initialize(1920, 1080, 60, 5_000_000);

// 2. 创建编码服务
var encodingService = new VideoEncodingService(encoder);

// 3. 启动服务
encodingService.Start();

// 4. 添加帧到队列
var capturedFrame = new CapturedFrame(1920, 1080, 1920 * 4);
await encodingService.EnqueueFrameAsync(capturedFrame);

// 5. 获取编码后的帧
var encodedFrame = await encodingService.DequeueEncodedFrameAsync();

// 6. 停止服务
await encodingService.StopAsync();
encodingService.Dispose();
```

### 完整工作流

```csharp
// 创建捕获服务
var captureService = new ScreenCaptureService(...);

// 创建编码服务
var config = VideoEncoderFactory.GetRecommendedConfig(1920, 1080, 60);
var encoder = VideoEncoderFactory.CreateEncoder(EncoderType.Auto, config);
encoder.Initialize(1920, 1080, 60, config.Bitrate);
var encodingService = new VideoEncodingService(encoder);

// 启动服务
encodingService.Start();

// 捕获 -> 编码流程
captureService.OnFrameCaptured += async (frame) =>
{
    await encodingService.EnqueueFrameAsync(frame);
};

// 编码 -> 发送流程
Task.Run(async () =>
{
    while (true)
    {
        var encodedFrame = await encodingService.DequeueEncodedFrameAsync();
        if (encodedFrame != null)
        {
            // 发送编码数据到网络
            await networkSender.SendAsync(encodedFrame.Data);
            encodedFrame.Dispose();
        }
    }
});

// 清理
await encodingService.StopAsync();
encodingService.Dispose();
```

## 性能优化建议

### 1. 选择合适的配置

**低延迟场景**:
```csharp
var config = VideoEncoderConfig.CreateLowLatency(1920, 1080, 60);
config.Preset = "ultrafast";
config.Tune = "zerolatency";
config.MaxBFrames = 0;
```

**高质量场景**:
```csharp
var config = VideoEncoderConfig.CreateHighQuality(1920, 1080, 60);
config.Preset = "medium";
config.Bitrate = 10_000_000;
```

### 2. 调整队列大小

```csharp
var service = new VideoEncodingService(encoder)
{
    InputQueueCapacity = 5,   // 减少输入队列延迟
    OutputQueueCapacity = 20  // 增加输出缓冲
};
```

### 3. 监控性能

```csharp
// 定期输出性能报告
Timer performanceTimer = new Timer(_ =>
{
    Console.WriteLine(encodingService.GetPerformanceReport());
}, null, 0, 5000);
```

## FFmpeg库依赖

### Windows平台

1. 下载FFmpeg动态链接库:
   - avcodec-59.dll
   - avformat-59.dll
   - avutil-57.dll
   - swscale-6.dll
   - swresample-4.dll

2. 将DLL文件放到应用程序目录或设置FFmpeg.AutoGen的RootPath

### 设置库路径

```csharp
// 在程序启动时设置
FFmpeg.AutoGen.ffmpeg.RootPath = @"C:\ffmpeg\bin";
```

## 故障排查

### 常见问题

**1. 找不到FFmpeg库**
```
错误: DllNotFoundException: Unable to load DLL 'avcodec-59'
解决: 确保FFmpeg DLL在正确的路径下
```

**2. 编码失败**
```
检查日志输出,确认:
- 帧数据格式正确(BGRA)
- 分辨率匹配
- 内存充足
```

**3. 性能不佳**
```
优化建议:
- 使用ultrafast preset
- 启用硬件编码(未来)
- 降低分辨率或帧率
- 减少码率
```

## 性能指标

### 目标性能
- 1080p@60fps编码延迟: < 20ms
- CPU占用: < 30% (单核)
- 内存占用: < 100MB
- 编码质量: 可接受的视觉质量

### 实测性能(参考)
- Intel i7-10700K + FFmpeg软件编码:
  - 1080p@60fps: ~15ms/帧
  - 4K@60fps: ~60ms/帧

## 未来改进

1. **硬件编码支持**
   - NVIDIA NVENC (计划中)
   - Intel QuickSync (计划中)

2. **编码优化**
   - 多线程编码
   - GPU加速像素格式转换
   - 自适应码率控制

3. **功能增强**
   - 支持更多编码格式(H.265, VP9)
   - ROI编码
   - 视觉质量优化

## 相关文档

- [屏幕捕获模块](../Capture/README.md)
- [网络传输模块](../../Services/README.md)
- [FFmpeg.AutoGen文档](https://github.com/Ruslan-B/FFmpeg.AutoGen)
