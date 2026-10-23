package com.linkview.app.ui.screens

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.Refresh
import androidx.compose.material.icons.rounded.Wifi
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.linkview.app.LinkViewApplication
import com.linkview.app.ui.EmptyState
import com.linkview.app.ui.InfoBanner
import com.linkview.app.ui.PageHeader
import com.linkview.app.ui.SecondaryButton
import com.linkview.app.ui.SurfaceCard

@Composable
fun DevicesScreen(app: LinkViewApplication, onNavigate: (String) -> Unit) {
    val devices by app.discovery.devices.collectAsStateWithLifecycle()
    val settings by app.settings.settings.collectAsStateWithLifecycle()

    Column(modifier = Modifier.fillMaxSize().padding(horizontal = 20.dp, vertical = 8.dp)) {
        PageHeader(
            title = "设备中心",
            subtitle = "同一局域网内广播的 LinkView 设备",
            trailing = {
                SecondaryButton("刷新", {
                    app.discovery.stop()
                    if (settings.lanDiscovery) app.discovery.start()
                }, icon = Icons.Rounded.Refresh)
            }
        )
        Spacer(Modifier.height(14.dp))
        InfoBanner("发现结果只用于展示。连接必须使用对方显示的 6 位配对码，不会因为局域网广播就自动信任设备。")
        Spacer(Modifier.height(14.dp))

        if (devices.isEmpty()) {
            SurfaceCard(modifier = Modifier.fillMaxWidth()) {
                EmptyState(
                    icon = Icons.Rounded.Wifi,
                    title = "附近没有发现设备",
                    text = "若对方已开启 LinkView，请检查两端是否在同一网络、是否启用了客户端隔离，或路由器是否禁用了多播。",
                    action = {
                        SecondaryButton("使用配对码加入", { onNavigate("pair") })
                    }
                )
            }
        } else {
            LazyColumn(verticalArrangement = Arrangement.spacedBy(10.dp)) {
                items(devices, key = { it.deviceId }) { device ->
                    SurfaceCard(modifier = Modifier.fillMaxWidth(), padding = 14.dp) {
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            Column(modifier = Modifier.weight(1f)) {
                                Text(device.name, style = MaterialTheme.typography.titleMedium)
                                Text(
                                    device.addresses.joinToString(" · ").ifBlank { device.sourceAddress },
                                    style = MaterialTheme.typography.bodySmall,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant
                                )
                                Text(
                                    "${device.type} · ${device.appVersion}",
                                    style = MaterialTheme.typography.labelSmall,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant
                                )
                            }
                            TextButton(onClick = { onNavigate("pair") }) { Text("使用配对码") }
                        }
                    }
                }
            }
        }
    }
}
