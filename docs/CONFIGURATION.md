# 配置

## 1. 文件位置

### Windows

| 文件 | 位置 | 说明 |
|---|---|---|
| 配置 | `%LOCALAPPDATA%\LinkView\settings.json` | 带 `schemaVersion`，可导入/导出 |
| 身份 | `%LOCALAPPDATA%\LinkView\identity.key` | DPAPI 保护的 ECDSA P-256 私钥 |
| 密钥 | `%LOCALAPPDATA%\LinkView\secrets.dat` | DPAPI 保护的 TURN 凭据 |
| 历史 | `%LOCALAPPDATA%\LinkView\history.json` | 连接记录 |
| 设备 | `%LOCALAPPDATA%\LinkView\devices.json` | 收藏、备注、最近设备 |
| 日志 | `%LOCALAPPDATA%\LinkView\logs\linkview-YYYY-MM-DD.log` | UTF-8，按天 |
| 诊断 | `%LOCALAPPDATA%\LinkView\diagnostics\*.md` | 用户主动导出 |

### Android

| 存储 | 名称 | 说明 |
|---|---|---|
| SharedPreferences | `linkview.settings` | 非敏感配置 |
| SharedPreferences | `linkview.secrets` | AES-GCM 加密的 TURN 凭据 |
| Android Keystore | `linkview.identity.v1` / `linkview.secrets.v1` | 身份私钥与加密密钥 |

## 2. Windows 配置结构

默认值（节选）：

```json
{
  "schemaVersion": 1,
  "general": {
    "deviceName": "你的电脑名",
    "defaultPage": "home",
    "launchAtStartup": false,
    "startMinimized": false,
    "closeToTray": true,
    "notificationsEnabled": true
  },
  "video": {
    "preset": "balanced",
    "maxWidth": 1920,
    "maxHeight": 1080,
    "targetFps": 30,
    "minFps": 10,
    "targetBitrateKbps": 4000,
    "minBitrateKbps": 1000,
    "maxBitrateKbps": 8000,
    "codec": "auto",
    "keyframeIntervalSeconds": 2,
    "adaptiveBitrate": true,
    "adaptiveFrameRate": true,
    "preferHardwareEncode": true,
    "preferHardwareDecode": true,
    "allowSoftwareFallback": true,
    "scaleMode": "fit"
  },
  "network": {
    "preference": "auto",
    "stunServers": [],
    "turnServers": [],
    "iceTransport": "all",
    "connectionTimeoutSeconds": 30,
    "iceTimeoutSeconds": 20,
    "reconnectMaxAttempts": 4,
    "reconnectInitialDelayMs": 1000,
    "reconnectMaxDelayMs": 8000,
    "lanDiscoveryEnabled": true,
    "allowCrossNetwork": true,
    "signalingServer": "ws://127.0.0.1:5180/ws"
  },
  "connection": {
    "requirePairingConfirmation": true,
    "allowUnknownDeviceRequests": false,
    "sessionTimeoutSeconds": 900,
    "autoReconnect": true
  },
  "security": {
    "pairingCodeTtlSeconds": 300,
    "allowClipboardSharing": false,
    "deleteSessionStateOnEnd": true,
    "showEncryptionStatus": true
  },
  "privacy": {
    "neverRecordScreen": true,
    "maskSensitiveLogs": true,
    "historyRetentionDays": 30,
    "recentDeviceLimit": 8
  },
  "appearance": {
    "theme": "system",
    "accentColor": "#3B82F6",
    "animations": "full",
    "density": "comfortable",
    "fontScale": 1.0,
    "language": "zh-CN"
  },
  "performance": {
    "statsRefreshMs": 1000,
    "hardwareEncode": true,
    "hardwareDecode": true,
    "softwareFallback": true,
    "renderVSync": true
  },
  "diagnostics": {
    "logLevel": "information",
    "logRetentionDays": 7,
    "captureMediaTimings": true,
    "exportRedactedOnly": true
  },
  "experimental": {
    "useWindowsGraphicsCapture": true,
    "enableDataChannelControl": true
  }
}
```

> 注意：UI 只暴露**已实现并对行为生效**的选项。例如 `adaptiveBitrate`、硬件编码、H.264 等字段存在于配置模型，但当前构建没有相应实现，界面不会提供虚假开关，并在相关页面明确说明。

## 3. 生效时机

| 设置 | 生效时机 |
|---|---|
| 主题、强调色、动画、字号 | 保存后即时 |
| 日志等级 | 保存后即时（新日志） |
| 设备名称 | 下一次建立信令连接/配对时 |
| 信令服务地址 | 下一次连接时 |
| 画质、分辨率、帧率、码率、关键帧间隔 | 下一次开始共享时（码率也可在下一次共享时应用） |
| STUN/TURN、ICE 策略、协议族偏好、超时 | 下一次连接时 |
| 自动重连次数/延迟 | 下一次进入重连流程时 |
| 局域网发现开关 | 下次启动生效（Windows）；Android 即时启停 |
| 默认页面 | 下次启动生效 |
| 历史/设备保留策略 | 下次保存设置时立即裁剪 |

## 4. 校验与迁移

- `SettingsValidator` 拒绝：空设备名、非法分辨率/帧率/码率顺序、超时越界、仅中继但无 TURN、非法 STUN/TURN 协议、非法 ws/wss、强调色格式、字号范围等。
- 非法配置在加载时会备份为 `.invalid-时间戳` 并回退默认值；不会静默接受。
- 版本迁移：版本 0（旧扁平字段）→ 1，包括 `deviceName`、`theme`、`fps`、`bitrateKbps`、`stunServer` 的搬移与钳制。
- 比当前版本更新的配置会被拒绝，避免猜测未知语义。
- 未知字段保存在 `UnknownFields` 并给出提示，不参与业务。
- 导入：解析 → 迁移 → 校验 → 原子保存；任何错误都会提示并保持原配置。
- 导出：写出完整配置，但不包含凭据。

## 5. 敏感凭据

- TURN 用户名可保存在普通配置（非秘密）。
- TURN 密码/凭据：
  - Windows 写入 DPAPI 的 `secrets.dat`，键为 `turn:{url}`；
  - Android 使用 Keystore AES-GCM 加密。
- UI 不回显已保存凭据；留空表示保持不变，勾选删除才会清除。
- 日志与诊断报告不包含凭据。

## 6. 信令与 TURN 示例

```json
"stunServers": ["stun:stun.example.org:3478"],
"turnServers": [
  { "uri": "turn:turn.example.org:3478?transport=udp", "username": "linkview" }
],
"iceTransport": "all",
"signalingServer": "wss://signal.example.org/ws"
```

- TURN 凭据通过设置页单独保存。
- 使用 `wss://` 时需有效证书；客户端不提供跳过证书校验的选项。
- 仅中继：`"iceTransport": "relay"`，必须至少一个 TURN，否则校验失败。

## 7. Android 设置

- 设备名称、信令服务、主题、动效。
- 画质预设、最大宽高、目标帧率（15/24/30/60）、目标码率。
- STUN（每行一个）、TURN URL/用户名/凭据、仅中继、自动重连、局域网发现。
- 配对确认开关。
- 诊断与日志入口。
- Android 当前没有“历史保留天数”“收藏设备”“日志等级”等完整设置界面。
