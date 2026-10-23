package com.linkview.app.diagnostics

import android.content.Context
import com.linkview.app.data.LogStore
import com.linkview.app.data.SettingsStore
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import okhttp3.OkHttpClient
import okhttp3.Request
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.NetworkInterface
import java.net.SocketTimeoutException
import java.net.URI
import java.security.SecureRandom
import java.util.concurrent.TimeUnit

enum class CheckStatus(val label: String) {
    Passed("通过"), Warning("警告"), Failed("失败"), Skipped("未检查")
}

data class DiagnosticCheck(
    val name: String,
    val category: String,
    val status: CheckStatus,
    val detail: String,
    val durationMs: Long = 0
)

data class InterfaceInfo(
    val name: String,
    val address: String,
    val family: String,
    val scope: String,
    val usable: Boolean
)

class Diagnostics(
    private val context: Context,
    private val settingsStore: SettingsStore,
    private val logs: LogStore
) {
    suspend fun interfaces(): List<InterfaceInfo> = withContext(Dispatchers.IO) { collectInterfaces() }

    private fun collectInterfaces(): List<InterfaceInfo> {
        val result = mutableListOf<InterfaceInfo>()
        NetworkInterface.getNetworkInterfaces()?.toList()?.forEach { nic ->
            if (!nic.isUp || nic.isLoopback) return@forEach
            nic.inetAddresses.toList().forEach { address ->
                val host = address.hostAddress ?: return@forEach
                if (host.contains(':')) {
                    val normalized = host.substringBefore('%')
                    result.add(
                        InterfaceInfo(
                            name = nic.name,
                            address = normalized,
                            family = "IPv6",
                            scope = ipv6Scope(address),
                            usable = !address.isLinkLocalAddress && !address.isLoopbackAddress
                        )
                    )
                } else {
                    result.add(
                        InterfaceInfo(
                            name = nic.name,
                            address = host,
                            family = "IPv4",
                            scope = ipv4Scope(address),
                            usable = !address.isLoopbackAddress && !address.isLinkLocalAddress
                        )
                    )
                }
            }
        }
        return result
    }

    suspend fun run(): List<DiagnosticCheck> = withContext(Dispatchers.IO) {
        val checks = mutableListOf<DiagnosticCheck>()
        var start = System.currentTimeMillis()
        val ifaces = collectInterfaces()
        val v4 = ifaces.filter { it.family == "IPv4" && it.usable }
        val v6 = ifaces.filter { it.family == "IPv6" && it.usable }
        checks.add(
            DiagnosticCheck(
                "IPv4 地址", "本机网络",
                if (v4.isNotEmpty()) CheckStatus.Passed else CheckStatus.Failed,
                if (v4.isEmpty()) "未找到可用 IPv4 地址" else v4.joinToString(" · ") { "${it.address} (${it.name})" },
                System.currentTimeMillis() - start
            )
        )
        start = System.currentTimeMillis()
        checks.add(
            DiagnosticCheck(
                "IPv6 地址", "本机网络",
                if (v6.isNotEmpty()) CheckStatus.Passed else CheckStatus.Warning,
                if (v6.isEmpty()) "未找到全局 IPv6 地址，不影响 IPv4 连接" else v6.joinToString(" · ") { it.address },
                System.currentTimeMillis() - start
            )
        )

        val server = settingsStore.settings.value.signalingServer
        start = System.currentTimeMillis()
        checks.add(checkSignaling(server, System.currentTimeMillis() - start))

        settingsStore.settings.value.stunServers
            .split('\n', ',')
            .map { it.trim() }
            .filter { it.isNotEmpty() }
            .forEach { stun ->
                start = System.currentTimeMillis()
                checks.add(probeStun(stun, System.currentTimeMillis() - start))
            }

        start = System.currentTimeMillis()
        val turn = settingsStore.settings.value
        checks.add(
            DiagnosticCheck(
                "TURN 配置", "NAT 穿透",
                if (turn.turnUri.isBlank()) CheckStatus.Warning else CheckStatus.Passed,
                if (turn.turnUri.isBlank()) "未配置 TURN；对称 NAT 下可能无法跨网络连接" else "已配置 ${turn.turnUri}",
                System.currentTimeMillis() - start
            )
        )

        checks.add(
            DiagnosticCheck(
                "屏幕采集授权", "权限",
                if (android.os.Build.VERSION.SDK_INT >= 29) CheckStatus.Passed else CheckStatus.Warning,
                "开始共享时会显示系统 MediaProjection 授权对话框",
                0
            )
        )
        checks
    }

    private fun checkSignaling(serverUri: String, elapsed: Long): DiagnosticCheck {
        return try {
            val ws = URI(serverUri)
            if (ws.scheme != "ws" && ws.scheme != "wss") {
                return DiagnosticCheck("信令服务", "信令", CheckStatus.Failed, "地址格式非法", elapsed)
            }
            val scheme = if (ws.scheme == "wss") "https" else "http"
            val port = if (ws.port > 0) ws.port else if (ws.scheme == "wss") 443 else 80
            val health = URI("$scheme://${ws.host}:$port/healthz").toURL()
            val client = OkHttpClient.Builder().callTimeout(5, TimeUnit.SECONDS).build()
            client.newCall(Request.Builder().url(health).build()).execute().use { response ->
                if (response.isSuccessful) {
                    DiagnosticCheck("信令服务", "信令", CheckStatus.Passed, "可达 ${ws.authority}", elapsed)
                } else {
                    DiagnosticCheck("信令服务", "信令", CheckStatus.Failed, "HTTP ${response.code}", elapsed)
                }
            }
        } catch (ex: Exception) {
            DiagnosticCheck("信令服务", "信令", CheckStatus.Failed, "无法访问：${ex.message}", elapsed)
        }
    }

    private suspend fun probeStun(server: String, elapsed: Long): DiagnosticCheck = withContext(Dispatchers.IO) {
        val text = server.removePrefix("stun:")
        val host: String
        val port: Int
        if (text.contains(':')) {
            host = text.substringBeforeLast(':')
            port = text.substringAfterLast(':').toIntOrNull() ?: 3478
        } else {
            host = text
            port = 3478
        }

        try {
            val address = InetAddress.getByName(host)
            DatagramSocket().use { socket ->
                socket.soTimeout = 3000
                val tx = ByteArray(12).also { SecureRandom().nextBytes(it) }
                val request = ByteArray(20)
                request[1] = 0x01
                request[4] = 0x21
                request[5] = 0x12
                request[6] = 0xA4.toByte()
                request[7] = 0x42
                tx.copyInto(request, 8)
                val started = System.currentTimeMillis()
                socket.send(DatagramPacket(request, request.size, InetSocketAddress(address, port)))
                val buffer = ByteArray(1500)
                val packet = DatagramPacket(buffer, buffer.size)
                socket.receive(packet)
                val rtt = System.currentTimeMillis() - started
                if (packet.length >= 20 && buffer[0] == 0x01.toByte() && buffer[1] == 0x01.toByte()) {
                    DiagnosticCheck("STUN：$server", "NAT 穿透", CheckStatus.Passed, "RTT ${rtt}ms", elapsed)
                } else {
                    DiagnosticCheck("STUN：$server", "NAT 穿透", CheckStatus.Failed, "响应不是 STUN Binding", elapsed)
                }
            }
        } catch (ex: SocketTimeoutException) {
            DiagnosticCheck("STUN：$server", "NAT 穿透", CheckStatus.Failed, "3 秒内没有响应", elapsed)
        } catch (ex: Exception) {
            DiagnosticCheck("STUN：$server", "NAT 穿透", CheckStatus.Failed, ex.message ?: "不可达", elapsed)
        }
    }

    fun exportText(): String {
        val settings = settingsStore.settings.value
        val ifaces = collectInterfaces()
        return buildString {
            appendLine("LinkView Android 诊断报告")
            appendLine("时间：${java.text.SimpleDateFormat("yyyy-MM-dd HH:mm:ss", java.util.Locale.getDefault()).format(java.util.Date())}")
            appendLine("设备：${settings.deviceName}")
            appendLine("信令服务：${settings.signalingServer}")
            appendLine("网络偏好：${if (settings.iceRelayOnly) "仅中继" else "直连优先"}")
            appendLine("STUN：${settings.stunServers.ifBlank { "未配置" }}")
            appendLine("TURN：${settings.turnUri.ifBlank { "未配置" }}")
            appendLine()
            appendLine("网络接口：")
            ifaces.forEach { appendLine("- ${it.name} ${it.family} ${it.address} (${it.scope}) ${if (it.usable) "可用" else "不用于连接"}") }
            appendLine()
            appendLine("最近日志：")
            logs.entries.value.takeLast(150).forEach {
                appendLine("${java.text.SimpleDateFormat("HH:mm:ss", java.util.Locale.getDefault()).format(java.util.Date(it.time))} ${it.level} ${it.tag} | ${it.message}")
            }
        }
    }

    private fun ipv4Scope(address: InetAddress): String = when {
        address.isLoopbackAddress -> "回环"
        address.isLinkLocalAddress -> "链路本地"
        address.isSiteLocalAddress -> "私网"
        else -> "公网"
    }

    private fun ipv6Scope(address: InetAddress): String {
        if (address.isLoopbackAddress) return "回环"
        if (address.isLinkLocalAddress) return "链路本地"
        val bytes = address.address
        if (bytes.isNotEmpty() && (bytes[0].toInt() and 0xFE) == 0xFC) return "ULA"
        return "全局"
    }
}
