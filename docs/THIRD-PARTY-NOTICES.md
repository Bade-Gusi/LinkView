# 第三方组件与许可证

本项目实际使用以下组件。版本以项目文件为准；分发时应随附相应许可证文本。

| 组件 | 版本 | 许可证 | 用途 |
|---|---|---|---|
| .NET / ASP.NET Core | 10 | MIT | 运行时、宿主、DI、日志、信令服务 |
| SIPSorcery | 10.0.17 | BSD-3-Clause | ICE、DTLS、SRTP、RTP、PeerConnection |
| SIPSorceryMedia.Abstractions | 10.0.17 | BSD-3-Clause | 媒体抽象与像素转换 |
| SIPSorceryMedia.Encoders（源码内置） | 10.0.4 源码 | BSD-3-Clause | VP8 编解码封装 |
| libvpx / vpxmd.dll | 随 Encoders 10.0.4 提供 | BSD-3-Clause | VP8 编解码原生库 |
| SIPSorceryMedia.FFmpeg | 仅评估，未纳入最终依赖 | LGPL/GPL（视构建） | 未使用；避免原生 FFmpeg 分发复杂度 |
| BouncyCastle.Cryptography | 2.7.0 | MIT | DTLS 证书与加密原语（SIPSorcery 依赖） |
| Concentus | 2.2.2 | BSD-3-Clause | Opus（当前版本未启用音频） |
| DnsClient | 1.8.0 | MIT | SIPSorcery 依赖 |
| Makaretu.Dns.Multicast | 0.27.0 | MIT | SIPSorcery 依赖（mDNS 相关） |
| SIPSorcery.WebSocketSharp | 0.0.1 | 见该包许可证 | SIPSorcery WebSocket 依赖 |
| Vortice.Direct3D11 / Vortice.DXGI | 3.8.3 | MIT | WGC 帧的 D3D11 纹理互操作 |
| CommunityToolkit.Mvvm | 8.4.2 | MIT | Windows MVVM 源生成器 |
| Microsoft.Extensions.Hosting / Logging | 10.0.13 | MIT | Windows 宿主与日志 |
| Microsoft.AspNetCore.Mvc.Testing | 10.x | MIT | 信令集成测试 |
| xunit / Microsoft.NET.Test.Sdk | 随模板 | Apache-2.0 / MIT | 测试 |
| Android Gradle Plugin | 8.9.1 | Apache-2.0 | Android 构建 |
| Kotlin | 2.1.10 | Apache-2.0 | Android 语言 |
| Jetpack Compose / AndroidX | BOM 2025.02.00 | Apache-2.0 | Android UI |
| Kotlin Coroutines | 1.10.1 | Apache-2.0 | Android 并发 |
| kotlinx.serialization | 1.8.0 | Apache-2.0 | Android JSON |
| OkHttp | 4.12.0 | Apache-2.0 | Android WebSocket/HTTP |
| io.github.webrtc-sdk:android | 137.7151.05 | BSD-3-Clause | Android WebRTC（libwebrtc 构建） |
| Gradle | 8.11.1 | Apache-2.0 | 构建工具（Wrapper） |

## 说明

- `src/LinkView.Media.Vp8/LICENSE` 为 SIPSorceryMedia.Encoders 的 BSD-3-Clause 文本；`vpxmd.dll` 的 BSD-3 文本随构建输出为 `VP8-LICENSE.txt`。
- SIPSorceryMedia.Encoders 的源码经过重新编译以匹配 `SIPSorceryMedia.Abstractions 10.0.17`，原因见 `docs/TESTING.md`。未修改其编解码逻辑。
- `SIPSorceryMedia.FFmpeg` 仅用于可行性评估，未进入最终运行时依赖；因此不引入 FFmpeg 原生库的许可证与分发负担。
- Android WebRTC 版本为 `137.7151.05`；本项目**未验证**该构建与 SIPSorcery 10.0.17 的跨端互通。
- 项目自身源代码未授予开源许可，作者保留所有权利。未经明确书面许可，不得复制、修改、分发或使用。
