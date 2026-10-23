# LinkView 0.9.0 项目状态报告

日期：2026-10-23
环境：Windows 11 Pro 10.0.26300 / AMD64 / .NET SDK 10.0.402 / 无 Android 设备 / 无 TURN 服务器

本报告只陈述实际完成并验证的内容。**任何未验证的能力都不会被标记为完成。**

---

## 1. 已完成的模块

| 模块 | 状态 | 说明 |
|---|---|---|
| `LinkView.Protocol` | 已完成 | 信封、强类型 payload、版本/大小/枚举校验、JSON 编解码 |
| `LinkView.Core` | 已完成 | 状态机、错误目录、配置 v1 + 迁移/校验、身份、配对码、IP 工具 |
| `LinkView.Signaling` | 已完成并运行 | ASP.NET Core WebSocket、认证、配对、SDP/ICE、限流、TTL |
| `LinkView.Media.Vp8` | 已完成 | BSD-3 源码重新编译，修复 VP8/H263 枚举错位；x64 vpxmd |
| `LinkView.Windows` | 已完成并实机运行 | WPF + MVVM、完整 UI、WGC 采集、VP8、SIPSorcery、诊断、设置 |
| Windows 测试 | 56 + 7 通过 | 核心与协议 |
| 信令测试 | 7 通过 | 真实 WebSocket |
| 验证工具 | 已完成 | WebRTC、WGC、双会话 E2E |
| Android 客户端 | 源码 + APK 构建 | Compose、MediaProjection、WebRTC、诊断；**未真机验证** |
| 文档 | 已完成 | README、架构、网络、协议、安全、配置、测试、构建、排障、状态、第三方许可证 |

## 2. 已完成的功能

### 连接与安全
- ECDSA P-256 设备身份；Windows DPAPI / Android Keystore。
- 信令挑战-应答认证，防重放（一次性 nonce + 时间窗）。
- 设备 ID 与公钥绑定验证。
- 6 位一次性配对码、哈希+盐内存存储、过期、双端确认。
- 配对失败连接级冷却 + IP 窗口限流。
- 会话最多 2 人；未授权转发被拒绝。
- DTLS-SRTP 媒体加密；无跳过证书校验的开关。
- 日志脱敏、DPAPI/Keystore 凭据存储、不录制屏幕。

### Windows
- Windows.Graphics.Capture：显示器/窗口枚举、多显示器、等比缩放、D3D11 拷贝。
- 真实编码发送：libvpx VP8，目标分辨率/帧率/码率、关键帧间隔。
- 真实接收解码：有界队列、VP8 解码、WPF 位图渲染路径。
- 本地预览、远端画面（含全屏查看窗口）。
- 暂停/恢复/停止共享；停止立即释放资源。
- 设备发现、设备收藏/备注/移除、连接历史。
- 真实诊断：接口、IPv4/IPv6、信令健康、DNS、STUN、TURN 配置、会话状态。
- 配置导入/导出/迁移/校验/恢复默认；主题/强调色/字号/动效。
- 结构化日志、内存日志页、诊断报告导出。
- 连接拒绝、错误分类、用户可读提示、空状态、连接状态动效。
- ICE 重启恢复（host 侧）与可配置退避/次数。

### Android（源码 + 编译）
- Kotlin + Jetpack Compose + Coroutines + StateFlow。
- Android Keystore 身份与 TURN 凭据 AES-GCM。
- OkHttp WebSocket 信令、挑战签名、心跳。
- WebRTC：VP8、sendrecv、SurfaceViewRenderer、getStats 解析（RTT/丢包/抖动）。
- MediaProjection + `mediaProjection` 前台服务 + 通知停止动作。
- 屏幕采集通过 `ScreenCapturerAndroid`，系统停止回调处理。
- 设备发现、配对、同意、会话、设置、诊断、日志页面。
- 浅色/深色/跟随系统主题、组件化设计。
- APK 成功构建：`dist/android/LinkView-0.9.0-debug.apk`。

## 3. 实际构建结果

### Windows / 信令

```
dotnet build LinkView.slnx          → 成功
dotnet publish (Windows, Release)   → 成功，dist/windows/
dotnet publish (Signaling, Release) → 成功，dist/signaling/
```

发布为 `win-x64` framework-dependent；目标机需安装 .NET 10 Desktop Runtime。

### Android

```
环境：Microsoft OpenJDK 17.0.20.1 + Android SDK 35 + Build-Tools 35.0.0 + Gradle 8.11.1
在纯 ASCII 路径 D:\linkview-build\android 构建
gradle assembleDebug → BUILD SUCCESSFUL
APK 大小 62 MB
```

仓库包含 Gradle Wrapper（8.11.1）。中文用户名路径下的 AGP 路径问题已复现，构建方法见 `BUILDING.md`。

## 4. 实际测试结果

| 类别 | 结果 |
|---|---|
| 编译通过 | 完成 |
| 单元测试 | 63 项通过（Core 56 + Protocol 7） |
| 信令集成测试 | 7 项通过（真实 WebSocket） |
| 本机 WebRTC + VP8 | 138 帧编码/解码 |
| WGC 采集 | 90 帧，59 FPS，真实像素 |
| 双会话端到端 | 配对 → WebRTC → 屏幕 → 解码 14 帧 |
| Windows UI 实机 | 页面渲染、主题、配对码生成、诊断 |
| Android 编译 | APK 构建成功 |
| Android 真机 | 未执行 |
| Windows ↔ Android | 未执行 |

端到端关键输出：

```
PAIRING CODE: （示例配对码已脱敏）
PAIR JOINED
WEBRTC CONNECTED
CAPTURE SOURCE: 显示器 1 · 主显示器 2560 × 1440
HOST  sent=7fps 193kbps resolve=1280x720 codec=VP8 encode=10.2ms
GUEST decoded=14 frames recv=8fps 209kbps resolve=1280x720 decode=5.1ms
RESULT: PASS
```

瞬时 FPS 不代表持续性能；未做基准测试。

## 5. 已实际验证的网络环境

- 本机回环：两个真实 PeerConnection + 真实信令服务。
- 双栈网卡上的 ICE 建连（候选包含私网 IPv4 与全局 IPv6）。
- 无 STUN/TURN 下的本机直连。
- 信令 WebSocket 在 `127.0.0.1:5180` 的完整流程。

## 6. 尚未验证的网络环境

- Windows ↔ Android 任意网络。
- 两台实体 Windows 设备。
- 跨子网、不同路由器、企业 Wi-Fi 客户端隔离。
- 对称 NAT、严格防火墙、公网跨运营商。
- TURN 中继（无服务器可测）。
- IPv6-only、仅 IPv4 强制、链路本地 IPv6 跨设备。
- Wi-Fi/移动网络切换、断网恢复、休眠/唤醒、手机锁屏/旋转。
- 长时间稳定性、内存/CPU/GPU/温度、1080p60。
- 受保护内容（DRM）、窗口遮挡、特殊显卡驱动。

## 7. 仍然存在的问题与缺口

1. **Android 未真机运行**：权限、前台服务、MediaProjection、WebRTC 原生库行为都可能存在设备/厂商差异。
2. **跨端互通未验证**：SIPSorcery 与 `io.github.webrtc-sdk 137.7151.05` 的 SDP/VP8 互通没有实测证据。
3. **无音频**：仅视频轨。
4. **无 H.264 / 硬件编码 / 自适应码率/帧率**：软件 VP8。
5. **无纯 LAN 无信令直连**：发现只展示；手动 IP 只做校验/诊断。跨设备仍需要信令服务。
6. **Windows 指标缺口**：RTT/丢包/抖动无法从 SIPSorcery 公开 API 稳定读取，显示“不支持”。
7. **Windows 路径识别有限**：无法可靠区分直连与 TURN 中继的选中候选；只展示策略与 ICE 状态。
8. **Android 自动重连未实现**：断线只提示失败；Windows 实现了有上限的 ICE 重启。
9. **Android 无历史/收藏/备注/设备信任列表**：只有即时状态与日志。
10. **信令服务单实例内存**：无持久化、集群、集中限流；TLS 需外部反向代理。
11. **Windows 热插拔/显示器分辨率变化**未完全自动重建采集；需要重新选择源。
12. **托盘/开机启动/通知**未实现；UI 不提供虚假开关。
13. **设置模型中的部分字段**（硬件、H.264、自适应、密度、语言等）尚未生效，未在 UI 暴露或明确标注。
14. **多 TURN 服务器**：Windows 模型支持列表，但设置 UI 只编辑第一条；导入多 TURN 后保存会丢失其余条目。
15. **Android cleartext**：为本地 ws:// 开发启用，生产需收紧。

## 8. 第三方依赖及许可证

主要组件（完整列表见 `THIRD-PARTY-NOTICES.md`）：
- SIPSorcery 10.0.17 / Abstractions 10.0.17 — BSD-3-Clause
- SIPSorceryMedia.Encoders（源码内置）+ libvpx/vpxmd — BSD-3-Clause
- Vortice.Windows 3.8.3 — MIT
- CommunityToolkit.Mvvm 8.4.2 — MIT
- .NET / ASP.NET Core 10 — MIT
- BouncyCastle.Cryptography 2.7.0 — MIT
- Concentus 2.2.2 — BSD-3-Clause
- Android: AGP/Kotlin/Compose/OkHttp/Coroutines/serialization — Apache-2.0
- io.github.webrtc-sdk:android 137.7151.05 — BSD-3-Clause
- Gradle 8.11.1 — Apache-2.0
- 测试：xunit（Apache-2.0）、Microsoft.NET.Test.Sdk（MIT）、Mvc.Testing（MIT）

项目自身源代码未授予开源许可，作者保留所有权利。未经明确书面许可，不得复制、修改、分发或使用。

## 9. 运行环境要求

**Windows 客户端**
- Windows 10 19041+ / Windows 11，x64。
- .NET 10 Desktop Runtime（framework-dependent 发布）。
- 显卡支持 D3D11；WGC 可用。
- 开放防火墙：TCP 信令（默认 5180），UDP 媒体动态端口。
- 不需要管理员权限。

**信令服务**
- .NET 10 ASP.NET Core Runtime。
- 默认 TCP 5180；单实例内存。
- 生产建议：wss 反向代理、限流、监控与重启策略。

**Android**
- Android 8.0（API 26）以上。
- 真机需 Android System WebView/系统支持（WebRTC SDK 自带原生库）。
- 通知权限（Android 13+）、MediaProjection 授权。
- 与信令服务网络可达；防火墙允许 UDP 媒体。

## 10. 启动方式

```powershell
# 1) 信令
cd dist/signaling
./LinkView.Signaling.exe

# 2) Windows 客户端
cd dist/windows
./LinkView.exe

# 3) Android（若已安装）
adb install -r dist/android/LinkView-0.9.0-debug.apk
```

首次使用前，在 Windows/Android 设置中把信令地址改为可访问的地址；真机不能使用 `127.0.0.1`。

## 11. 第一次投屏

1. 两端连接同一个信令服务（或 Windows 即可）。
2. A 端“连接中心 → 生成配对码”。
3. B 端“连接中心 → 加入连接”，输入 6 位码。
4. A 端在弹窗确认。
5. Windows：进入“屏幕共享”，选择显示器/窗口，点击“开始共享”。
6. Android：进入“屏幕共享”，点击“共享我的屏幕”，允许系统 MediaProjection 授权。
7. 对端在“屏幕共享 → 对方画面”或 Android 会话页查看；可随时停止。

## 12. 验收清单（实事求是）

### A. 产品和界面
- [x] Windows 完整统一 UI
- [x] Android 原生 Compose UI（源码/编译；未真机）
- [x] 两个平台一致的品牌与设计语言
- [x] 浅色/深色/跟随系统（Windows 已实机验证；Android 未真机）
- [x] 动画克制、可关闭/精简
- [x] 丰富且有效的设置（未实现项不显示为可用）
- [x] 错误与空状态完整
- [x] 主要操作有明确反馈
- [x] UI 不执行采集/编码/网络重活

### B. 核心功能
- [x] Windows 真实发送屏幕（已实测）
- [ ] Android 真实接收显示（未验证）
- [ ] Android 真实发送屏幕（未验证）
- [x] Windows 真实接收解码（已实测解码帧；UI 像素渲染未对照）
- [x] 暂停/停止共享
- [x] 设备配对
- [x] 主动断开
- [~] 连接恢复（Windows 有上限 ICE 重启；Android 未实现）

### C. 网络
- [x] IPv4 环境可用（本机）
- [~] IPv6 候选人处理与回退（未做 IPv6-only 实测）
- [x] 不把 IPv6 做成输入框
- [x] 标准 SDP/ICE/DTLS/SRTP
- [x] 真实诊断
- [~] 直连/中继：展示策略，未可靠识别选中候选
- [x] 未验证能力未被宣称完成
- [ ] 网络切换/断线实测

### D. 质量
- [x] 明确资源释放
- [x] 无明显永久任务泄漏（代码审查 + E2E 清理；未长跑）
- [x] 重复连接未观察到无限增长（两次 E2E；未做压力测试）
- [x] 真实单元测试
- [x] 真实集成测试
- [ ] 跨端验证记录（无）
- [x] 可复现构建说明
- [x] 用户文档

### E. 安全与隐私
- [x] 未经授权不采集
- [x] 用户可立即停止
- [x] 会话身份验证
- [x] 未授权设备不能加入（测试覆盖）
- [x] 默认不录制/上传
- [x] 凭据受 OS 安全存储保护
- [x] 日志脱敏
- [x] 权限拒绝/系统停止共享处理（Windows 实测；Android 未真机）

### F. 工程完整性
- [x] Windows 按文档构建
- [x] Android 按文档构建（APK 已产出）
- [x] 信令独立构建/运行
- [x] 依赖版本明确
- [x] 核心功能非空方法
- [x] 未用假数据冒充实时指标
- [x] 未完成部分已标注

## 13. 结论

LinkView 0.9.0 是一个**可运行的 Windows 工程预览**：Windows 端的采集、VP8、WebRTC、信令、配对、UI 与诊断链路均经过真实运行验证；Android 端源码完整并能编译出 APK。

它**不是**已经完成商业交付的跨平台产品：Android 真机与跨端互通尚未验证，音频/H.264/硬件编码/纯 LAN 无信令直连尚未实现，TURN、跨 NAT、IPv6-only 与长时间稳定性未实测。上述缺口均在 README、TESTING 与 NETWORKING 中明确标注，未用界面或报告掩盖。
