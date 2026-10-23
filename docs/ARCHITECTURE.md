# LinkView 架构

## 1. 目标与约束

- Windows 与 Android 使用各自自然的 UI/平台实现，但共享信令协议与核心状态语义。
- 媒体不经过信令服务器。
- 所有持久化、日志、诊断和 UI 更新不得阻塞采集/编码/网络线程。
- 未经用户明确授权不开始采集；系统停止采集后状态必须立即反映。

## 2. 工程分层

```
Windows                                   Android
┌───────────────────────────────┐         ┌──────────────────────────────┐
│ LinkView.Windows (WPF/MVVM)   │         │ app (Compose + StateFlow)    │
│ ViewModels / Views / Themes   │         │ ui/ , MainActivity           │
├───────────────────────────────┤         ├──────────────────────────────┤
│ Services / Media (应用编排)    │         │ session/ , rtc/ , net/       │
│ SessionCoordinator            │         │ SessionManager, WebRtcClient │
│ ScreenCaptureService          │         │ ScreenShareService           │
│ SignaingClient, Discovery     │         │ SignalingClient, Discovery   │
├───────────────────────────────┤         ├──────────────────────────────┤
│ LinkView.Core (领域)          │         │ 与 Core 对齐的数据/状态语义   │
│ 状态机、配置、错误、身份、IP     │         │ 独立 Kotlin 实现              │
├───────────────────────────────┤         └──────────────────────────────┘
│ LinkView.Protocol (协议)       │
└───────────────────────────────┘
                 ▲
                 │ WebSocket / JSON
        ┌────────┴─────────┐
        │ LinkView.Signaling│  ASP.NET Core, 内存会话
        └───────────────────┘
```

- `LinkView.Protocol`：消息枚举、强类型 payload、信封校验、版本检查。
- `LinkView.Core`：纯 .NET、无 UI 依赖；状态机、错误目录、配置模型/迁移/校验、设备身份、配对码、IP 地址工具。
- `LinkView.Media.Vp8`：内置 SIPSorceryMedia.Encoders 的 BSD-3 源码并用 Abstractions 10.0.17 重新编译，避免二进制枚举错位；携带 x64 `vpxmd.dll`。
- `LinkView.Signaling`：无状态媒体、单实例内存会话、认证挑战、配对、SDP/ICE 转发、限流与清理。
- `LinkView.Windows`：WPF 表现层 + 平台服务。
- `android/app`：Kotlin 单模块，按 data/net/rtc/session/media/diagnostics/ui 分包。

## 3. 会话状态机（Windows 权威实现）

状态：

`Idle → Discovering → DeviceFound → Pairing → AwaitingConsent → Negotiating → Connecting → Connected ⇄ Sharing → Reconnecting → Stopping → Disconnected / Failed`

规则（`ShareSessionStateMachine`）：
- 非法转换被拒绝，不改变当前状态。
- `Sharing/Connected/Reconnecting` 被视为“持有媒体资源”，离开前必须停止采集、关闭 PeerConnection。
- 任意非终止状态都可进入 `Stopping/Disconnected/Failed`（按转换表）。
- `Disconnected/Failed` 可重新进入 `Pairing/Discovering`。
- 界面状态、侧边栏、错误处理与资源释放均以该状态机为准，不使用多个互相矛盾的布尔变量。

Android 的 `SessionState` 对齐相同语义（不含 Discovering 阶段，因为设备发现独立运行）。

## 4. Windows 运行时服务

| 服务 | 职责 | 生命周期 |
|---|---|---|
| `SettingsService` | 配置加载/迁移/校验/原子保存/导入导出 | 单例 |
| `IdentityService` | ECDSA P-256 身份，DPAPI 保护私钥 | 单例、懒加载 |
| `SecretStore` | TURN 凭据 DPAPI 加密 | 单例 |
| `LogStore` + `LinkViewLoggerProvider` | 内存环形日志 + 文件日志 + 脱敏 | 单例 |
| `DeviceStore` / `ConnectionHistoryService` | 设备/历史 JSON 持久化 | 单例 |
| `DiscoveryService` | IPv4/IPv6 多播发现，设备 TTL | 单例，可启停 |
| `NetworkInfoService` | 网卡/地址/作用域枚举 | 单例 |
| `StunProbe` | 真实 RFC 5389 Binding 请求 | 单例 |
| `SignalingClient` | WebSocket + 挑战签名 + 消息循环 + 心跳 | 每会话 |
| `SessionCoordinator` | 编排配对、SDP/ICE、采集、状态机、重连、历史 | 单例 |
| `ScreenCaptureService` | WGC 采集、D3D11 拷贝、源枚举 | 单例，可复用 |
| `WebRtcSession` | SIPSorcery PC、VP8 编解码、统计、候选过滤 | 每会话 |
| `DiagnosticsService` | 真实检查 + 导出报告 | 单例 |

### 4.1 媒体发送路径（Windows）

```
WGC FrameArrived (专用线程)
  → D3D11 CopyResource 到 staging 纹理
  → Map + 逐行拷贝 BGRA 到复用缓冲
  → FrameAvailable（同步、快速）
  → SessionCoordinator.OnLocalFrame
      ├─ 按目标 FPS 节流
      ├─ 预览事件（UI 异步拷贝，约 11 FPS）
      ├─ 每 N 秒 ForceKeyFrame
      └─ VpxVideoEncoder.EncodeVideo (VP8, 软件)
  → RTCPeerConnection.SendVideo(90 kHz duration, sample)
  → SRTP/UDP
```

- 采集尺寸在 WGC FramePool 创建时按 Settings 等比缩放并取偶数。
- 编码在 WGC 回调线程上完成，天然形成背压；不做无界帧队列。
- 预览是唯一的额外拷贝，且限制刷新率，避免 UI 阻塞采集。

### 4.2 媒体接收路径（Windows）

```
RTP 视频帧 (SIPSorcery 线程)
  → 有界 Channel<VideoPacket>(容量 2, DropOldest)
  → 专用解码 Task
  → VpxVideoEncoder.DecodeVideo (VP8 → BGR24)
  → RemoteFrame 事件
  → ViewModel 在 UI 线程写入 WriteableBitmap（33 ms 节流）
```

- 丢帧策略只丢弃尚未进入解码器的完整帧，不破坏参考帧关系。
- 若解码失败，记录错误并继续，不阻塞 RTP 接收。

### 4.3 线程模型

- UI 线程只做绑定与位图写入。
- WGC 回调线程负责采集与编码。
- SIPSorcery 的 ICE/DTLS/RTP 在自己的线程；候选与状态事件通过不可变 DTO 转发。
- 信令消息通过单读者 Channel 串行处理，避免 SDP/ICE 乱序。
- 所有取消都通过 `CancellationToken`；会话结束时取消信令处理、停止采集、关闭 PeerConnection。

## 5. 信令与配对

1. WebSocket 连接。
2. `hello` → 服务端下发随机 `authChallenge`。
3. 客户端用设备私钥签名规范化字符串，发送 `authProof`。
4. 服务端验证设备 ID = SHA-256(SPKI) 前缀、签名、时间偏差与挑战一次性。
5. Host `pairCreate` → 6 位一次性码（哈希+盐存储）。
6. Guest `pairJoin` → 服务端扫描等待中的会话做恒定时间校验；失败按连接/IP 限流。
7. Host 收到 `joinRequest` 并确认，双方收到 `pairJoined`。
8. Host 创建 Offer；Guest 应答；双方交换 trickle ICE。
9. 任一端可 `sessionEnd`；服务端清理临时状态并通知对端。

详见 [PROTOCOL.md](PROTOCOL.md) 与 [NETWORKING.md](NETWORKING.md)。

## 6. 错误模型

`LinkViewError`：分类（Permissions/MediaCapture/Encoding/Decoding/Signaling/Pairing/Network/IceConnectivity/...）、稳定错误码、中文用户提示、技术细节、是否可重试、建议动作。

错误路径：
- 采集失败 → `CAPTURE_*`
- 编码/解码失败 → `ENCODER_*` / `DECODER_*`
- SDP/ICE/媒体未到达 → 对应分类
- 信令错误码映射为面向用户的 `LinkViewError`，同时写入结构化日志。
- 日志包含会话 ID、模块、事件与结果，不包含 SDP、配对码或凭据。

## 7. Android 运行时

- `LinkViewApplication` 初始化 `PeerConnectionFactory`、设置、Keystore 身份、日志、`SessionManager`、`DiscoveryService`、`Diagnostics`。
- `SessionManager`：
  - 串行处理信令消息；
  - 维护与 Windows 对齐的 `SessionState` 与 UI state；
  - host 创建 Offer，guest 应答；
  - 维护 RTC 统计轮询（1 s）；
  - 屏幕共享由 `ScreenShareService` 持有前台通知，`WebRtcClient` 创建 `ScreenCapturerAndroid`。
- `WebRtcClient`：统一计划（Unified Plan）、VP8、sendrecv 视频轨、远端渲染代理、`getStats()` 解析。
- 远端渲染由 Compose `AndroidView(SurfaceViewRenderer)` 完成；`DisposableEffect` 负责 detach/release。

## 8. 配置与持久化

Windows：
- `%LOCALAPPDATA%\LinkView\settings.json`（带 `schemaVersion`）
- `identity.key`（DPAPI）
- `secrets.dat`（DPAPI）
- `history.json`、`devices.json`
- `logs/linkview-YYYY-MM-DD.log`
- `diagnostics/*.md`

Android：
- SharedPreferences `linkview.settings`
- SharedPreferences `linkview.secrets`（AES-GCM 密钥在 Android Keystore）
- 设备私钥在 Android Keystore，不可导出

## 9. 已知架构缺口

- Android 没有连接历史持久化与“收藏/备注”界面。
- Windows 无托盘、开机启动、音频、H.264、硬件编码与自适应码率。
- 局域网直连需要中心信令；未实现无服务器的 LAN SDP 交换。
- 信令服务为单实例内存服务，无持久化与集群。
- Windows 无法可靠暴露选中的 ICE 候选对，因此路径标签仅描述策略。
