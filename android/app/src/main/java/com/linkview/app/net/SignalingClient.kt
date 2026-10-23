package com.linkview.app.net

import android.util.Base64
import com.linkview.app.data.AuthChallengePayload
import com.linkview.app.data.AuthOkPayload
import com.linkview.app.data.AuthProofPayload
import com.linkview.app.data.DeviceSummary
import com.linkview.app.data.Envelope
import com.linkview.app.data.ErrorPayload
import com.linkview.app.data.HelloPayload
import com.linkview.app.data.IdentityStore
import com.linkview.app.data.LogStore
import com.linkview.app.data.MAX_MESSAGE_BYTES
import com.linkview.app.data.MessageType
import com.linkview.app.data.PROTOCOL_VERSION
import com.linkview.app.data.PingPayload
import com.linkview.app.data.ProtocolJson
import com.linkview.app.data.buildAuthCanonical
import com.linkview.app.data.decodePayload
import com.linkview.app.data.envelope
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.channels.BufferOverflow
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.asSharedFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeout
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.Response
import okhttp3.WebSocket
import okhttp3.WebSocketListener
import java.util.concurrent.TimeUnit

/**
 * LinkView 信令客户端。完成设备身份挑战应答，并在已认证连接上收发会话协商消息。
 * 信令服务只交换 SDP/ICE，不承载媒体。
 */
class SignalingClient(
    private val identityStore: IdentityStore,
    private val logStore: LogStore,
    private val scope: CoroutineScope
) {
    private val client = OkHttpClient.Builder()
        .connectTimeout(15, TimeUnit.SECONDS)
        .readTimeout(0, TimeUnit.MILLISECONDS)
        .pingInterval(25, TimeUnit.SECONDS)
        .build()

    private var socket: WebSocket? = null
    private var authDeferred: CompletableDeferred<Unit>? = null
    private var pingJob: Job? = null

    private val _messages = MutableSharedFlow<Envelope>(
        replay = 0,
        extraBufferCapacity = 64,
        onBufferOverflow = BufferOverflow.DROP_OLDEST
    )
    val messages: SharedFlow<Envelope> = _messages.asSharedFlow()

    private val _closed = MutableSharedFlow<String?>(
        replay = 0,
        extraBufferCapacity = 4,
        onBufferOverflow = BufferOverflow.DROP_OLDEST
    )
    val closed: SharedFlow<String?> = _closed.asSharedFlow()

    var self: DeviceSummary? = null
        private set
    var sessionId: String? = null
    var serverUri: String = ""
        private set

    @Volatile
    var isAuthenticated: Boolean = false
        private set

    suspend fun connect(serverUri: String, deviceName: String): Result<Unit> = withContext(Dispatchers.IO) {
        if (isAuthenticated) return@withContext Result.success(Unit)
        if (!serverUri.startsWith("ws://") && !serverUri.startsWith("wss://")) {
            return@withContext Result.failure(IllegalArgumentException("信令地址必须以 ws:// 或 wss:// 开头"))
        }

        this@SignalingClient.serverUri = serverUri
        val deferred = CompletableDeferred<Unit>()
        authDeferred = deferred

        val request = Request.Builder().url(serverUri).build()
        val listener = object : WebSocketListener() {
            override fun onOpen(webSocket: WebSocket, response: Response) {
                val identity = identityStore.identity
                val hello = envelope(
                    MessageType.Hello,
                    HelloPayload(
                        deviceId = identity.deviceId,
                        deviceName = deviceName,
                        deviceType = "android",
                        publicKey = identity.publicKeyBase64,
                        appVersion = "0.9.0",
                        protocolVersion = PROTOCOL_VERSION
                    )
                )
                webSocket.send(ProtocolJson.json.encodeToString(Envelope.serializer(), hello))
            }

            override fun onMessage(webSocket: WebSocket, text: String) {
                if (text.length > MAX_MESSAGE_BYTES * 4) {
                    logStore.warn("Signaling", "收到超大信令消息，已忽略")
                    return
                }

                val env = try {
                    ProtocolJson.json.decodeFromString(Envelope.serializer(), text)
                } catch (ex: Exception) {
                    logStore.warn("Signaling", "无法解析信令消息：${ex.message}")
                    return
                }

                when (env.type) {
                    MessageType.AuthChallenge -> handleChallenge(webSocket, env)
                    MessageType.AuthOk -> {
                        val payload = env.decodePayload<AuthOkPayload>()
                        self = payload?.self
                        isAuthenticated = true
                        deferred.complete(Unit)
                        logStore.info("Signaling", "身份认证成功")
                        startHeartbeat()
                    }
                    MessageType.Error -> {
                        val error = env.decodePayload<ErrorPayload>()
                        if (authDeferred?.isCompleted == false) {
                            deferred.completeExceptionally(
                                IllegalStateException(error?.message ?: "信令认证失败")
                            )
                        }
                        _messages.tryEmit(env)
                    }
                    else -> {
                        if (env.sessionId != null) sessionId = env.sessionId
                        _messages.tryEmit(env)
                    }
                }
            }

            override fun onFailure(webSocket: WebSocket, t: Throwable, response: Response?) {
                isAuthenticated = false
                logStore.error("Signaling", "信令连接断开", t)
                if (authDeferred?.isCompleted == false) {
                    deferred.completeExceptionally(t)
                }
                _closed.tryEmit(t.message)
            }

            override fun onClosed(webSocket: WebSocket, code: Int, reason: String) {
                isAuthenticated = false
                logStore.info("Signaling", "信令连接关闭：$reason")
                if (authDeferred?.isCompleted == false) {
                    deferred.completeExceptionally(IllegalStateException("信令连接关闭：$reason"))
                }
                _closed.tryEmit(reason)
            }
        }

        socket = client.newWebSocket(request, listener)
        try {
            withTimeout(15_000) { deferred.await() }
            Result.success(Unit)
        } catch (ex: Exception) {
            close()
            Result.failure(ex)
        } finally {
            authDeferred = null
        }
    }

    private fun handleChallenge(webSocket: WebSocket, env: Envelope) {
        val challenge = env.decodePayload<AuthChallengePayload>() ?: return
        val identity = identityStore.identity
        val timestamp = System.currentTimeMillis()
        val canonical = buildAuthCanonical(challenge.nonce, identity.deviceId, timestamp)
        val signature = Base64.encodeToString(identityStore.sign(canonical), Base64.NO_WRAP)
        val proof = envelope(
            MessageType.AuthProof,
            AuthProofPayload(signature = signature, clientTimestampUnixMs = timestamp)
        )
        webSocket.send(ProtocolJson.json.encodeToString(Envelope.serializer(), proof))
    }

    suspend fun send(envelope: Envelope): Boolean = withContext(Dispatchers.IO) {
        val ws = socket ?: return@withContext false
        if (envelope.sessionId != null) sessionId = envelope.sessionId
        ws.send(ProtocolJson.json.encodeToString(Envelope.serializer(), envelope))
    }

    private fun startHeartbeat() {
        pingJob?.cancel()
        pingJob = scope.launch {
            while (true) {
                delay(30_000)
                if (isAuthenticated) {
                    send(envelope(MessageType.Ping, PingPayload(System.currentTimeMillis())))
                } else {
                    break
                }
            }
        }
    }

    fun close() {
        pingJob?.cancel()
        pingJob = null
        socket?.close(1000, "bye")
        socket = null
        isAuthenticated = false
        self = null
        sessionId = null
    }
}
