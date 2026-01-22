using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using ExpandScreen.Core.Capture;
using ExpandScreen.Core.Encode;
using ExpandScreen.Protocol.Messages;
using ExpandScreen.Protocol.Network;
using ExpandScreen.Services.Connection;
using ExpandScreen.Utils;

namespace ExpandScreen.IntegrationTests
{
    /// <summary>
    /// 端到端集成测试
    /// 测试任务2.1.8: 集成所有模块
    /// - 虚拟显示驱动 ↔ 应用程序
    /// - 屏幕捕获 → 视频编码 → 网络发送
    /// - USB连接 → 网络通信
    /// </summary>
    public class EndToEndIntegrationTests
    {
        private const string RequiresFfmpegSkipReason =
            "Requires FFmpeg native binaries available on the runner (typically Windows + ffmpeg dlls).";

        /// <summary>
        /// 测试1: 捕获 → 编码 → 网络发送管道
        /// </summary>
        [Fact(Skip = RequiresFfmpegSkipReason)]
        public async Task Test_CaptureEncodeNetworkPipeline()
        {
            LogHelper.Info("=== 测试: 捕获→编码→网络发送管道 ===");

            // 创建网络连接对
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;

            TcpClient? serverTcp = null;
            TcpClient? clientTcp = null;
            int receivedFrames = 0;

            try
            {
                // 建立连接
                var connectTask = Task.Run(async () =>
                {
                    var client = new TcpClient();
                    await client.ConnectAsync(IPAddress.Loopback, port);
                    return client;
                });

                serverTcp = await listener.AcceptTcpClientAsync();
                clientTcp = await connectTask;

                // 创建网络会话
                var sender = new NetworkSender(clientTcp.GetStream());
                var receiver = new NetworkReceiver(serverTcp.GetStream());

                // 接收端监听
                var receiveEvent = new TaskCompletionSource<bool>();
                receiver.MessageReceived += (s, e) =>
                {
                    if (e.Header.Type == MessageType.VideoFrame)
                    {
                        receivedFrames++;
                        LogHelper.Info($"接收到视频帧: #{receivedFrames}, 大小: {e.Payload.Length} 字节");

                        if (receivedFrames >= 5)
                        {
                            receiveEvent.TrySetResult(true);
                        }
                    }
                };

                // 创建编码器
                var config = VideoEncoderConfig.CreateLowLatency(1920, 1080, 60);
                var encoder = new FFmpegEncoder(config);
                encoder.Initialize(1920, 1080, 60, 5_000_000);

                // 模拟捕获和编码
                LogHelper.Info("开始模拟捕获和编码帧...");
                var width = 1920;
                var height = 1080;
                var stride = width * 4;
                var frameData = new byte[stride * height];

                // 生成测试帧数据
                for (int i = 0; i < frameData.Length; i += 4)
                {
                    frameData[i] = (byte)(i % 256);     // B
                    frameData[i + 1] = (byte)((i * 2) % 256); // G
                    frameData[i + 2] = (byte)((i * 3) % 256); // R
                    frameData[i + 3] = 255; // A
                }

                // 编码并发送多帧
                for (int i = 0; i < 5; i++)
                {
                    LogHelper.Info($"编码第 {i + 1} 帧...");
                    var encodedData = encoder.Encode(frameData);

                    // 发送编码后的帧
                    await sender.SendMessageAsync(MessageType.VideoFrame, encodedData);
                    LogHelper.Info($"发送第 {i + 1} 帧, 大小: {encodedData.Length} 字节");

                    await Task.Delay(16); // 模拟60fps
                }

                // 等待接收完成
                var timeout = Task.Delay(5000);
                var completed = await Task.WhenAny(receiveEvent.Task, timeout);

                // 断言
                Assert.True(completed == receiveEvent.Task, "接收超时");
                Assert.Equal(5, receivedFrames);

                LogHelper.Info($"✅ 成功接收所有 {receivedFrames} 帧");

                // 清理
                encoder.Dispose();
                sender.Dispose();
                receiver.Dispose();
            }
            finally
            {
                serverTcp?.Close();
                clientTcp?.Close();
                listener.Stop();
            }
        }

        /// <summary>
        /// 测试2: 端到端性能测试
        /// </summary>
        [Fact(Skip = RequiresFfmpegSkipReason)]
        public async Task Test_EndToEndPerformance()
        {
            LogHelper.Info("=== 测试: 端到端性能分析 ===");

            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;

            TcpClient? serverTcp = null;
            TcpClient? clientTcp = null;

            try
            {
                // 建立连接
                var connectTask = Task.Run(async () =>
                {
                    var client = new TcpClient();
                    await client.ConnectAsync(IPAddress.Loopback, port);
                    return client;
                });

                serverTcp = await listener.AcceptTcpClientAsync();
                clientTcp = await connectTask;

                var sender = new NetworkSender(clientTcp.GetStream());
                var receiver = new NetworkReceiver(serverTcp.GetStream());

                // 性能计数器
                int receivedFrames = 0;
                var receiveEvent = new TaskCompletionSource<bool>();
                var receiveStopwatch = new Stopwatch();
                var firstFrameTime = 0L;
                var lastFrameTime = 0L;

                receiver.MessageReceived += (s, e) =>
                {
                    if (e.Header.Type == MessageType.VideoFrame)
                    {
                        if (receivedFrames == 0)
                        {
                            receiveStopwatch.Start();
                            firstFrameTime = receiveStopwatch.ElapsedMilliseconds;
                        }

                        receivedFrames++;
                        lastFrameTime = receiveStopwatch.ElapsedMilliseconds;

                        if (receivedFrames >= 60)
                        {
                            receiveStopwatch.Stop();
                            receiveEvent.TrySetResult(true);
                        }
                    }
                };

                // 编码器配置
                var config = VideoEncoderConfig.CreateLowLatency(1920, 1080, 60);
                var encoder = new FFmpegEncoder(config);
                encoder.Initialize(1920, 1080, 60, 5_000_000);

                // 准备测试数据
                var width = 1920;
                var height = 1080;
                var stride = width * 4;
                var frameData = new byte[stride * height];

                LogHelper.Info("开始性能测试: 编码并发送60帧...");
                var sendStopwatch = Stopwatch.StartNew();

                // 编码和发送计时
                var encodeTimes = new List<long>();
                var sendTimes = new List<long>();

                for (int i = 0; i < 60; i++)
                {
                    // 编码计时
                    var encodeTimer = Stopwatch.StartNew();
                    var encodedData = encoder.Encode(frameData);
                    encodeTimer.Stop();
                    encodeTimes.Add(encodeTimer.ElapsedMilliseconds);

                    // 发送计时
                    var sendTimer = Stopwatch.StartNew();
                    await sender.SendMessageAsync(MessageType.VideoFrame, encodedData);
                    sendTimer.Stop();
                    sendTimes.Add(sendTimer.ElapsedMilliseconds);

                    await Task.Delay(16); // 60fps
                }

                sendStopwatch.Stop();

                // 等待接收完成
                var timeout = Task.Delay(5000);
                var completed = await Task.WhenAny(receiveEvent.Task, timeout);

                Assert.True(completed == receiveEvent.Task, "接收超时");

                // 性能统计
                var totalSendTime = sendStopwatch.ElapsedMilliseconds;
                var totalReceiveTime = lastFrameTime - firstFrameTime;
                var avgEncodeTime = encodeTimes.Average();
                var maxEncodeTime = encodeTimes.Max();
                var avgSendTime = sendTimes.Average();
                var actualFps = 60000.0 / totalReceiveTime;

                LogHelper.Info("=== 性能统计结果 ===");
                LogHelper.Info($"编码性能:");
                LogHelper.Info($"  - 平均编码时间: {avgEncodeTime:F2}ms");
                LogHelper.Info($"  - 最大编码时间: {maxEncodeTime}ms");
                LogHelper.Info($"网络传输:");
                LogHelper.Info($"  - 平均发送时间: {avgSendTime:F2}ms");
                LogHelper.Info($"  - 总发送时间: {totalSendTime}ms");
                LogHelper.Info($"  - 总接收时间: {totalReceiveTime}ms");
                LogHelper.Info($"端到端性能:");
                LogHelper.Info($"  - 实际FPS: {actualFps:F1}");
                LogHelper.Info($"  - 端到端延迟: {(totalReceiveTime - totalSendTime):F0}ms");

                // 性能断言
                Assert.True(avgEncodeTime < 50, $"编码时间过长: {avgEncodeTime}ms");
                Assert.True(actualFps >= 30, $"FPS过低: {actualFps:F1}");

                LogHelper.Info("✅ 性能测试通过");

                // 清理
                encoder.Dispose();
                sender.Dispose();
                receiver.Dispose();
            }
            finally
            {
                serverTcp?.Close();
                clientTcp?.Close();
                listener.Stop();
            }
        }

        /// <summary>
        /// 测试3: 握手协议集成测试
        /// </summary>
        [Fact]
        public async Task Test_HandshakeIntegration()
        {
            LogHelper.Info("=== 测试: 握手协议集成 ===");

            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;

            TcpClient? serverTcp = null;
            TcpClient? clientTcp = null;

            try
            {
                // 建立连接
                var connectTask = Task.Run(async () =>
                {
                    var client = new TcpClient();
                    await client.ConnectAsync(IPAddress.Loopback, port);
                    return client;
                });

                serverTcp = await listener.AcceptTcpClientAsync();
                clientTcp = await connectTask;

                // 创建会话
                var clientSession = new NetworkSession(clientTcp.GetStream());
                var serverSession = new NetworkSession(serverTcp.GetStream());

                // 服务器端处理握手
                var serverHandshakeReceived = new TaskCompletionSource<HandshakeMessage>();
                serverSession.MessageReceived += async (s, e) =>
                {
                    if (e.Header.Type == MessageType.Handshake)
                    {
                        LogHelper.Info("服务器收到握手请求");
                        var handshake = MessageSerializer.DeserializeJsonPayload<HandshakeMessage>(e.Payload);
                        if (handshake != null)
                        {
                            serverHandshakeReceived.TrySetResult(handshake);
                            await serverSession.RespondToHandshakeAsync(handshake, true);
                            LogHelper.Info("服务器响应握手");
                        }
                    }
                };

                // 客户端发起握手
                var clientHandshake = new HandshakeMessage
                {
                    DeviceId = "android-test-001",
                    DeviceName = "Test Android Device",
                    ClientVersion = "1.0.0",
                    ScreenWidth = 2560,
                    ScreenHeight = 1600
                };

                LogHelper.Info("客户端发起握手...");
                var handshakeResult = await clientSession.PerformHandshakeAsync(clientHandshake);

                // 断言
                Assert.True(handshakeResult, "握手失败");
                Assert.True(clientSession.IsHandshakeCompleted);
                Assert.NotNull(clientSession.SessionId);

                LogHelper.Info($"✅ 握手成功, SessionId: {clientSession.SessionId}");

                // 清理
                clientSession.Dispose();
                serverSession.Dispose();
            }
            finally
            {
                serverTcp?.Close();
                clientTcp?.Close();
                listener.Stop();
            }
        }

        /// <summary>
        /// 测试4: 心跳机制集成测试
        /// </summary>
        [Fact]
        public async Task Test_HeartbeatIntegration()
        {
            LogHelper.Info("=== 测试: 心跳机制集成 ===");

            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;

            TcpClient? serverTcp = null;
            TcpClient? clientTcp = null;

            try
            {
                // 建立连接
                var connectTask = Task.Run(async () =>
                {
                    var client = new TcpClient();
                    await client.ConnectAsync(IPAddress.Loopback, port);
                    return client;
                });

                serverTcp = await listener.AcceptTcpClientAsync();
                clientTcp = await connectTask;

                var clientSession = new NetworkSession(clientTcp.GetStream(), heartbeatIntervalMs: 1000, heartbeatTimeoutMs: 5000);
                var serverSession = new NetworkSession(serverTcp.GetStream(), heartbeatIntervalMs: 1000, heartbeatTimeoutMs: 5000);

                // 服务器端处理握手（握手完成后才会启动心跳发送）
                serverSession.MessageReceived += async (s, e) =>
                {
                    if (e.Header.Type == MessageType.Handshake)
                    {
                        var handshake = MessageSerializer.DeserializeJsonPayload<HandshakeMessage>(e.Payload);
                        if (handshake != null)
                        {
                            await serverSession.RespondToHandshakeAsync(handshake, true);
                        }
                    }
                };

                var clientHandshake = new HandshakeMessage
                {
                    DeviceId = "android-test-001",
                    DeviceName = "Test Android Device",
                    ClientVersion = "1.0.0",
                    ScreenWidth = 2560,
                    ScreenHeight = 1600
                };

                Assert.True(await clientSession.PerformHandshakeAsync(clientHandshake), "握手失败");

                LogHelper.Info("心跳已启动（握手完成后自动发送），等待3秒...");
                await Task.Delay(3500);

                var clientStats = clientSession.GetStatistics();
                var serverStats = serverSession.GetStatistics();

                Assert.True(clientStats.TimeSinceLastHeartbeat < 3000, $"客户端心跳超时过长: {clientStats.TimeSinceLastHeartbeat}ms");
                Assert.True(serverStats.TimeSinceLastHeartbeat < 3000, $"服务器心跳超时过长: {serverStats.TimeSinceLastHeartbeat}ms");

                LogHelper.Info($"✅ 心跳测试通过 (客户端: {clientStats.TimeSinceLastHeartbeat:F0}ms, 服务器: {serverStats.TimeSinceLastHeartbeat:F0}ms)");

                clientSession.Dispose();
                serverSession.Dispose();
            }
            finally
            {
                serverTcp?.Close();
                clientTcp?.Close();
                listener.Stop();
            }
        }

        /// <summary>
        /// 测试5: 内存和资源泄漏测试
        /// </summary>
        [Fact(Skip = RequiresFfmpegSkipReason)]
        public async Task Test_MemoryAndResourceLeaks()
        {
            LogHelper.Info("=== 测试: 内存和资源泄漏 ===");

            var initialMemory = GC.GetTotalMemory(true);
            LogHelper.Info($"初始内存: {initialMemory / 1024 / 1024}MB");

            // 创建和销毁多次
            for (int iteration = 0; iteration < 10; iteration++)
            {
                var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;

                var connectTask = Task.Run(async () =>
                {
                    var client = new TcpClient();
                    await client.ConnectAsync(IPAddress.Loopback, port);
                    return client;
                });

                var serverTcp = await listener.AcceptTcpClientAsync();
                var clientTcp = await connectTask;

                var sender = new NetworkSender(clientTcp.GetStream());
                var encoder = new FFmpegEncoder();
                encoder.Initialize(1920, 1080, 60, 5_000_000);

                // 编码一些帧
                var frameData = new byte[1920 * 1080 * 4];
                for (int i = 0; i < 10; i++)
                {
                    var encoded = encoder.Encode(frameData);
                    await sender.SendMessageAsync(MessageType.VideoFrame, encoded);
                }

                // 清理
                encoder.Dispose();
                sender.Dispose();
                clientTcp.Close();
                serverTcp.Close();
                listener.Stop();

                if (iteration % 3 == 0)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    var currentMemory = GC.GetTotalMemory(true);
                    LogHelper.Info($"迭代 {iteration}: 当前内存 {currentMemory / 1024 / 1024}MB");
                }
            }

            // 强制GC
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var finalMemory = GC.GetTotalMemory(true);
            var memoryGrowth = finalMemory - initialMemory;
            var memoryGrowthMB = memoryGrowth / 1024.0 / 1024.0;

            LogHelper.Info($"最终内存: {finalMemory / 1024 / 1024}MB");
            LogHelper.Info($"内存增长: {memoryGrowthMB:F2}MB");

            // 断言：内存增长应该在合理范围内
            Assert.True(memoryGrowthMB < 100, $"内存增长过大: {memoryGrowthMB:F2}MB");

            LogHelper.Info("✅ 内存泄漏测试通过");
        }
    }
}
