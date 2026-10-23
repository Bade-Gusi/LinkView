package com.linkview.app.data

import kotlinx.serialization.SerialName
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.decodeFromJsonElement
import kotlinx.serialization.json.encodeToJsonElement
import kotlinx.serialization.json.jsonObject
import java.util.UUID

const val PROTOCOL_VERSION = 1
const val MAX_MESSAGE_BYTES = 64 * 1024

object ProtocolJson {
    val json = Json {
        ignoreUnknownKeys = true
        encodeDefaults = true
        explicitNulls = false
        coerceInputValues = true
    }
}

object MessageType {
    const val Hello = "hello"
    const val AuthChallenge = "authChallenge"
    const val AuthProof = "authProof"
    const val AuthOk = "authOk"
    const val PairCreate = "pairCreate"
    const val PairCreated = "pairCreated"
    const val PairJoin = "pairJoin"
    const val JoinRequest = "joinRequest"
    const val PairDecision = "pairDecision"
    const val PairJoined = "pairJoined"
    const val PeerLeft = "peerLeft"
    const val SdpOffer = "sdpOffer"
    const val SdpAnswer = "sdpAnswer"
    const val IceCandidate = "iceCandidate"
    const val SessionEnd = "sessionEnd"
    const val Ping = "ping"
    const val Pong = "pong"
    const val Error = "error"
}

@Serializable
data class Envelope(
    val v: Int = PROTOCOL_VERSION,
    val id: String = UUID.randomUUID().toString().replace("-", ""),
    val type: String,
    val sessionId: String? = null,
    val ts: Long = System.currentTimeMillis(),
    val payload: JsonElement
)

@Serializable
data class HelloPayload(
    val deviceId: String,
    val deviceName: String,
    val deviceType: String,
    val publicKey: String,
    val appVersion: String,
    val protocolVersion: Int
)

@Serializable data class AuthChallengePayload(val nonce: String, val expiresAtUnixMs: Long)
@Serializable data class AuthProofPayload(val signature: String, val clientTimestampUnixMs: Long)

@Serializable
data class DeviceSummary(
    val deviceId: String,
    val deviceName: String,
    val deviceType: String,
    val appVersion: String,
    val observedAddress: String? = null
)

@Serializable data class AuthOkPayload(val self: DeviceSummary, val heartbeatSeconds: Int)
@Serializable data class PairCreatePayload(val ttlSeconds: Int)
@Serializable data class PairCreatedPayload(val sessionId: String, val pairingCode: String, val expiresAtUnixMs: Long)
@Serializable data class PairJoinPayload(val pairingCode: String)
@Serializable data class JoinRequestPayload(val sessionId: String, val guest: DeviceSummary)
@Serializable data class PairDecisionPayload(val accept: Boolean, val reason: String? = null)
@Serializable data class PairJoinedPayload(val sessionId: String, val peer: DeviceSummary, val role: String)
@Serializable data class PeerLeftPayload(val reason: String)
@Serializable data class SdpPayload(val sdpType: String, val sdp: String)
@Serializable data class IceCandidatePayload(
    val candidate: String,
    val sdpMid: String? = null,
    val sdpMLineIndex: Int? = null,
    val usernameFragment: String? = null
)
@Serializable data class SessionEndPayload(val reason: String)
@Serializable data class PingPayload(val clientTimeMs: Long)
@Serializable data class PongPayload(val clientTimeMs: Long, val serverTimeMs: Long)
@Serializable data class ErrorPayload(val code: String, val message: String, val retryAfterMs: Int? = null)

inline fun <reified T> Envelope.decodePayload(): T? = try {
    ProtocolJson.json.decodeFromJsonElement<T>(payload)
} catch (_: Exception) {
    null
}

inline fun <reified T> envelope(type: String, payload: T, sessionId: String? = null): Envelope =
    Envelope(
        type = type,
        sessionId = sessionId,
        payload = ProtocolJson.json.encodeToJsonElement(payload)
    )

fun buildAuthCanonical(nonce: String, deviceId: String, timestamp: Long): ByteArray =
    "LinkView-Auth-v1\n$nonce\n$deviceId\n$timestamp".toByteArray(Charsets.UTF_8)
