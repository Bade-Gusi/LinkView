# LinkView

Professional peer-to-peer screen sharing for Windows and Android, built on standard WebRTC with VP8 video, .NET 10/WPF, and Kotlin/Jetpack Compose.

LinkView focuses on explicit pairing, clear connection state, real diagnostics, and truthful reporting of what the implementation can and cannot do. It is an engineering preview, not a finished commercial product.

> **Project status: 0.9.0 engineering preview.** The Windows capture and WebRTC pipeline is implemented and has been verified locally. The Android client compiles into an APK but has not been run on a physical device or emulator. Windows-to-Android interoperability has not been verified. See [docs/STATUS.md](docs/STATUS.md) for the authoritative status report.

## Verification Status

| Area | Status | Evidence |
|---|---|---|
| Windows client build | Verified | `dotnet build` succeeds |
| Signaling service | Verified | Runs and serves `/healthz`; real WebSocket tests |
| Automated tests | Verified | 70/70 passing (Core 56, Protocol 7, Signaling 7) |
| Windows.Graphics.Capture | Verified | 90 frames, 59 FPS, 2560x1440 to 1280x720 |
| VP8 encode/decode | Verified | 138 frames encoded and decoded |
| Two-session end-to-end | Verified | Pairing, WebRTC, real screen, 14 frames decoded |
| Windows UI | Verified | Light/dark themes, pairing, diagnostics |
| Android APK build | Verified | `assembleDebug` succeeds, 62 MB APK |
| Android runtime | Not verified | No physical device or emulator available |
| Windows-Android interop | Not verified | Requires device testing |
| TURN, cross-NAT, IPv6-only | Not tested | No suitable test infrastructure |

## Capabilities

### Shared

- One-time six-digit pairing codes with host confirmation.
- ECDSA P-256 device identities; private keys protected by DPAPI on Windows and Android Keystore on Android.
- Signaling service relays SDP and ICE only; media never passes through it.
- WebRTC with DTLS-SRTP; VP8 video.
- STUN/TURN configuration, direct-first ICE policy, optional relay-only mode.
- IPv4/IPv6 candidate filtering with explicit-only and preferred-with-fallback modes.
- LAN device discovery over IPv4 multicast, with best-effort IPv6 multicast.
- Structured, redacted logs and real network diagnostics.

### Windows

- WPF client with a custom design system, light/dark themes, and ten navigation pages.
- Windows.Graphics.Capture for monitors and windows; multi-monitor source selection.
- Local preview, remote view, full-screen viewer, pause/resume/stop.
- Settings for video, network, security, privacy, appearance, performance, and diagnostics.
- Connection history, saved devices, favorites, and notes.
- Real interface, DNS, signaling, and STUN checks.
- ICE restart with bounded backoff and configurable retry limits.

### Android

- Kotlin, Jetpack Compose, coroutines, StateFlow.
- MediaProjection capture in a `mediaProjection` foreground service with a stop action.
- WebRTC send/receive with SurfaceViewRenderer.
- Android Keystore identity and encrypted TURN credentials.
- Screens for home, devices, pairing, session, settings, diagnostics, and logs.
- WebRTC statistics for RTT, packet loss, jitter, frame rate, and bitrate.

## Architecture

```text
Windows (WPF/MVVM)                    Android (Compose)
  Views / ViewModels                    ui/
  SessionCoordinator                    SessionManager
  ScreenCaptureService                  ScreenShareService
  WebRtcSession (SIPSorcery)            WebRtcClient (libwebrtc)
  SignalingClient                       SignalingClient
  DiscoveryService                      DiscoveryService
        |                                      |
        +------------- WebSocket --------------+
                          |
                  LinkView.Signaling
             (ASP.NET Core, in-memory sessions)
                          |
             SDP / ICE only, never media
```

Shared domain semantics are implemented independently on each platform; the wire protocol is defined once in [docs/PROTOCOL.md](docs/PROTOCOL.md).

## Quick Start from Source

Requirements: Windows 10 19041+ or Windows 11, x64, and .NET 10 SDK.

```powershell
dotnet build LinkView.slnx
dotnet test LinkView.slnx

# Terminal 1: signaling server
dotnet run --project src/LinkView.Signaling

# Terminal 2: Windows client
dotnet run --project src/LinkView.Windows
```

The signaling server listens on `http://0.0.0.0:5180` by default. The Windows client defaults to `ws://127.0.0.1:5180/ws`.

1. On device A, open Connection Center and generate a pairing code.
2. On device B, join with that code.
3. Device A confirms the connection.
4. On Windows, choose a capture source and start sharing.

For a phone, change the signaling address to the PC's LAN address, for example `ws://192.168.1.20:5180/ws`. `127.0.0.1` points to the phone itself. Allow TCP 5180 and the application's UDP media traffic through the firewall.

## Build

### Windows and signaling

```powershell
dotnet publish src/LinkView.Windows/LinkView.Windows.csproj `
  -c Release -r win-x64 --self-contained true -o dist/windows

dotnet publish src/LinkView.Signaling/LinkView.Signaling.csproj `
  -c Release -r win-x64 --self-contained true -o dist/signaling
```

The client requires `vpxmd.dll` beside `LinkView.exe`. Self-contained publishes include the .NET runtime.

### Android

Requirements: JDK 17, Android SDK Platform 35, Build Tools 35.0.0, and the included Gradle wrapper.

```bash
cd android
./gradlew assembleDebug
# Output: app/build/outputs/apk/debug/app-debug.apk
```

Windows builds from a non-ASCII path may fail in AAPT2/Javac. Build from an ASCII staging directory if necessary. See [docs/BUILDING.md](docs/BUILDING.md).

## Known Limitations

- No audio support; video only.
- Software VP8 only. H.264, hardware encoding, adaptive bitrate, and adaptive frame rate are not implemented.
- A signaling service is required. Discovery is informational; manual IP entry validates and diagnoses but does not replace signaling.
- Windows does not display RTT, packet loss, or jitter because the current SIPSorcery public API does not expose a stable RTCP report reader. The UI reports these as unsupported instead of estimating them.
- Windows cannot reliably identify the selected ICE candidate pair, so it reports policy and ICE state rather than claiming direct or relayed transport.
- Android does not implement automatic ICE restart.
- The Android client has not been device-tested. MediaProjection, foreground service, vendor permission behavior, rotation, lock screen, and process death remain unverified.
- TURN, cross-NAT, IPv6-only, network switching, and long-running stability have not been tested.
- The Android build permits cleartext traffic for local `ws://` development. Production deployments should use `wss://` behind a TLS reverse proxy.
- The signaling service is a single-instance, in-memory implementation without persistence or clustering.

## Documentation

- [Architecture](docs/ARCHITECTURE.md)
- [Networking](docs/NETWORKING.md)
- [Signaling protocol](docs/PROTOCOL.md)
- [Security and privacy](docs/SECURITY.md)
- [Configuration](docs/CONFIGURATION.md)
- [Testing record](docs/TESTING.md)
- [Building](docs/BUILDING.md)
- [Troubleshooting](docs/TROUBLESHOOTING.md)
- [Project status](docs/STATUS.md)
- [Third-party notices](docs/THIRD-PARTY-NOTICES.md)

## License

No license is granted for this repository. All rights are reserved by the author. You may view the source code, but you may not copy, modify, distribute, or use it without explicit written permission.

Third-party components remain under their respective licenses. See [docs/THIRD-PARTY-NOTICES.md](docs/THIRD-PARTY-NOTICES.md). The bundled VP8 native library is distributed under the BSD 3-Clause license; its text is included in `src/LinkView.Media.Vp8/LICENSE`.

---

# 中文说明

LinkView 是一个面向 Windows 与 Android 的点对点屏幕共享工程，使用标准 WebRTC、VP8 视频、.NET 10/WPF 与 Kotlin/Jetpack Compose。

项目的重点是明确授权、清晰的连接状态、真实诊断，以及如实说明实现边界。当前版本是工程预览，不是已完成的商业产品。

## 当前状态

- Windows 客户端、信令服务、屏幕采集、VP8 编解码与双会话端到端链路已完成本机验证。
- 自动化测试 70 项全部通过。
- Android 客户端源码完整并可成功构建 APK，但尚未在真机或模拟器运行。
- Windows 与 Android 的跨端互通尚未验证。
- 未实测 TURN、跨 NAT、IPv6-only、网络切换与长时间稳定性。

准确状态以 [docs/STATUS.md](docs/STATUS.md) 为准。

## 功能概览

- 一次性六位配对码，宿主确认后才能加入。
- ECDSA P-256 设备身份；Windows 使用 DPAPI，Android 使用 Keystore。
- 信令服务只转发 SDP/ICE，不接触媒体。
- WebRTC DTLS-SRTP；VP8 视频。
- IPv4/IPv6 候选过滤与优先回退；STUN/TURN 配置；可选仅中继。
- Windows.Graphics.Capture 采集显示器或窗口，支持多显示器、预览、全屏与暂停/恢复/停止。
- 局域网设备发现、收藏、备注、连接历史。
- 真实网络诊断：接口、DNS、信令健康、STUN、TURN 配置。
- Android MediaProjection 前台服务采集、WebRTC 收发、诊断与日志。
- 浅色、深色、跟随系统主题；可关闭动画；可调字号。

## 本地启动

```powershell
dotnet build LinkView.slnx
dotnet test LinkView.slnx

dotnet run --project src/LinkView.Signaling
dotnet run --project src/LinkView.Windows
```

信令默认监听 `http://0.0.0.0:5180`，Windows 客户端默认连接 `ws://127.0.0.1:5180/ws`。

Android 真机需在设置中改为电脑局域网地址，例如 `ws://192.168.1.20:5180/ws`；`127.0.0.1` 指向手机自身。防火墙需允许 TCP 5180 与 UDP 媒体流量。

## 构建

Windows 与信令服务使用 .NET 10 SDK：

```powershell
dotnet publish src/LinkView.Windows/LinkView.Windows.csproj -c Release -r win-x64 --self-contained true -o dist/windows
dotnet publish src/LinkView.Signaling/LinkView.Signaling.csproj -c Release -r win-x64 --self-contained true -o dist/signaling
```

Android 需要 JDK 17 与 Android SDK 35：

```bash
cd android
./gradlew assembleDebug
```

## 已知限制

- 无音频；仅视频。
- 仅软件 VP8；无 H.264、硬件编码、自适应码率与自适应帧率。
- 连接必须依赖信令服务；局域网发现只用于展示，手动 IP 只做校验与诊断。
- Windows 不显示 RTT、丢包、抖动，因为 SIPSorcery 当前公开 API 没有稳定的 RTCP 统计读取入口；界面显示“不支持”，不伪造数据。
- Windows 无法可靠区分直连与中继，只展示策略与 ICE 状态。
- Android 未实现自动 ICE 重启。
- Android 未真机验证；MediaProjection、前台服务、厂商权限、旋转、锁屏与进程终止行为仍需实机测试。
- TURN、跨 NAT、IPv6-only、网络切换与长时间稳定性未实测。
- Android 为本地 `ws://` 开发保留了明文流量；生产环境应使用 `wss://`。
- 信令服务为单实例内存实现，无持久化与集群。

## 文档

- [架构](docs/ARCHITECTURE.md)
- [网络](docs/NETWORKING.md)
- [信令协议](docs/PROTOCOL.md)
- [安全与隐私](docs/SECURITY.md)
- [配置](docs/CONFIGURATION.md)
- [测试记录](docs/TESTING.md)
- [构建](docs/BUILDING.md)
- [排障](docs/TROUBLESHOOTING.md)
- [项目状态](docs/STATUS.md)
- [第三方组件与许可证](docs/THIRD-PARTY-NOTICES.md)

## 许可

本仓库未授予开源许可证，作者保留所有权利。你可以查看源码，但未经明确书面许可，不得复制、修改、分发或使用。第三方组件仍遵循各自许可证，详见 [docs/THIRD-PARTY-NOTICES.md](docs/THIRD-PARTY-NOTICES.md)。
