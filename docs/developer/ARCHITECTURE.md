# 系统架构（Developer View）

本文档给出 ExpandScreen 的系统架构与关键数据流，方便快速理解模块边界与扩展点。

---

## 1. 组件视图

### 1.1 端到端结构

```mermaid
flowchart LR
  subgraph Windows["Windows 客户端"]
    UI["ExpandScreen.UI\n(WPF + MVVM)"]
    Core["ExpandScreen.Core\n(捕获/编码/显示)"]
    Services["ExpandScreen.Services\n(连接/输入/配置/诊断)"]
    Protocol["ExpandScreen.Protocol\n(消息/收发/会话/优化)"]
    Driver["ExpandScreen.Driver\n(虚拟显示驱动)"]
    Utils["ExpandScreen.Utils\n(日志/扩展/通用)"]

    UI --> Services
    Services --> Core
    Services --> Protocol
    Core --> Protocol
    Driver --> Core
    Utils --> UI
    Utils --> Services
    Utils --> Protocol
  end

  subgraph Android["Android 客户端"]
    ACore["core\n(网络/解码/渲染/输入采集)"]
    AUI["UI\n(Compose)"]
    ACore --> AUI
  end

  Windows <--> |"USB: ADB 端口转发\nWiFi: UDP发现 + TCP会话"| Android
```

---

## 2. 解决方案结构

参考：`docs/DEVELOPMENT.md`（更偏“如何构建/调试”）

- `src/ExpandScreen.UI`：WPF 界面（设备选择、设置、运行状态）
- `src/ExpandScreen.Core`：屏幕捕获、编码、虚拟显示管理等核心逻辑
- `src/ExpandScreen.Services`：连接（USB/WiFi）、输入映射、配置、诊断等服务层
- `src/ExpandScreen.Protocol`：协议定义、消息序列化、会话、ABR/FEC 优化
- `src/ExpandScreen.Driver`：虚拟显示驱动（WDK）
- `src/ExpandScreen.Utils`：日志与通用工具
- `src/ExpandScreen.IntegrationTests`：端到端/协议行为集成测试

---

## 3. 关键数据流

### 3.1 视频链路（Windows → Android）

```mermaid
sequenceDiagram
  participant Cap as Capture(DXGI)
  participant Enc as Encoder(FFmpeg/NVENC/QSV)
  participant Svc as VideoService
  participant Proto as NetworkSender/Session
  participant Net as TCP(TLS?)
  participant Rcv as NetworkReceiver
  participant Dec as Decoder(MediaCodec)
  participant Ren as Renderer(OpenGL)

  Cap->>Enc: Frame(BGRA)
  Enc->>Svc: EncodedFrame(H264/...)
  Svc->>Proto: VideoFrame(JSON, base64 data)
  Proto->>Net: 24B header + payload
  Net->>Rcv: bytes
  Rcv->>Dec: payload
  Dec->>Ren: YUV/RGB
```

说明：

- 发送端会对 media 队列做流控/丢弃，优先保实时性。
- 可选 ABR/FEC：弱网下降码率，必要时用 parity 尝试恢复缺失帧。

### 3.2 触控链路（Android → Windows）

```mermaid
sequenceDiagram
  participant Touch as TouchCollector
  participant AProto as NetworkSender
  participant Net as TCP(TLS?)
  participant WProto as NetworkReceiver/Session
  participant Input as InputService
  participant OS as WindowsInput

  Touch->>AProto: TouchEvent(JSON)
  AProto->>Net: bytes
  Net->>WProto: bytes
  WProto->>Input: TouchEventMessage
  Input->>OS: 注入/映射
```

### 3.3 音频链路（Windows → Android）

- `AudioConfig`：JSON，协商采样率/声道/码率/帧长。
- `AudioFrame`：RAW bytes，时间戳写入 Header `Timestamp`。

相关实现：`src/ExpandScreen.Protocol/Network/NetworkSender.cs`、`src/ExpandScreen.Protocol/Messages/ProtocolMessages.cs`

---

## 4. 扩展点清单

- **协议扩展**：新增 `MessageType` + payload + 双端编解码；规则见 `docs/developer/API.md`
- **性能优化**：media 流控（码率/队列阈值/丢弃策略）、FEC 参数、关键帧策略
- **安全**：WiFi TLS（证书、配对码策略、诊断信息脱敏）
- **可观测性**：日志、会话统计（bytes/messages/rtt/loss）、诊断快照

