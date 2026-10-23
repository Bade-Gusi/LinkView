package com.linkview.app.ui.screens

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.linkview.app.BuildConfig
import com.linkview.app.LinkViewApplication
import com.linkview.app.data.QualityPreset
import com.linkview.app.data.ThemeMode
import com.linkview.app.ui.InfoBanner
import com.linkview.app.ui.PageHeader
import com.linkview.app.ui.SecondaryButton
import com.linkview.app.ui.Segmented
import com.linkview.app.ui.SectionTitle
import com.linkview.app.ui.SettingRow
import com.linkview.app.ui.SurfaceCard

@Composable
fun SettingsScreen(app: LinkViewApplication, onNavigate: (String) -> Unit) {
    val settings by app.settings.settings.collectAsStateWithLifecycle()
    var deviceName by remember(settings.deviceName) { mutableStateOf(settings.deviceName) }
    var server by remember(settings.signalingServer) { mutableStateOf(settings.signalingServer) }
    var stun by remember(settings.stunServers) { mutableStateOf(settings.stunServers) }
    var turnUri by remember(settings.turnUri) { mutableStateOf(settings.turnUri) }
    var turnUser by remember(settings.turnUsername) { mutableStateOf(settings.turnUsername) }
    var credential by remember { mutableStateOf("") }
    var width by remember(settings.maxWidth) { mutableStateOf(settings.maxWidth.toString()) }
    var height by remember(settings.maxHeight) { mutableStateOf(settings.maxHeight.toString()) }
    var bitrate by remember(settings.targetBitrateKbps) { mutableStateOf(settings.targetBitrateKbps.toString()) }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(horizontal = 20.dp, vertical = 8.dp)
    ) {
        PageHeader("设置", "画质、网络、安全与界面")
        Spacer(Modifier.height(14.dp))

        SectionTitle("设备与连接")
        SurfaceCard {
            OutlinedTextField(
                value = deviceName,
                onValueChange = {
                    deviceName = it.take(64)
                    if (it.isNotBlank()) app.settings.update { s -> s.copy(deviceName = it.take(64)) }
                },
                label = { Text("设备名称") },
                singleLine = true,
                modifier = Modifier.fillMaxWidth()
            )
            Spacer(Modifier.height(10.dp))
            OutlinedTextField(
                value = server,
                onValueChange = {
                    server = it
                    if (it.startsWith("ws://") || it.startsWith("wss://")) {
                        app.settings.update { s -> s.copy(signalingServer = it.trim()) }
                    }
                },
                label = { Text("信令服务地址") },
                singleLine = true,
                isError = !(server.startsWith("ws://") || server.startsWith("wss://")),
                supportingText = {
                    Text("例如 ws://192.168.1.10:5180/ws；生产环境应使用 wss://")
                },
                modifier = Modifier.fillMaxWidth()
            )
            HorizontalDivider(Modifier.padding(vertical = 10.dp), color = MaterialTheme.colorScheme.outlineVariant)
            Text("主题", style = MaterialTheme.typography.titleSmall)
            Spacer(Modifier.height(8.dp))
            Segmented(
                options = listOf("跟随系统", "浅色", "深色"),
                selectedIndex = settings.theme.ordinal,
                onSelected = { index ->
                    app.settings.update { s ->
                        s.copy(
                            theme = when (index) {
                                1 -> ThemeMode.Light
                                2 -> ThemeMode.Dark
                                else -> ThemeMode.System
                            }
                        )
                    }
                }
            )
            Spacer(Modifier.height(8.dp))
            SettingRow("按钮动画", "关闭后页面切换与状态动效立即完成") {
                Switch(
                    checked = settings.animations,
                    onCheckedChange = { value -> app.settings.update { s -> s.copy(animations = value) } }
                )
            }
        }

        Spacer(Modifier.height(14.dp))
        SectionTitle("画质")
        SurfaceCard {
            Text("预设", style = MaterialTheme.typography.titleSmall)
            Spacer(Modifier.height(8.dp))
            Segmented(
                options = QualityPreset.entries.map { it.label },
                selectedIndex = settings.quality.ordinal,
                onSelected = { app.settings.applyQualityPreset(QualityPreset.entries[it]) }
            )
            Spacer(Modifier.height(12.dp))
            Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                OutlinedTextField(
                    value = width,
                    onValueChange = { raw ->
                        width = raw.filter { it.isDigit() }.take(4)
                        width.toIntOrNull()?.let { value ->
                            if (value in 320..3840) app.settings.update { s -> s.copy(maxWidth = value) }
                        }
                    },
                    label = { Text("最大宽度") },
                    singleLine = true,
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                    modifier = Modifier.weight(1f)
                )
                OutlinedTextField(
                    value = height,
                    onValueChange = { raw ->
                        height = raw.filter { it.isDigit() }.take(4)
                        height.toIntOrNull()?.let { value ->
                            if (value in 240..2160) app.settings.update { s -> s.copy(maxHeight = value) }
                        }
                    },
                    label = { Text("最大高度") },
                    singleLine = true,
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                    modifier = Modifier.weight(1f)
                )
            }
            Spacer(Modifier.height(10.dp))
            Text("目标帧率", style = MaterialTheme.typography.titleSmall)
            Spacer(Modifier.height(8.dp))
            Segmented(
                options = listOf("15", "24", "30", "60"),
                selectedIndex = listOf(15, 24, 30, 60).indexOf(settings.targetFps).coerceAtLeast(0),
                onSelected = { index ->
                    app.settings.update { s -> s.copy(targetFps = listOf(15, 24, 30, 60)[index]) }
                }
            )
            Spacer(Modifier.height(10.dp))
            OutlinedTextField(
                value = bitrate,
                onValueChange = { raw ->
                    bitrate = raw.filter { it.isDigit() }.take(6)
                    bitrate.toIntOrNull()?.let { value ->
                        if (value in 300..30000) app.settings.update { s -> s.copy(targetBitrateKbps = value) }
                    }
                },
                label = { Text("目标码率（kbps）") },
                singleLine = true,
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                modifier = Modifier.fillMaxWidth()
            )
            Text(
                "画质设置在下一次开始共享时生效。",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.padding(top = 6.dp)
            )
        }

        Spacer(Modifier.height(14.dp))
        SectionTitle("网络")
        SurfaceCard {
            OutlinedTextField(
                value = stun,
                onValueChange = {
                    stun = it
                    app.settings.update { s -> s.copy(stunServers = it) }
                },
                label = { Text("STUN 服务器（每行一个）") },
                minLines = 2,
                modifier = Modifier.fillMaxWidth()
            )
            Spacer(Modifier.height(10.dp))
            OutlinedTextField(
                value = turnUri,
                onValueChange = {
                    turnUri = it
                    app.settings.update { s -> s.copy(turnUri = it.trim()) }
                },
                label = { Text("TURN 服务器 URL") },
                singleLine = true,
                modifier = Modifier.fillMaxWidth()
            )
            Spacer(Modifier.height(8.dp))
            OutlinedTextField(
                value = turnUser,
                onValueChange = {
                    turnUser = it
                    app.settings.update { s -> s.copy(turnUsername = it.trim()) }
                },
                label = { Text("TURN 用户名") },
                singleLine = true,
                modifier = Modifier.fillMaxWidth()
            )
            Spacer(Modifier.height(8.dp))
            OutlinedTextField(
                value = credential,
                onValueChange = { credential = it },
                label = { Text("TURN 凭据") },
                singleLine = true,
                visualTransformation = PasswordVisualTransformation(),
                supportingText = { Text("凭据使用 Android Keystore 加密保存，不会写入普通配置") },
                modifier = Modifier.fillMaxWidth()
            )
            Spacer(Modifier.height(6.dp))
            SecondaryButton("保存 TURN 凭据", {
                app.settings.setTurnCredential(credential)
                credential = ""
            })
            Spacer(Modifier.height(8.dp))
            SettingRow("仅使用 TURN 中继", "需要已配置 TURN；会牺牲延迟换取连通性") {
                Switch(
                    checked = settings.iceRelayOnly,
                    onCheckedChange = { value -> app.settings.update { s -> s.copy(iceRelayOnly = value) } }
                )
            }
            SettingRow("自动重连", "连接中断后尝试恢复") {
                Switch(
                    checked = settings.autoReconnect,
                    onCheckedChange = { value -> app.settings.update { s -> s.copy(autoReconnect = value) } }
                )
            }
            SettingRow("局域网发现", "IPv4/IPv6 多播，更改后立即生效") {
                Switch(
                    checked = settings.lanDiscovery,
                    onCheckedChange = { value ->
                        app.settings.update { s -> s.copy(lanDiscovery = value) }
                        if (value) app.discovery.start() else app.discovery.stop()
                    }
                )
            }
        }

        Spacer(Modifier.height(14.dp))
        SectionTitle("安全与隐私")
        SurfaceCard {
            SettingRow("配对时需要确认", "关闭后知道有效配对码的设备可直接加入") {
                Switch(
                    checked = settings.requireConfirmation,
                    onCheckedChange = { value -> app.settings.update { s -> s.copy(requireConfirmation = value) } }
                )
            }
            Text(
                "设备身份由 Android Keystore 中的 ECDSA P-256 密钥确定；信令认证使用一次性挑战签名，媒体使用 DTLS-SRTP。" +
                    "LinkView 默认不录制、不上传屏幕内容。",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.padding(top = 6.dp)
            )
        }

        Spacer(Modifier.height(14.dp))
        SectionTitle("诊断")
        SurfaceCard {
            Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                SecondaryButton("网络诊断", { onNavigate("diagnostics") }, Modifier.weight(1f))
                SecondaryButton("日志", { onNavigate("logs") }, Modifier.weight(1f))
            }
        }

        Spacer(Modifier.height(14.dp))
        InfoBanner(
            "Android 客户端的构建需要 JDK 17 与 Android SDK 35。本交付环境未安装 Android 工具链，" +
                "因此 APK 构建与真机互通验证未在此环境执行，详见项目状态报告。"
        )

        Spacer(Modifier.height(14.dp))
        SurfaceCard {
            Text("关于 LinkView", style = MaterialTheme.typography.titleSmall)
            Text(
                "版本 ${BuildConfig.VERSION_NAME} · 协议 v1 · 设备 ${app.identity.identity.deviceId.take(16)}…",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.padding(top = 5.dp)
            )
            Text(
                "WebRTC：io.github.webrtc-sdk 137.7151.05（BSD） · 仅视频，未实现音频。",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.padding(top = 4.dp)
            )
        }
        Spacer(Modifier.height(24.dp))
    }
}
