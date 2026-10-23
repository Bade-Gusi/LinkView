package com.linkview.app.ui.screens

import android.content.Intent
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
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.linkview.app.LinkViewApplication
import com.linkview.app.ui.InfoBanner
import com.linkview.app.ui.PageHeader
import com.linkview.app.ui.SecondaryButton
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

@Composable
fun LogsScreen(app: LinkViewApplication, onBack: () -> Unit) {
    val entries by app.logs.entries.collectAsStateWithLifecycle()
    val context = LocalContext.current

    Column(modifier = Modifier.fillMaxSize().padding(horizontal = 20.dp, vertical = 8.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Column(modifier = Modifier.weight(1f)) {
                PageHeader("日志与诊断", "已在写入前脱敏，不包含屏幕内容或凭据")
            }
            TextButton(onClick = onBack) { Text("返回设置") }
        }
        Spacer(Modifier.height(10.dp))
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            SecondaryButton("分享诊断报告", {
                val report = app.diagnostics.exportText()
                val intent = Intent(Intent.ACTION_SEND).apply {
                    type = "text/plain"
                    putExtra(Intent.EXTRA_SUBJECT, "LinkView 诊断报告")
                    putExtra(Intent.EXTRA_TEXT, report)
                }
                context.startActivity(Intent.createChooser(intent, "分享诊断报告"))
            }, Modifier.weight(1f))
            SecondaryButton("清空日志", { app.logs.clear() }, Modifier.weight(1f))
        }
        Spacer(Modifier.height(10.dp))
        InfoBanner("日志用于排障。TURN 凭据、密码、令牌与配对码字段在写入前会被替换为 ***。")

        Spacer(Modifier.height(10.dp))
        LazyColumn(modifier = Modifier.fillMaxSize()) {
            items(entries.asReversed()) { entry ->
                Row(modifier = Modifier.fillMaxWidth().padding(vertical = 7.dp)) {
                    Text(
                        SimpleDateFormat("HH:mm:ss", Locale.getDefault()).format(Date(entry.time)),
                        style = MaterialTheme.typography.labelSmall,
                        modifier = Modifier.padding(end = 8.dp)
                    )
                    Column(modifier = Modifier.weight(1f)) {
                        Text(
                            "${levelLabel(entry.level)} · ${entry.tag}",
                            style = MaterialTheme.typography.labelSmall,
                            color = levelColor(entry.level)
                        )
                        Text(entry.message, style = MaterialTheme.typography.bodySmall)
                    }
                }
                HorizontalDivider(color = MaterialTheme.colorScheme.outlineVariant)
            }
        }
    }
}

private fun levelLabel(level: String) = level

private fun levelColor(level: String): Color = when (level) {
    "ERROR" -> Color(0xFFC23B3B)
    "WARN" -> Color(0xFFD08A1E)
    else -> Color(0xFF2F6FEB)
}
