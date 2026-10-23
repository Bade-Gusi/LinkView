package com.linkview.app.ui.screens

import androidx.compose.ui.unit.sp

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
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.ContentCopy
import androidx.compose.material.icons.rounded.Link
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalClipboardManager
import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.linkview.app.LinkViewApplication
import com.linkview.app.session.SessionState
import com.linkview.app.session.SessionUiState
import com.linkview.app.ui.CodeDisplay
import com.linkview.app.ui.ErrorBanner
import com.linkview.app.ui.InfoBanner
import com.linkview.app.ui.PageHeader
import com.linkview.app.ui.PrimaryButton
import com.linkview.app.ui.SecondaryButton
import com.linkview.app.ui.Segmented
import com.linkview.app.ui.StatusPill
import com.linkview.app.ui.SurfaceCard
import kotlinx.coroutines.delay

@Composable
fun PairScreen(
    app: LinkViewApplication,
    state: SessionUiState,
    onNavigate: (String) -> Unit
) {
    val settings by app.settings.settings.collectAsStateWithLifecycle()
    var mode by rememberSaveable { mutableIntStateOf(0) }
    var joinCode by rememberSaveable { mutableStateOf("") }
    var countdown by remember { mutableStateOf("") }
    val clipboard = LocalClipboardManager.current

    LaunchedEffect(state.pairingExpiresAt) {
        val expiresAt = state.pairingExpiresAt
        if (expiresAt != null) while (true) {
            val remaining = expiresAt - System.currentTimeMillis()
            countdown = if (remaining <= 0) {
                "已过期"
            } else {
                val minutes = remaining / 60000
                val seconds = (remaining % 60000) / 1000
                "%02d:%02d 后失效".format(minutes, seconds)
            }
            delay(1000)
        }
    }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(horizontal = 20.dp, vertical = 8.dp)
    ) {
        PageHeader(
            title = "连接中心",
            subtitle = "通过一次性配对码建立加密会话",
            trailing = { StatusPill(state.state) }
        )
        Spacer(Modifier.height(14.dp))

        Segmented(
            options = listOf("创建连接", "加入连接"),
            selectedIndex = mode,
            onSelected = { mode = it }
        )

        Spacer(Modifier.height(14.dp))

        if (mode == 0) {
            SurfaceCard {
                Text("让另一台设备加入你的会话", style = MaterialTheme.typography.headlineSmall)
                Text(
                    "生成一次性配对码并发给对方。对方确认后会话即建立，配对码使用一次后失效。",
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    modifier = Modifier.padding(top = 5.dp, bottom = 14.dp)
                )

                if (state.pairingCode == null) {
                    Text(
                        if (state.state == SessionState.Pairing) "正在向信令服务申请配对码…" else "尚未生成配对码",
                        style = MaterialTheme.typography.bodyMedium,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        modifier = Modifier.padding(bottom = 10.dp)
                    )
                    PrimaryButton(
                        text = "生成配对码",
                        onClick = { app.session.createPairing() },
                        enabled = state.state != SessionState.Pairing,
                        modifier = Modifier.fillMaxWidth()
                    )
                } else {
                    Column(modifier = Modifier.fillMaxWidth()) {
                        Text("配对码", style = MaterialTheme.typography.labelMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
                        CodeDisplay(state.pairingCode)
                        Text(countdown, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                        Spacer(Modifier.height(12.dp))
                        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                            SecondaryButton("复制配对码", {
                                clipboard.setText(AnnotatedString(state.pairingCode))
                            }, icon = Icons.Rounded.ContentCopy)
                            SecondaryButton("取消", { app.session.cancelPairing() })
                        }
                    }
                }
            }

            Spacer(Modifier.height(12.dp))
            InfoBanner("收到连接请求时会在应用内弹出确认。未经你允许，任何设备都不能加入会话。")
        } else {
            SurfaceCard {
                Text("加入对方的会话", style = MaterialTheme.typography.headlineSmall)
                Text(
                    "输入对方设备上显示的 6 位配对码。连接成功后将自动建立加密的点对点媒体通道。",
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    modifier = Modifier.padding(top = 5.dp, bottom = 14.dp)
                )
                OutlinedTextField(
                    value = joinCode,
                    onValueChange = { raw ->
                        joinCode = raw.filter { it.isDigit() }.take(6)
                    },
                    label = { Text("6 位配对码") },
                    singleLine = true,
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.NumberPassword),
                    textStyle = MaterialTheme.typography.headlineSmall.copy(letterSpacing = 4.sp),
                    modifier = Modifier.fillMaxWidth(),
                    isError = state.error != null
                )
                Spacer(Modifier.height(12.dp))
                PrimaryButton(
                    text = "加入连接",
                    onClick = { app.session.join(joinCode) },
                    enabled = joinCode.length == 6 && state.state !in setOf(SessionState.Pairing, SessionState.AwaitingConsent),
                    modifier = Modifier.fillMaxWidth(),
                    icon = Icons.Rounded.Link
                )
                Text(
                    "当前状态：${state.infoMessage ?: state.state.name}",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    modifier = Modifier.padding(top = 10.dp)
                )
            }
        }

        Spacer(Modifier.height(12.dp))
        ErrorBanner(state.error)

        Spacer(Modifier.height(12.dp))
        SurfaceCard {
            Text("信令服务", style = MaterialTheme.typography.titleSmall)
            Text(
                settings.signalingServer,
                style = MaterialTheme.typography.bodySmall,
                modifier = Modifier.padding(top = 5.dp)
            )
            Text(
                "信令服务只交换配对、SDP 与 ICE 信息，不转发屏幕画面。跨网络连接需要双方都能访问该地址。",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.padding(top = 5.dp)
            )
            TextButton(onClick = { onNavigate("settings") }) { Text("修改服务地址") }
        }
        Spacer(Modifier.height(20.dp))
    }
}
