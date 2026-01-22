using System.IO;
using System.Net;
using System.Net.Sockets;
using ExpandScreen.Protocol.Messages;
using ExpandScreen.Protocol.Network;
using Xunit;

namespace ExpandScreen.IntegrationTests
{
    /// <summary>
    /// 网络传输模块单元测试
    /// </summary>
    public class NetworkTransportTests
    {
        /// <summary>
        /// 测试消息序列化和反序列化
        /// </summary>
        [Fact]
        public void TestMessageSerialization()
        {
            // 创建测试消息头
            var header = MessageSerializer.CreateHeader(MessageType.Heartbeat, 100, 42);

            // 序列化
            byte[] serialized = MessageSerializer.SerializeHeader(header);

            // 验证长度
            Assert.Equal(MessageSerializer.HEADER_SIZE, serialized.Length);

            // 反序列化
            var deserialized = MessageSerializer.DeserializeHeader(serialized);

            // 验证字段
            Assert.Equal(MessageSerializer.MAGIC_NUMBER, deserialized.Magic);
            Assert.Equal(MessageType.Heartbeat, deserialized.Type);
            Assert.Equal(MessageSerializer.PROTOCOL_VERSION, deserialized.Version);
            Assert.Equal(100u, deserialized.PayloadLength);
            Assert.Equal(42u, deserialized.SequenceNumber);
        }

        /// <summary>
        /// 测试JSON负载序列化
        /// </summary>
        [Fact]
        public void TestJsonPayloadSerialization()
        {
            // 创建测试消息
            var handshake = new HandshakeMessage
            {
                DeviceId = "test-device",
                DeviceName = "Test Device",
                ClientVersion = "1.0.0",
                ScreenWidth = 1920,
                ScreenHeight = 1080
            };

            // 序列化
            byte[] serialized = MessageSerializer.SerializeJsonPayload(handshake);
            Assert.True(serialized.Length > 0);

            // 反序列化
            var deserialized = MessageSerializer.DeserializeJsonPayload<HandshakeMessage>(serialized);

            // 验证
            Assert.NotNull(deserialized);
            Assert.Equal("test-device", deserialized.DeviceId);
            Assert.Equal("Test Device", deserialized.DeviceName);
            Assert.Equal(1920, deserialized.ScreenWidth);
            Assert.Equal(1080, deserialized.ScreenHeight);
        }

        /// <summary>
        /// 测试完整消息组合
        /// </summary>
        [Fact]
        public void TestCombineMessage()
        {
            // 创建测试负载
            var payload = new byte[] { 1, 2, 3, 4, 5 };

            // 创建消息头
            var header = MessageSerializer.CreateHeader(MessageType.VideoFrame, (uint)payload.Length, 1);

            // 组合消息
            byte[] combined = MessageSerializer.CombineMessage(header, payload);

            // 验证
            Assert.Equal(MessageSerializer.HEADER_SIZE + payload.Length, combined.Length);

            // 验证头部
            byte[] headerBytes = new byte[MessageSerializer.HEADER_SIZE];
            Buffer.BlockCopy(combined, 0, headerBytes, 0, MessageSerializer.HEADER_SIZE);
            var deserializedHeader = MessageSerializer.DeserializeHeader(headerBytes);
            Assert.Equal(MessageType.VideoFrame, deserializedHeader.Type);

            // 验证负载
            byte[] deserializedPayload = new byte[payload.Length];
            Buffer.BlockCopy(combined, MessageSerializer.HEADER_SIZE, deserializedPayload, 0, payload.Length);
            Assert.Equal(payload, deserializedPayload);
        }

        /// <summary>
        /// 测试魔数验证失败
        /// </summary>
        [Fact]
        public void TestInvalidMagicNumber()
        {
            // 创建无效的消息头
            byte[] invalidHeader = new byte[MessageSerializer.HEADER_SIZE];
            // 写入错误的魔数
            invalidHeader[0] = 0xFF;
            invalidHeader[1] = 0xFF;
            invalidHeader[2] = 0xFF;
            invalidHeader[3] = 0xFF;

            // 应该抛出异常
            Assert.Throws<InvalidDataException>(() =>
            {
                MessageSerializer.DeserializeHeader(invalidHeader);
            });
        }

        /// <summary>
        /// 测试NetworkSender和NetworkReceiver端到端通信
        /// </summary>
        [Fact]
        public async Task TestSenderReceiverCommunication()
        {
            // 创建TCP连接对
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;

            TcpClient? serverClient = null;
            TcpClient? clientClient = null;

            try
            {
                // 客户端连接
                var connectTask = Task.Run(async () =>
                {
                    var client = new TcpClient();
                    await client.ConnectAsync(IPAddress.Loopback, port);
                    return client;
                });

                // 服务器接受
                var acceptTask = listener.AcceptTcpClientAsync();

                serverClient = await acceptTask;
                clientClient = await connectTask;

                // 创建Sender和Receiver
                var sender = new NetworkSender(clientClient.GetStream());
                var receiver = new NetworkReceiver(serverClient.GetStream());

                // 设置接收事件
                MessageReceivedEventArgs? receivedMessage = null;
                var receiveEvent = new TaskCompletionSource<bool>();

                receiver.MessageReceived += (s, e) =>
                {
                    receivedMessage = e;
                    receiveEvent.TrySetResult(true);
                };

                // 发送测试消息
                var testPayload = new HandshakeMessage
                {
                    DeviceId = "test-123",
                    DeviceName = "Test",
                    ScreenWidth = 1920,
                    ScreenHeight = 1080
                };

                await sender.SendMessageAsync(MessageType.Handshake, testPayload);

                // 等待接收（带超时）
                var timeout = Task.Delay(5000);
                var completed = await Task.WhenAny(receiveEvent.Task, timeout);

                Assert.True(completed == receiveEvent.Task, "Message not received within timeout");
                Assert.NotNull(receivedMessage);
                Assert.Equal(MessageType.Handshake, receivedMessage.Header.Type);

                // 反序列化并验证
                var received = MessageSerializer.DeserializeJsonPayload<HandshakeMessage>(receivedMessage.Payload);
                Assert.NotNull(received);
                Assert.Equal("test-123", received.DeviceId);
                Assert.Equal(1920, received.ScreenWidth);

                // 清理
                sender.Dispose();
                receiver.Dispose();
            }
            finally
            {
                serverClient?.Close();
                clientClient?.Close();
                listener.Stop();
            }
        }

        /// <summary>
        /// 测试NetworkSession握手流程
        /// </summary>
        [Fact]
        public async Task TestNetworkSessionHandshake()
        {
            // 创建TCP连接对
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;

            TcpClient? serverTcp = null;
            TcpClient? clientTcp = null;

            try
            {
                // 客户端连接
                var connectTask = Task.Run(async () =>
                {
                    var client = new TcpClient();
                    await client.ConnectAsync(IPAddress.Loopback, port);
                    return client;
                });

                // 服务器接受
                var acceptTask = listener.AcceptTcpClientAsync();

                serverTcp = await acceptTask;
                clientTcp = await connectTask;

                // 创建会话
                var clientSession = new NetworkSession(clientTcp.GetStream());
                var serverSession = new NetworkSession(serverTcp.GetStream());

                // 服务器端监听握手
                var serverHandshakeReceived = new TaskCompletionSource<HandshakeMessage>();
                serverSession.MessageReceived += async (s, e) =>
                {
                    if (e.Header.Type == MessageType.Handshake)
                    {
                        var handshake = MessageSerializer.DeserializeJsonPayload<HandshakeMessage>(e.Payload);
                        if (handshake != null)
                        {
                            serverHandshakeReceived.TrySetResult(handshake);
                            // 响应握手
                            await serverSession.RespondToHandshakeAsync(handshake, true);
                        }
                    }
                };

                // 客户端发起握手
                var clientHandshake = new HandshakeMessage
                {
                    DeviceId = "client-001",
                    DeviceName = "Test Client",
                    ScreenWidth = 2560,
                    ScreenHeight = 1600
                };

                var handshakeTask = clientSession.PerformHandshakeAsync(clientHandshake);

                // 等待握手完成
                var timeout = Task.Delay(5000);
                var completed = await Task.WhenAny(handshakeTask, timeout);

                Assert.True(completed == handshakeTask, "Handshake timeout");
                Assert.True(await handshakeTask, "Handshake failed");
                Assert.True(clientSession.IsHandshakeCompleted);
                Assert.NotNull(clientSession.SessionId);

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
    }
}

