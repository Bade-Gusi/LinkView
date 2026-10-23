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
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp
import com.linkview.app.LinkViewApplication
import com.linkview.app.diagnostics.CheckStatus
import com.linkview.app.diagnostics.DiagnosticCheck
import com.linkview.app.diagnostics.InterfaceInfo
import com.linkview.app.ui.InfoBanner
import com.linkview.app.ui.PageHeader
import com.linkview.app.ui.PrimaryButton
import com.linkview.app.ui.SectionTitle
import com.linkview.app.ui.SurfaceCard
import kotlinx.coroutines.launch

@Composable
fun DiagnosticsScreen(app: LinkViewApplication, onBack: () -> Unit) {
    val scope = rememberCoroutineScope()
    var checks by remember { mutableStateOf<List<DiagnosticCheck>>(emptyList()) }
    var interfaces by remember { mutableStateOf<List<InterfaceInfo>>(emptyList()) }
    var running by remember { mutableStateOf(false) }

    fun runChecks() {
        if (running) return
        running = true
        scope.launch {
            interfaces = app.diagnostics.interfaces()
            checks = app.diagnostics.run()
            running = false
        }
    }

    LaunchedEffect(Unit) {
        if (checks.isEmpty()) {
            running = true
            interfaces = app.diagnostics.interfaces()
            checks = app.diagnostics.run()
            running = false
        }
    }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(horizontal = 20.dp, vertical = 8.dp)
    ) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Column(modifier = Modifier.weight(1f)) {
                PageHeader("网络诊断", "真实检查本机网络、DNS、信令与 STUN")
            }
            TextButton(onClick = onBack) { Text("返回设置") }
        }
        Spacer(Modifier.height(12.dp))
        PrimaryButton("重新检查", { runChecks() }, Modifier.fillMaxWidth(), enabled = !running)
        Spacer(Modifier.height(12.dp))
        InfoBanner("STUN 可达不代表一定能穿透 NAT；最终以实际 ICE 协商结果为准。所有结果均来自本机真实请求。")

        Spacer(Modifier.height(14.dp))
        if (running) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                CircularProgressIndicator(modifier = Modifier.size(18.dp), strokeWidth = 2.dp)
                Text("正在执行检查…", style = MaterialTheme.typography.bodySmall, modifier = Modifier.padding(start = 10.dp))
            }
            Spacer(Modifier.height(10.dp))
        }

        checks.forEach { check ->
            SurfaceCard(modifier = Modifier.fillMaxWidth().padding(bottom = 8.dp), padding = 14.dp) {
                Row(verticalAlignment = Alignment.Top) {
                    Column(modifier = Modifier.weight(1f)) {
                        Text(check.name, style = MaterialTheme.typography.titleSmall)
                        Text(
                            check.category,
                            style = MaterialTheme.typography.labelSmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant
                        )
                        Text(
                            check.detail,
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                            modifier = Modifier.padding(top = 3.dp)
                        )
                    }
                    Text(
                        check.status.label,
                        style = MaterialTheme.typography.labelMedium,
                        color = statusColor(check.status)
                    )
                }
            }
        }

        Spacer(Modifier.height(14.dp))
        SectionTitle("本机网络接口")
        SurfaceCard {
            if (interfaces.isEmpty()) {
                Text("未读取到网络接口", style = MaterialTheme.typography.bodySmall)
            } else {
                interfaces.forEachIndexed { index, info ->
                    if (index > 0) HorizontalDivider(color = MaterialTheme.colorScheme.outlineVariant)
                    Row(modifier = Modifier.fillMaxWidth().padding(vertical = 8.dp)) {
                        Column(modifier = Modifier.weight(1f)) {
                            Text(info.name, style = MaterialTheme.typography.titleSmall)
                            Text(info.address, style = MaterialTheme.typography.bodySmall)
                        }
                        Column(horizontalAlignment = Alignment.End) {
                            Text("${info.family} · ${info.scope}", style = MaterialTheme.typography.labelSmall)
                            Text(
                                if (info.usable) "可用" else "不用于连接",
                                style = MaterialTheme.typography.labelSmall,
                                color = if (info.usable) statusColor(CheckStatus.Passed) else MaterialTheme.colorScheme.onSurfaceVariant
                            )
                        }
                    }
                }
            }
        }
        Spacer(Modifier.height(24.dp))
    }
}

private fun statusColor(status: CheckStatus): Color = when (status) {
    CheckStatus.Passed -> Color(0xFF2E9E6F)
    CheckStatus.Warning -> Color(0xFFD08A1E)
    CheckStatus.Failed -> Color(0xFFC23B3B)
    CheckStatus.Skipped -> Color(0xFF8493A5)
}
