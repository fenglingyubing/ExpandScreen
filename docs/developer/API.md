# 通信协议（API）

本文档描述 ExpandScreen 在 **USB（ADB 端口转发）/WiFi（UDP 发现 + TCP 会话）** 场景下的通信协议、消息格式与扩展方式。

> 以当前实现为准：`src/ExpandScreen.Protocol`、`src/ExpandScreen.Services`。

---

## 1. 传输层概览

### 1.1 USB（ADB 端口转发）

- Windows 使用 ADB 建立端口转发，将本机端口映射到 Android 端口（默认：`15555 -> 15555`）。
- 随后 Windows 通过 `127.0.0.1:<localPort>` 建立 TCP 连接并交换协议消息。

相关实现：`src/ExpandScreen.Services/Connection/UsbConnection.cs`

### 1.2 WiFi（UDP 发现 + TCP 会话）

- 发现阶段：Android 广播/单播 `DiscoveryRequest` 到 UDP 端口（默认 `15556`），Windows 回包 `DiscoveryResponse`，返回 TCP 监听端口。
- 会话阶段：Android 连接 `DiscoveryResponse.TcpPort` 的 TCP 服务，进入握手/心跳/消息收发。
- 可选 TLS：Windows 端可将 TCP 流包装为 `SslStream`（配对码仅用于轻量认证/测试，非强安全保证）。

相关实现：`src/ExpandScreen.Services/Connection/WifiDiscoveryResponder.cs`、`src/ExpandScreen.Services/Connection/WifiConnection.cs`

---

## 2. TCP 消息帧格式

所有 TCP 消息采用：

```
| 24B Header | Payload (PayloadLength bytes) |
```

### 2.1 Header（24 字节，大端）

实现：`src/ExpandScreen.Protocol/Messages/MessageTypes.cs`、`src/ExpandScreen.Protocol/Messages/MessageSerializer.cs`

| 字段 | 大小 | 类型 | 说明 |
| --- | --- | --- | --- |
| Magic | 4B | uint32 | 魔数，固定 `0x45585053`（ASCII: "EXPS"） |
| Type | 1B | byte | `MessageType` |
| Version | 1B | byte | 协议版本，当前 `0x01` |
| Reserved | 2B | uint16 | 预留（当前为 0） |
| Timestamp | 8B | uint64 | UTC 毫秒时间戳（可被覆盖，用于媒体时间戳等） |
| PayloadLength | 4B | uint32 | 负载长度 |
| SequenceNumber | 4B | uint32 | 单连接内递增序列号（接收侧要求严格递增） |

### 2.2 Payload 编码规则

`NetworkSender.SendMessageAsync<T>()` 约定：

- 若 `payload` 为 `byte[]`：**原始字节**直接作为 payload（不做 JSON）。
- 否则：使用 `System.Text.Json` 序列化为 **UTF-8 JSON**。

实现：`src/ExpandScreen.Protocol/Network/NetworkSender.cs`

接收侧约束：

- 默认最大负载：`10MB`（可配置）。
- 若检测到 `SequenceNumber` 非递增：抛错并终止会话（降低重放/乱序风险）。

实现：`src/ExpandScreen.Protocol/Network/NetworkReceiver.cs`

---

## 3. 消息类型与负载

实现：`src/ExpandScreen.Protocol/Messages/MessageTypes.cs`、`src/ExpandScreen.Protocol/Messages/ProtocolMessages.cs`

| Type | 值 | Payload | 编码 | 方向（建议） | 说明 |
| --- | --- | --- | --- | --- | --- |
| Handshake | `0x01` | `HandshakeMessage` | JSON | Client → Server | 建立会话与能力协商（屏幕尺寸/版本/配对码等） |
| HandshakeAck | `0x02` | `HandshakeAckMessage` | JSON | Server → Client | 握手结果与会话 ID |
| VideoFrame | `0x03` | `VideoFrameMessage` | JSON | Server → Client | 视频帧（规范：JSON；注意：`Data` 会被 base64 化） |
| TouchEvent | `0x04` | `TouchEventMessage` | JSON | Client → Server | 触控事件 |
| Heartbeat | `0x05` | `HeartbeatMessage` | JSON | Both | 心跳 |
| HeartbeatAck | `0x06` | `HeartbeatAckMessage` | JSON | Both | 心跳确认（用于 RTT 估算） |
| AudioConfig | `0x07` | `AudioConfigMessage` | JSON | Server → Client | 音频参数协商/开关 |
| AudioFrame | `0x08` | `byte[]` | RAW | Server → Client | 音频帧数据，时间戳放在 Header `Timestamp` |
| ProtocolFeedback | `0x09` | `ProtocolFeedbackMessage` | JSON | Both | 链路反馈（RTT/收包速率/丢消息增量等） |
| BitrateControl | `0x0A` | `BitrateControlMessage` | JSON | Both | 目标码率广播（诊断/展示） |
| KeyFrameRequest | `0x0B` | `KeyFrameRequestMessage` | JSON | Both | 请求关键帧（画面破损/丢消息后） |
| FecConfig | `0x0C` | `FecConfigMessage` | JSON | Both | FEC 参数开关（数据分片/冗余分片） |
| FecShard | `0x0D` | `FecShardMessage` | JSON | Server → Client | FEC parity 分片 |
| FecGroupMetadata | `0x0E` | `FecGroupMetadataMessage` | JSON | Server → Client | FEC 分组元数据（保护范围/首序列号/分片长度等） |

---

## 4. 关键流程

### 4.1 WiFi 发现流程（UDP）

1. Client 向 UDP 端口发送 `DiscoveryRequestMessage`（JSON）。
2. Server 回包 `DiscoveryResponseMessage`（JSON），返回 `TcpPort`。
3. Client 连接 `TcpPort` 建立 TCP 会话。

相关实现：`src/ExpandScreen.Services/Connection/WifiDiscoveryResponder.cs`

### 4.2 会话建立（握手）

1. Client 发送 `Handshake`（包含 `DeviceId/DeviceName/ClientVersion/ScreenWidth/ScreenHeight/PairingCode?`）。
2. Server 校验（可选配对码），并回复 `HandshakeAck`。
3. `Accepted=true` 后才允许发送除 `Handshake/HandshakeAck` 以外的消息。

相关实现：`src/ExpandScreen.Protocol/Network/NetworkSession.cs`

### 4.3 心跳与 RTT

- 双方周期性发送 `Heartbeat`，对端立即回 `HeartbeatAck`。
- RTT 估算：`now - HeartbeatAck.OriginalTimestamp`（ms）。
- 若超过超时时间未收到心跳：触发会话清理。

相关实现：`src/ExpandScreen.Protocol/Network/NetworkSession.cs`

### 4.4 流控与丢弃策略（发送端）

`NetworkSender` 将消息分为两类队列：

- **critical**：控制/握手/心跳等，优先发送；
- **media**：`VideoFrame/AudioFrame/FEC`，可被丢弃以保实时性。

当队列溢出或累计字节超过阈值时：优先丢弃最旧的 media；极端情况下才丢 critical。

相关实现：`src/ExpandScreen.Protocol/Network/NetworkSender.cs`

### 4.5 自适应码率（ABR）与 FEC

- 接收端定期发送 `ProtocolFeedback`（收包速率、RTT、丢消息增量）。
- 发送端基于 AIMD + 平滑策略调整 media 目标码率，并通过 `BitrateControl` 广播给对端（用于展示/诊断）。
- 若启用 FEC：发送端按组为 `VideoFrame` 生成 parity 分片（`FecShard`）与元数据（`FecGroupMetadata`）；接收端在缺失时尝试恢复缺失帧。

相关实现：`src/ExpandScreen.Protocol/Optimization/AdaptiveBitrateController.cs`、`src/ExpandScreen.Protocol/Fec/FecVideoFrameGroupCodec.cs`

---

## 5. 扩展与兼容

### 5.1 新增消息类型（推荐步骤）

1. 在 `MessageType` 中分配新值（避免复用旧值）。
2. 定义 payload（优先 JSON；若为高频二进制，可用 `byte[]` + 头部时间戳/序列号承载元信息）。
3. 同步实现两端编解码与处理逻辑。
4. 增加/更新协议测试（建议：集成测试覆盖连通性与基本收发）。
5. 更新本文档与 `docs/开发流程文档.md` 的协议变更记录。

### 5.2 版本策略

当前 `Header.Version = 0x01`，建议：

- **兼容扩展**：新增字段尽量可选（nullable / 默认值），旧端可忽略；
- **破坏性变更**：升级 `Version` 并提供降级策略（握手协商/双栈兼容/灰度开关）。

---

## 6. 跨端对齐说明

- 权威实现：优先以 Windows/C# 协议实现为准：`src/ExpandScreen.Protocol`
- Android 侧协议代码位于：`android-client/app/src/main/kotlin/com/expandscreen/protocol`，若与本文档不一致，按“新增/变更消息类型”的流程同步两端并补齐测试。
- `NetworkSender` 支持 `byte[]` 作为 RAW payload；若用于跨端互通，务必在此处明确该 `MessageType` 的 **唯一** 编码方式（JSON 还是 RAW），避免出现“测试可跑但互通失败”的分裂协议。
