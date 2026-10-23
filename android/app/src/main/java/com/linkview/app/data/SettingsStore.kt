package com.linkview.app.data

import android.content.Context
import android.os.Build
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Base64
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import java.security.KeyStore
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

enum class ThemeMode { System, Light, Dark }

enum class QualityPreset(val label: String) {
    Auto("自动"),
    Smooth("流畅"),
    Balanced("均衡"),
    High("高清"),
    Ultra("超清")
}

data class AppSettings(
    val deviceName: String = Build.MODEL.ifBlank { "Android 设备" },
    val signalingServer: String = "ws://10.0.2.2:5180/ws",
    val theme: ThemeMode = ThemeMode.System,
    val quality: QualityPreset = QualityPreset.Balanced,
    val maxWidth: Int = 1280,
    val maxHeight: Int = 720,
    val targetFps: Int = 30,
    val targetBitrateKbps: Int = 2500,
    val stunServers: String = "",
    val turnUri: String = "",
    val turnUsername: String = "",
    val iceRelayOnly: Boolean = false,
    val autoReconnect: Boolean = true,
    val requireConfirmation: Boolean = true,
    val lanDiscovery: Boolean = true,
    val animations: Boolean = true
)

class SettingsStore(context: Context) {
    private val prefs = context.getSharedPreferences("linkview.settings", Context.MODE_PRIVATE)
    private val secure = SecureCredentialStore(context)
    private val _settings = MutableStateFlow(load())
    val settings: StateFlow<AppSettings> = _settings

    private fun load(): AppSettings = AppSettings(
        deviceName = prefs.getString("deviceName", null) ?: Build.MODEL.ifBlank { "Android 设备" },
        signalingServer = prefs.getString("signalingServer", null) ?: "ws://10.0.2.2:5180/ws",
        theme = runCatching {
            ThemeMode.valueOf(prefs.getString("theme", "System")!!)
        }.getOrDefault(ThemeMode.System),
        quality = runCatching {
            QualityPreset.valueOf(prefs.getString("quality", "Balanced")!!)
        }.getOrDefault(QualityPreset.Balanced),
        maxWidth = prefs.getInt("maxWidth", 1280),
        maxHeight = prefs.getInt("maxHeight", 720),
        targetFps = prefs.getInt("targetFps", 30),
        targetBitrateKbps = prefs.getInt("targetBitrateKbps", 2500),
        stunServers = prefs.getString("stunServers", "") ?: "",
        turnUri = prefs.getString("turnUri", "") ?: "",
        turnUsername = prefs.getString("turnUsername", "") ?: "",
        iceRelayOnly = prefs.getBoolean("iceRelayOnly", false),
        autoReconnect = prefs.getBoolean("autoReconnect", true),
        requireConfirmation = prefs.getBoolean("requireConfirmation", true),
        lanDiscovery = prefs.getBoolean("lanDiscovery", true),
        animations = prefs.getBoolean("animations", true)
    )

    fun update(transform: (AppSettings) -> AppSettings) {
        val next = transform(_settings.value)
        prefs.edit()
            .putString("deviceName", next.deviceName)
            .putString("signalingServer", next.signalingServer)
            .putString("theme", next.theme.name)
            .putString("quality", next.quality.name)
            .putInt("maxWidth", next.maxWidth)
            .putInt("maxHeight", next.maxHeight)
            .putInt("targetFps", next.targetFps)
            .putInt("targetBitrateKbps", next.targetBitrateKbps)
            .putString("stunServers", next.stunServers)
            .putString("turnUri", next.turnUri)
            .putString("turnUsername", next.turnUsername)
            .putBoolean("iceRelayOnly", next.iceRelayOnly)
            .putBoolean("autoReconnect", next.autoReconnect)
            .putBoolean("requireConfirmation", next.requireConfirmation)
            .putBoolean("lanDiscovery", next.lanDiscovery)
            .putBoolean("animations", next.animations)
            .apply()
        _settings.value = next
    }

    fun setTurnCredential(value: String?) {
        if (value.isNullOrBlank()) secure.remove("turnCredential") else secure.put("turnCredential", value)
    }

    fun turnCredential(): String? = secure.get("turnCredential")

    fun applyQualityPreset(preset: QualityPreset) {
        val width: Int
        val height: Int
        val fps: Int
        val bitrate: Int
        when (preset) {
            QualityPreset.Auto -> { width = 1280; height = 720; fps = 30; bitrate = 2500 }
            QualityPreset.Smooth -> { width = 960; height = 540; fps = 24; bitrate = 1200 }
            QualityPreset.Balanced -> { width = 1280; height = 720; fps = 30; bitrate = 2500 }
            QualityPreset.High -> { width = 1920; height = 1080; fps = 30; bitrate = 5000 }
            QualityPreset.Ultra -> { width = 1920; height = 1080; fps = 60; bitrate = 9000 }
        }

        update {
            it.copy(
                quality = preset,
                maxWidth = width,
                maxHeight = height,
                targetFps = fps,
                targetBitrateKbps = bitrate
            )
        }
    }
}

/** 使用 Android Keystore 中的 AES-GCM 密钥加密 TURN 凭据。 */
private class SecureCredentialStore(context: Context) {
    private val prefs = context.getSharedPreferences("linkview.secrets", Context.MODE_PRIVATE)
    private val alias = "linkview.secrets.v1"
    private val keyStore = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }

    fun put(key: String, value: String) {
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.ENCRYPT_MODE, secretKey())
        val iv = cipher.iv
        val encrypted = cipher.doFinal(value.toByteArray(Charsets.UTF_8))
        val packed = ByteArray(iv.size + encrypted.size)
        iv.copyInto(packed)
        encrypted.copyInto(packed, iv.size)
        prefs.edit().putString(key, Base64.encodeToString(packed, Base64.NO_WRAP)).apply()
    }

    fun get(key: String): String? {
        val packed = prefs.getString(key, null) ?: return null
        return try {
            val bytes = Base64.decode(packed, Base64.NO_WRAP)
            val iv = bytes.copyOfRange(0, 12)
            val cipher = Cipher.getInstance("AES/GCM/NoPadding")
            cipher.init(Cipher.DECRYPT_MODE, secretKey(), GCMParameterSpec(128, iv))
            String(cipher.doFinal(bytes.copyOfRange(12, bytes.size)), Charsets.UTF_8)
        } catch (_: Exception) {
            null
        }
    }

    fun remove(key: String) = prefs.edit().remove(key).apply()

    private fun secretKey(): SecretKey {
        (keyStore.getEntry(alias, null) as? KeyStore.SecretKeyEntry)?.let { return it.secretKey }
        val generator = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore")
        generator.init(
            KeyGenParameterSpec.Builder(
                alias,
                KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT
            )
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .build()
        )
        return generator.generateKey()
    }
}
