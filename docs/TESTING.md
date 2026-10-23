# 测试与验证记录

本文件只记录**实际执行过**的结果。未执行的项目明确标注为未验证。

## 1. 环境

- Windows 11 Pro 10.0.26300，AMD64，双栈网卡（WLAN：私网 IPv4 + 全局 IPv6）。
- .NET SDK 10.0.402；运行时 10.0.7。
- 构建 Android 时临时安装：Microsoft OpenJDK 17.0.20.1、Android SDK Platform 35 / Build-Tools 35.0.0、Gradle 8.11.1。
- 无 Android 真机、无模拟器、无 TURN 服务器。

## 2. 自动化测试

命令：

```powershell
dotnet build LinkView.slnx
dotnet test LinkView.slnx
```

结果（2026-10-23）：

```
LinkView.Protocol.Tests   7 passed, 0 failed
LinkView.Core.Tests      56 passed, 0 failed
LinkView.Signaling.Tests  7 passed, 0 failed
合计 70 passed, 0 failed
```

覆盖点：

**Core（56）**
- 状态机合法/非法转换、媒体资源释放判定、停止/重试条件；
- 设备 ID 稳定性、不同密钥隔离、签名验证、篡改失败；
- 配对码格式、随机盐、恒定时间校验；
- IPv4/IPv6 地址分类、链路本地/ULA/私网、端点格式化、方括号/作用域解析、非法输入；
- 配置默认校验、码率顺序、仅中继无 TURN、强调色、迁移 v0→v1、未来版本拒绝、往返序列化。

**Protocol（7）**
- 信封/负载往返、camelCase 枚举、未知版本拒绝、非法 JSON 不抛异常、非法枚举、缺失负载、错误码字符串化。

**Signaling（7，真实 WebSocket + TestServer）**
- 完整认证/配对/接受/SDP/ICE 转发/结束；
- 未认证不能创建会话；
- 错误配对码 4 次无效 + 第 5 次锁定；
- 配对码一次性；
- 拒绝加入通知；
- 篡改签名被拒；
- 设备 ID 与公钥不匹配被拒。

## 3. 媒体探针

### 3.1 WebRTC + VP8（`tools/LinkView.WebRtcProbe`）

```powershell
dotnet run --project tools/LinkView.WebRtcProbe
```

实际输出：

```
Encoder formats: VP8/96@90000
[sender] connectionState=connected
[receiver] connectionState=connected
encodedFrames=138 decodedFrames=138 encodedBytes=51659
firstDecoded=640x360 691200 bytes
RESULT: PASS
```

验证：真实 SDP、ICE、DTLS/SRTP、VP8 编码与解码。此前使用现成 `SIPSorceryMedia.Encoders` 包时发现 Abstractions 枚举二进制不兼容（VP8 被解释为 H263），因此改为内置源码重编译；探针输出确认编码器格式为 VP8/96。

### 3.2 Windows.Graphics.Capture（`tools/LinkView.CaptureProbe`）

```powershell
dotnet run --project tools/LinkView.CaptureProbe
```

实际输出：

```
Capture item: 显示 1 size=2560x1440
frames=90 avgInterval=16.96ms p95=20.40ms effectiveFps=59.0
avgBrightness=70.1
RESULT: PASS
```

验证：D3D11 设备、WGC monitor item、缩放 FramePool、staging 纹理读取、真实桌面像素、帧率约 59 FPS。

### 3.3 双会话端到端（`tools/LinkView.E2E`）

流程：
1. 启动 `LinkView.Signaling`。
2. 创建两个独立应用数据目录、身份与 `SessionCoordinator`。
3. 主机创建配对码；客机加入；主机自动确认。
4. 真实 WebRTC 建连。
5. 主机选择主显示器开始共享。
6. 客机统计 `RemoteFrame`。

实际输出（节选）：

```
PAIRING CODE: （示例配对码已脱敏）
PAIR JOINED
WEBRTC CONNECTED
CAPTURE SOURCE: 显示器 1 · 主显示器 2560 × 1440
SHARING STARTED
HOST  sent=7fps 193kbps resolve=1280x720 codec=VP8 encode=10.2ms state=Sharing
GUEST decoded=14 frames recv=8fps 209kbps resolve=1280x720 decode=5.1ms
RESULT: PASS
```

说明：7/8 FPS 是会话刚开始约 1 秒的瞬时样本，不代表稳定性能；本次只验证链路正确性，不是性能基准。

## 4. UI 实机检查

- Windows 应用实际启动，完成首页、设备中心、连接中心、屏幕共享、性能监控、网络诊断、连接历史、高级设置、日志、关于页面渲染。
- 浅色与深色主题：通过配置切换并重启，确认导航、文字、卡片、输入框、按钮、状态色全部更新。期间发现并修复了主题字典顺序导致浅色覆盖深色的真实缺陷。
- 连接中心：通过 UI Automation 点击“生成配对码”，真实连接本机信令服务，显示 6 位码、倒计时和“正在配对”状态。
- 网络诊断：真实显示 3 项通过、1 项警告、1 项失败；失败项为当时未启动信令服务，符合预期。
- UI 输入、导航、设置下拉框、主题切换均经过手动/自动化点击验证。
- 未验证：Windows 接收远端画面的实际像素渲染（解码链路已由 E2E 验证，WPF WriteableBitmap 写入路径未做端到端真机对比）。

## 5. Android 构建

在纯 ASCII 构建目录执行：

```powershell
gradle assembleDebug
```

结果：

```
BUILD SUCCESSFUL
app-debug.apk  62 MB
```

产物已复制到 `dist/android/LinkView-0.9.0-debug.apk`。

**未执行**：
- APK 安装/启动；
- Android 14/15 MediaProjection 前台服务行为；
- 权限对话框、通知、锁屏/旋转/进程终止；
- Android ↔ Windows 互通；
- Android 接收/发送真实画面；
- Android 网络诊断真机结果。

## 6. 未验证矩阵

| 场景 | 状态 |
|---|---|
| Windows ↔ Android SDP/ICE/媒体 | 未完成 |
| Android 真机屏幕采集 | 未完成 |
| 两台 Windows 实体机（非本机回环） | 未完成 |
| IPv6-only 局域网 | 未完成 |
| 仅 IPv4 / 双栈局域网（跨主机） | 未完成 |
| 不同子网/路由器/NAT | 未完成 |
| TURN 中继 | 未完成 |
| 公网跨运营商 | 未完成 |
| 防火墙/客户端隔离 | 未完成 |
| 网络中断/切换/Wi-Fi↔移动网络 | 未完成 |
| 电脑休眠/唤醒、手机锁屏/旋转 | 未完成 |
| 长时间运行、内存泄漏 | 未完成 |
| 1080p60、CPU/GPU/内存基准 | 未完成 |
| H.264/硬件编码 | 未实现 |
| 音频 | 未实现 |

## 7. 可复现性

- Windows 与信令：`dotnet build` / `dotnet test` / `dotnet publish`，无需本机代理。
- Android：需要 JDK 17、Android SDK 35；本项目验证时使用了手动安装的 SDK 与 Gradle 8.11.1。Gradle Wrapper 已随仓库提供。
- 中文用户名路径会导致 AGP 路径检查失败；请复制到 ASCII 路径，或在 `gradle.properties` 保留 `android.overridePathCheck=true` 并使用 ASCII 构建目录。本仓库保留该开关并已记录原因。
