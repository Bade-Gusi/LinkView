package com.linkview.app.ui.screens

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.Add
import androidx.compose.material.icons.rounded.Link
import androidx.compose.material.icons.rounded.PhoneAndroid
import androidx.compose.material.icons.rounded.ScreenShare
import androidx.compose.material.icons.rounded.Wifi
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.produceState
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.linkview.app.LinkViewApplication
import com.linkview.app.diagnostics.InterfaceInfo
import com.linkview.app.session.SessionState
import com.linkview.app.session.SessionUiState
import com.linkview.app.ui.EmptyState
import com.linkview.app.ui.InfoBanner
import com.linkview.app.ui.LinkLogo
import com.linkview.app.ui.PrimaryButton
import com.linkview.app.ui.SecondaryButton
import com.linkview.app.ui.SectionTitle
import com.linkview.app.ui.StatusPill
import com.linkview.app.ui.SurfaceCard
import com.linkview.app.ui.stateDescription
import com.linkview.app.ui.stateLabelAndColor

@Composable
fun HomeScreen(
    app: LinkViewApplication,
    state: SessionUiState,
    onNavigate: (String) -> Unit
) {
    val settings by app.settings.settings.collectAsStateWithLifecycle()
    val discovered by app.discovery.devices.collectAsStateWithLifecycle()
    val interfaces by produceState<List<InterfaceInfo>>(initialValue = emptyList()) {
        value = app.diagnostics.interfaces()
    }
    val ipv4 = interfaces.count { it.family == "IPv4" && it.usable }
    val ipv6 = interfaces.count { it.family == "IPv6" && it.usable }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(horizontal = 20.dp, vertical = 8.dp)
    ) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically
        ) {
            LinkLogo(38.dp)
            Column(modifier = Modifier.weight(1f).padding(start = 11.dp)) {
                Text("LinkView", style = MaterialTheme.typography.headlineSmall)
                Text(
                    "点对点屏幕共享",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
            }
            StatusPill(state.state)
        }

        Spacer(Modifier.height(18.dp))

        SurfaceCard(modifier = Modifier.fillMaxWidth(), padding = 20.dp) {
            val (label, _) = stateLabelAndColor(state.state)
            Text(label, style = MaterialTheme.typography.headlineSmall)
            Text(
                stateDescription(state.state),
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.padding(top = 5.dp)
            )
            Spacer(Modifier.height(16.dp))
            when (state.state) {
                SessionState.Connected, SessionState.Sharing, SessionState.Connecting,
                SessionState.Negotiating, SessionState.Reconnecting, SessionState.AwaitingConsent,
                SessionState.Pairing, SessionState.Stopping -> {
                    PrimaryButton("打开会话", { onNavigate("session") }, Modifier.fillMaxWidth())
                }
                else -> {
                    Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                        PrimaryButton("创建连接", { onNavigate("pair") }, Modifier.weight(1f))
                        SecondaryButton("加入连接", { onNavigate("pair") }, Modifier.weight(1f))
                    }
                }
            }
        }

        Spacer(Modifier.height(14.dp))

        SurfaceCard {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Surface(
                    color = MaterialTheme.colorScheme.primaryContainer,
                    shape = MaterialTheme.shapes.medium,
                    modifier = Modifier.size(38.dp)
                ) {
                    Icon(
                        Icons.Rounded.PhoneAndroid,
                        contentDescription = null,
                        tint = MaterialTheme.colorScheme.primary,
                        modifier = Modifier.padding(9.dp)
                    )
                }
                Column(modifier = Modifier.weight(1f).padding(start = 11.dp)) {
                    Text(settings.deviceName, style = MaterialTheme.typography.titleMedium, maxLines = 1, overflow = TextOverflow.Ellipsis)
                    Text(
                        app.identity.identity.deviceId.take(18) + "…",
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant
                    )
                }
                Text("$ipv4 个 IPv4 · $ipv6 个 IPv6", style = MaterialTheme.typography.labelSmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
            HorizontalDivider(Modifier.padding(vertical = 12.dp), color = MaterialTheme.colorScheme.outlineVariant)
            Text("信令服务", style = MaterialTheme.typography.labelSmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
            Text(settings.signalingServer, style = MaterialTheme.typography.bodySmall)
            Text(
                "信令服务只交换配对与 SDP/ICE 信息，不转发屏幕画面。",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.padding(top = 4.dp)
            )
        }

        Spacer(Modifier.height(16.dp))
        SectionTitle("快速操作")
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            QuickAction("创建配对码", Icons.Rounded.Add, Modifier.weight(1f)) { onNavigate("pair") }
            QuickAction("加入会话", Icons.Rounded.Link, Modifier.weight(1f)) { onNavigate("pair") }
        }
        Spacer(Modifier.height(10.dp))
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            QuickAction("屏幕共享", Icons.Rounded.ScreenShare, Modifier.weight(1f)) { onNavigate("session") }
            QuickAction("网络诊断", Icons.Rounded.Wifi, Modifier.weight(1f)) { onNavigate("diagnostics") }
        }

        Spacer(Modifier.height(16.dp))
        SectionTitle("附近设备")
        SurfaceCard {
            if (discovered.isEmpty()) {
                EmptyState(
                    icon = Icons.Rounded.Wifi,
                    title = "附近没有发现设备",
                    text = "请确认双方在同一局域网，且路由器没有禁用多播。也可以直接使用配对码。"
                )
            } else {
                discovered.take(3).forEachIndexed { index, device ->
                    if (index > 0) HorizontalDivider(color = MaterialTheme.colorScheme.outlineVariant)
                    Row(
                        modifier = Modifier.fillMaxWidth().padding(vertical = 9.dp),
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        Column(modifier = Modifier.weight(1f)) {
                            Text(device.name, style = MaterialTheme.typography.titleSmall)
                            Text(
                                device.addresses.firstOrNull() ?: device.sourceAddress,
                                style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant
                            )
                        }
                        Text(device.type, style = MaterialTheme.typography.labelSmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                    }
                }
            }
        }

        Spacer(Modifier.height(14.dp))
        InfoBanner("默认不录制、不上传屏幕内容。共享时 Android 会显示系统前台服务通知，你可以随时在应用或通知中停止共享。")
        Spacer(Modifier.height(20.dp))
    }
}

@Composable
private fun QuickAction(
    title: String,
    icon: androidx.compose.ui.graphics.vector.ImageVector,
    modifier: Modifier = Modifier,
    onClick: () -> Unit
) {
    Surface(
        onClick = onClick,
        modifier = modifier,
        color = MaterialTheme.colorScheme.surface,
        shape = MaterialTheme.shapes.medium,
        border = androidx.compose.foundation.BorderStroke(1.dp, MaterialTheme.colorScheme.outlineVariant)
    ) {
        Column(modifier = Modifier.padding(14.dp)) {
            Icon(icon, contentDescription = null, tint = MaterialTheme.colorScheme.primary, modifier = Modifier.size(19.dp))
            Text(title, style = MaterialTheme.typography.titleSmall, modifier = Modifier.padding(top = 8.dp))
        }
    }
}
