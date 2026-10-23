# 构建

## 1. Windows 客户端与信令服务

### 前提

- Windows 10 19041+ / Windows 11，x64。
- .NET SDK 10（验证使用 10.0.402）。
- 无需管理员权限。
- Windows.Graphics.Capture 需要 Windows 10 1903+；本项目目标为 19041。

### 构建

```powershell
cd LinkView
dotnet restore LinkView.slnx
dotnet build LinkView.slnx -c Release
dotnet test LinkView.slnx
```

### 发布

```powershell
dotnet publish src/LinkView.Windows/LinkView.Windows.csproj `
  -c Release -r win-x64 --self-contained false -o dist/windows

dotnet publish src/LinkView.Signaling/LinkView.Signaling.csproj `
  -c Release -o dist/signaling
```

- 依赖 .NET 10 Desktop Runtime（目标机器需要安装）。
- `dist/windows/vpxmd.dll` 必须与 `LinkView.exe` 同目录。
- 无需单独的 VC++ 运行库（vpxmd 为静态依赖）。

### 运行

```powershell
cd dist/signaling
./LinkView.Signaling.exe
# 另开终端
cd dist/windows
./LinkView.exe
```

信令默认监听 `0.0.0.0:5180`。可用环境变量或 `appsettings.json` 修改：

```powershell
$env:ASPNETCORE_URLS="http://0.0.0.0:5180"
```

生产建议前置 Caddy/Nginx，提供 `wss://`。

## 2. Android 客户端

### 前提

- JDK 17（验证：Microsoft OpenJDK 17.0.20.1）
- Android SDK Platform 35、Build-Tools 35.0.0、Platform-Tools
- Gradle 8.11.1（Wrapper 已提供）
- 设备/模拟器 Android 8.0（API 26）以上；屏幕采集前台服务按 Android 14 要求实现。

### 配置 SDK

创建 `android/local.properties`（不要提交）：

```properties
sdk.dir=C:/Android/Sdk
```

或设置 `ANDROID_HOME`。

### 构建

```bash
cd android
./gradlew assembleDebug
# APK: app/build/outputs/apk/debug/app-debug.apk
```

Windows：

```powershell
cd android
.\gradlew.bat assembleDebug
```

### 中文/非 ASCII 路径

Android Gradle Plugin 在 Windows 上会对非 ASCII 项目路径报错。仓库保留：

```properties
android.overridePathCheck=true
```

但即使开启，AAPT2/Javac 仍可能在中文路径下失败（本项目验证时遇到 `文件名、目录名或卷标语法不正确`）。推荐：

```powershell
Copy-Item -Recurse android D:\linkview-build\android
cd D:\linkview-build\android
.\gradlew.bat assembleDebug
```

本项目的 APK 即通过该方式构建。

### 安装

```bash
adb install -r app/build/outputs/apk/debug/app-debug.apk
```

默认信令地址为 `ws://10.0.2.2:5180/ws`（Android 模拟器访问宿主机）。真机请在设置中改为电脑局域网 IP，例如 `ws://192.168.1.20:5180/ws`，并确保防火墙允许 TCP 5180（信令）和 UDP 媒体端口。

## 3. 验证工具

```powershell
# WebRTC + VP8 回环
dotnet run --project tools/LinkView.WebRtcProbe

# Windows.Graphics.Capture
dotnet run --project tools/LinkView.CaptureProbe

# 端到端（先启动信令）
dotnet run --project src/LinkView.Signaling
dotnet run --project tools/LinkView.E2E
```

`LinkView.E2E` 需要信令服务已监听 `127.0.0.1:5180`；它会创建临时应用数据目录，不会使用你的真实配置。

## 4. 产物

| 路径 | 内容 |
|---|---|
| `dist/windows/` | Windows 客户端（framework-dependent, win-x64） |
| `dist/signaling/` | 信令服务 |
| `dist/android/LinkView-0.9.0-debug.apk` | Android Debug APK（62 MB） |
| `android/gradle/wrapper/` | Gradle Wrapper 8.11.1 |

`dist/` 与 `_screenshots/` 已被 `.gitignore` 忽略；它们是本地验证产物。

## 5. 故障

- `NETSDK1045`：未安装 .NET 10 SDK。
- `vpxmd.dll` 缺失：确认发布输出包含该 DLL，且进程为 x64。
- Android `Unsupported class file major version`：使用 JDK 17，而不是 JDK 8/21 混用。
- Android 依赖下载失败：配置 Gradle 代理（`systemProp.https.proxyHost` 等）或使用可达的 Maven 镜像。
