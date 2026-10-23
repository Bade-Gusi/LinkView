package com.linkview.app.net

import android.content.Context
import android.net.wifi.WifiManager
import com.linkview.app.data.IdentityStore
import com.linkview.app.data.LogStore
import com.linkview.app.data.SettingsStore
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.serialization.Serializable
import com.linkview.app.data.ProtocolJson
import java.net.DatagramPacket
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.MulticastSocket
import java.net.NetworkInterface

data class DiscoveredDevice(
    val deviceId: String,
    val name: String,
    val type: String,
    val appVersion: String,
    val addresses: List<String>,
    val sourceAddress: String,
    val lastSeen: Long
)

/**
 * 局域网发现：与 Windows 端一致的 IPv4 多播发现，IPv6 多播为尽力而为。
 * 发现结果仅用于展示，真正的连接仍需一次性配对码，避免对局域网广播的盲目信任。
 */
class DiscoveryService(
    private val context: Context,
    private val settingsStore: SettingsStore,
    private val identityStore: IdentityStore,
    private val logs: LogStore,
    private val scope: CoroutineScope
) {
    companion object {
        private const val PORT = 45892
        private const val GROUP_V4 = "239.255.76.86"
        private const val GROUP_V6 = "ff12::4c56"
    }

    private val _devices = MutableStateFlow<List<DiscoveredDevice>>(emptyList())
    val devices: StateFlow<List<DiscoveredDevice>> = _devices
    private var lock: WifiManager.MulticastLock? = null
    private var receiveJob: Job? = null
    private var announceJob: Job? = null
    private var expiryJob: Job? = null
    private var socket: MulticastSocket? = null

    fun start() {
        if (receiveJob?.isActive == true) return
        if (!settingsStore.settings.value.lanDiscovery) return
        val wifi = context.applicationContext.getSystemService(WifiManager::class.java)
        lock = wifi?.createMulticastLock("linkview-discovery")?.apply {
            setReferenceCounted(false)
            acquire()
        }

        val created = try {
            MulticastSocket(null).apply {
                reuseAddress = true
                bind(InetSocketAddress(PORT))
                timeToLive = 1
                soTimeout = 1000
            }
        } catch (ex: Exception) {
            logs.warn("Discovery", "无法创建多播套接字：${ex.message}")
            return
        }
        socket = created

        val group4 = InetAddress.getByName(GROUP_V4)
        try {
            created.joinGroup(InetSocketAddress(group4, PORT), null)
        } catch (ex: Exception) {
            logs.warn("Discovery", "加入 IPv4 多播组失败：${ex.message}")
        }

        try {
            val group6 = InetAddress.getByName(GROUP_V6)
            NetworkInterface.getNetworkInterfaces().toList().forEach { nic ->
                try {
                    created.joinGroup(InetSocketAddress(group6, PORT), nic)
                } catch (_: Exception) {
                }
            }
        } catch (_: Exception) {
        }

        receiveJob = scope.launch(Dispatchers.IO) {
            val buffer = ByteArray(4096)
            while (isActive) {
                try {
                    val packet = DatagramPacket(buffer, buffer.size)
                    created.receive(packet)
                    if (packet.length in 1..4096) {
                        handleAnnouncement(packet.data.copyOf(packet.length), packet.address)
                    }
                } catch (_: java.net.SocketTimeoutException) {
                } catch (ex: Exception) {
                    if (isActive) logs.warn("Discovery", "接收发现包失败：${ex.message}")
                    delay(1000)
                }
            }
        }

        announceJob = scope.launch(Dispatchers.IO) {
            while (isActive) {
                try {
                    val payload = ProtocolJson.json.encodeToString(
                        Announcement.serializer(),
                        buildAnnouncement()
                    ).toByteArray(Charsets.UTF_8)
                    created.send(DatagramPacket(payload, payload.size, group4, PORT))
                    try {
                        created.send(
                            DatagramPacket(
                                payload,
                                payload.size,
                                InetAddress.getByName(GROUP_V6),
                                PORT
                            )
                        )
                    } catch (_: Exception) {
                    }
                } catch (ex: Exception) {
                    if (isActive) logs.warn("Discovery", "发送发现包失败：${ex.message}")
                }
                delay(2000)
            }
        }

        expiryJob = scope.launch {
            while (isActive) {
                delay(2000)
                val cutoff = System.currentTimeMillis() - 10_000
                _devices.value = _devices.value.filter { it.lastSeen >= cutoff }
            }
        }

        logs.info("Discovery", "局域网发现已启动")
    }

    fun stop() {
        receiveJob?.cancel()
        announceJob?.cancel()
        expiryJob?.cancel()
        socket?.close()
        socket = null
        lock?.release()
        lock = null
        _devices.value = emptyList()
        logs.info("Discovery", "局域网发现已停止")
    }

    private fun buildAnnouncement(): Announcement {
        val addresses = mutableListOf<String>()
        NetworkInterface.getNetworkInterfaces()?.toList()?.forEach { nic ->
            if (!nic.isUp || nic.isLoopback) return@forEach
            nic.inetAddresses.toList().forEach { address ->
                val host = address.hostAddress ?: return@forEach
                if (address.isLoopbackAddress || address.isLinkLocalAddress) return@forEach
                addresses.add(host.substringBefore('%'))
            }
        }

        return Announcement(
            v = 1,
            deviceId = identityStore.identity.deviceId,
            name = settingsStore.settings.value.deviceName,
            type = "android",
            appVersion = "0.9.0",
            port = PORT,
            addresses = addresses.distinct().take(8),
            capabilities = listOf("vp8", "send", "recv")
        )
    }

    private fun handleAnnouncement(bytes: ByteArray, source: InetAddress) {
        try {
            val ann = ProtocolJson.json.decodeFromString(Announcement.serializer(), String(bytes, Charsets.UTF_8))
            if (ann.v != 1 || ann.deviceId == identityStore.identity.deviceId) return
            val addresses = (ann.addresses + source.hostAddress.orEmpty())
                .filter { it.isNotBlank() }
                .map { it.substringBefore('%') }
                .distinct()
            val device = DiscoveredDevice(
                deviceId = ann.deviceId,
                name = ann.name,
                type = ann.type,
                appVersion = ann.appVersion,
                addresses = addresses,
                sourceAddress = source.hostAddress.orEmpty(),
                lastSeen = System.currentTimeMillis()
            )
            val current = _devices.value.toMutableList()
            val index = current.indexOfFirst { it.deviceId == device.deviceId }
            if (index >= 0) current[index] = device else current.add(device)
            _devices.value = current
        } catch (_: Exception) {
        }
    }

    @Serializable
    private data class Announcement(
        val v: Int,
        val deviceId: String,
        val name: String,
        val type: String,
        val appVersion: String,
        val port: Int,
        val addresses: List<String>,
        val capabilities: List<String>
    )
}
