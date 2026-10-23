package com.linkview.app.data

import android.content.Context
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import java.security.KeyPairGenerator
import java.security.KeyStore
import java.security.MessageDigest
import java.security.Signature
import java.security.spec.ECGenParameterSpec
import android.util.Base64

/**
 * 设备身份：ECDSA P-256 私钥保存在 Android Keystore，不可导出。
 * DeviceId 与 Windows 端一致：SHA-256(SPKI) 前 16 字节，前缀 "lv"。
 */
class IdentityStore(private val context: Context) {

    data class Identity(val deviceId: String, val publicKeyBase64: String)

    private val alias = "linkview.identity.v1"
    private val keyStore: KeyStore = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }

    val identity: Identity by lazy { ensureKey() }

    private fun ensureKey(): Identity {
        if (!keyStore.containsAlias(alias)) {
            val generator = KeyPairGenerator.getInstance(KeyProperties.KEY_ALGORITHM_EC, "AndroidKeyStore")
            val spec = KeyGenParameterSpec.Builder(
                alias,
                KeyProperties.PURPOSE_SIGN or KeyProperties.PURPOSE_VERIFY
            )
                .setAlgorithmParameterSpec(ECGenParameterSpec("secp256r1"))
                .setDigests(KeyProperties.DIGEST_SHA256)
                .build()
            generator.initialize(spec)
            generator.generateKeyPair()
        }

        val certificate = keyStore.getCertificate(alias)
            ?: error("Android Keystore 中没有设备证书")
        val publicBytes = certificate.publicKey.encoded
        val digest = MessageDigest.getInstance("SHA-256").digest(publicBytes)
        val deviceId = "lv" + digest.take(16).joinToString("") { "%02x".format(it) }
        return Identity(deviceId, Base64.encodeToString(publicBytes, Base64.NO_WRAP))
    }

    fun sign(data: ByteArray): ByteArray {
        val entry = keyStore.getEntry(alias, null) as KeyStore.PrivateKeyEntry
        val signature = Signature.getInstance("SHA256withECDSA")
        signature.initSign(entry.privateKey)
        signature.update(data)
        return signature.sign()
    }
}
