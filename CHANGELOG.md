# 变更日志

## 0.9.0 — 2026-10-23

首个工程预览版。

### 新增

- Windows WPF 客户端（.NET 10、MVVM、完整自建设计系统、浅/深色主题）。
- 10 个导航页面与设备收藏/最近设备区域：首页、设备中心、连接中心、屏幕共享、性能监控、网络诊断、连接历史、高级设置、日志与诊断、关于。
- Windows.Graphics.Capture 显示器/窗口采集；D3D11 拷贝；本地预览。
- 内置并重新编译 VP8 编解码（libvpx / vpxmd），修复上游 .NET 包的枚举二进制不兼容。
- SIPSorcery WebRTC：ICE、DTLS-SRTP、VP8 发送/接收、统计、候选族过滤、ICE 重启。
- 真实屏幕端到端验证（Windows 内部双会话）。
- ASP.NET Core WebSocket 信令服务：设备认证、配对码、确认、SDP/ICE 转发、限流、TTL 清理。
- ECDSA P-256 设备身份；Windows DPAPI、Android Keystore。
- IPv4/IPv6 地址分类、端点格式化、解析与候选过滤；优先模式自动回退。
- 局域网发现（IPv4/IPv6 多播）。
- 真实 STUN 诊断、信令健康检查、接口枚举。
- 配置 v1、v0→v1 迁移、校验、原子保存、导入导出、DPAPI 凭据存储。
- 结构化日志、脱敏、内存日志页、诊断报告导出。
- Android Kotlin + Jetpack Compose 客户端源码；MediaProjection 前台服务、WebRTC、设置、诊断、日志。
- 70 项自动化测试；三个验证探针/端到端工具。
- Windows/信令发布产物与 Android Debug APK。

### 已知限制

- Android 未在真机/模拟器运行；Windows ↔ Android 互通未验证。
- 未实现音频、H.264、硬件编码、自适应码率/帧率。
- 局域网连接仍依赖信令服务；手动 IP 仅做格式/诊断校验。
- Windows 不展示 RTT/丢包/抖动；不区分直连/中继的具体选中候选。
- Android 未实现自动 ICE 重启。
- 未实测 TURN、跨 NAT、IPv6-only、长时间运行。
