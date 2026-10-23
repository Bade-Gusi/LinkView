package com.linkview.app.ui.screens

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.Link
import androidx.compose.material.icons.rounded.ScreenShare
import androidx.compose.material.icons.rounded.Stop
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.key
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.linkview.app.LinkViewApplication
import com.linkview.app.session.SessionState
import com.linkview.app.session.SessionUiState
import com.linkview.app.ui.DangerButton
import com.linkview.app.ui.EmptyState
import com.linkview.app.ui.ErrorBanner
import com.linkview.app.ui.InfoBanner
import com.linkview.app.ui.PageHeader
import com.linkview.app.ui.PrimaryButton
import com.linkview.app.ui.SecondaryButton
import com.linkview.app.ui.SectionTitle
import com.linkview.app.ui.StatTile
import com.linkview.app.ui.StatusPill
import com.linkview.app.ui.SurfaceCard
import org.webrtc.RendererCommon
import org.webrtc.SurfaceViewRenderer

@Composable
fun SessionScreen(
    app: LinkViewApplication,
    state: SessionUiState,
    onRequestScreenShare: () -> Unit,
    onStopScreenShare: () -> Unit,
    onDisconnect: () -> Unit,
    onNavigate: (String) -> Unit
) {
    val active = state.state in setOf(
        SessionState.Negotiating,
        SessionState.Connecting,
        SessionState.Connected,
        SessionState.Sharing,
        SessionState.Reconnecting
    )

    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(horizontal = 20.dp, vertical = 8.dp)
    ) {
        PageHeader(
            title = "屏幕共享",
            subtitle = state.peer?.deviceName ?: "尚未连接",
            trailing = { StatusPill(state.state) }
        )
        Spacer(Modifier.height(14.dp))

        if (!active) {
            SurfaceCard(modifier = Modifier.fillMaxWidth()) {
                EmptyState(
                    icon = Icons.Rounded.Link,
                    title = "还没有活动会话",
                    text = "先通过配对码与另一台设备建立加密连接，然后即可接收或共享屏幕。",
                    action = { PrimaryButton("前往连接中心", { onNavigate("pair") }) }
                )
            }
            Spacer(Modifier.height(14.dp))
            InfoBanner("Android 端使用系统 MediaProjection 采集。开始共享时会出现系统授权与前台服务通知，停止共享会立即释放采集。")
            return@Column
        }

        val rtc = app.session.currentRtc()
        val rendererRef = remember { mutableStateOf<SurfaceViewRenderer?>(null) }

        Box(
            modifier = Modifier
                .fillMaxWidth()
                .height(260.dp)
                .background(Color(0xFF080B10), RoundedCornerShape(16.dp))
                .padding(1.dp),
            contentAlignment = Alignment.Center
        ) {
            if (rtc != null) {
                key(rtc) {
                    AndroidView(
                        modifier = Modifier.fillMaxSize(),
                        factory = { context ->
                            SurfaceViewRenderer(context).apply {
                                init(rtc.eglBase.eglBaseContext, null)
                                setScalingType(RendererCommon.ScalingType.SCALE_ASPECT_FIT)
                                setEnableHardwareScaler(true)
                                setMirror(false)
                                rendererRef.value = this
                                rtc.attachRemoteRenderer(this)
                            }
                        }
                    )
                    DisposableEffect(rtc) {
                        onDispose {
                            rtc.detachRemoteRenderer()
                            rendererRef.value?.release()
                            rendererRef.value = null
                        }
                    }
                }
            }
            if (state.stats.recvFps == 0) {
                Column(horizontalAlignment = Alignment.CenterHorizontally) {
                    Text("等待对方画面", color = Color(0xCCFFFFFF), style = MaterialTheme.typography.titleSmall)
                    Text(
                        "连接成功但尚未收到视频帧时会保持此状态，不会伪装成已接收。",
                        color = Color(0x88FFFFFF),
                        style = MaterialTheme.typography.bodySmall,
                        modifier = Modifier.padding(top = 4.dp, start = 24.dp, end = 24.dp)
                    )
                }
            }
        }

        Spacer(Modifier.height(12.dp))
        ErrorBanner(state.error)
        if (!state.infoMessage.isNullOrBlank()) {
            Spacer(Modifier.height(8.dp))
            InfoBanner(state.infoMessage)
        }

        Spacer(Modifier.height(14.dp))
        SectionTitle("实时指标")
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            StatTile("发送", state.stats.sentFps.toString(), "fps", Modifier.weight(1f))
            StatTile("接收", state.stats.recvFps.toString(), "fps", Modifier.weight(1f))
            StatTile("RTT", state.stats.rttMs?.toInt()?.toString() ?: "不支持", "ms", Modifier.weight(1f))
        }
        Spacer(Modifier.height(10.dp))
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            StatTile("发送码率", state.stats.sentKbps.toInt().toString(), "kbps", Modifier.weight(1f))
            StatTile("接收码率", state.stats.recvKbps.toInt().toString(), "kbps", Modifier.weight(1f))
            StatTile("丢包", state.stats.packetsLost?.toString() ?: "不支持", "", Modifier.weight(1f))
        }
        Spacer(Modifier.height(10.dp))
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            StatTile("编码耗时", if (state.stats.encodeMs > 0) "%.1f".format(state.stats.encodeMs) else "—", "ms", Modifier.weight(1f))
            StatTile("解码耗时", if (state.stats.decodeMs > 0) "%.1f".format(state.stats.decodeMs) else "—", "ms", Modifier.weight(1f))
            StatTile("编码格式", state.stats.codec, "", Modifier.weight(1f))
        }
        Spacer(Modifier.height(10.dp))
        Text(
            "发送 ${state.stats.sendWidth}×${state.stats.sendHeight} · 接收 ${state.stats.recvWidth}×${state.stats.recvHeight}",
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant
        )

        Spacer(Modifier.height(16.dp))
        SectionTitle("共享控制")
        if (state.isSharing) {
            DangerButton("停止共享", onStopScreenShare, Modifier.fillMaxWidth())
        } else {
            PrimaryButton(
                "共享我的屏幕",
                onRequestScreenShare,
                Modifier.fillMaxWidth(),
                icon = Icons.Rounded.ScreenShare
            )
        }
        Spacer(Modifier.height(10.dp))
        SecondaryButton("断开连接", onDisconnect, Modifier.fillMaxWidth())
        Spacer(Modifier.height(10.dp))
        InfoBanner("屏幕采集受 Android 系统授权控制。系统停止共享或用户从通知停止后，应用会立即更新状态并释放编码资源。")
        Spacer(Modifier.height(20.dp))
    }
}
