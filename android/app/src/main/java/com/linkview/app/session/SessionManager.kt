package com.linkview.app.session

import com.linkview.app.media.ScreenShareService

import android.content.Context
import android.content.Intent
import com.linkview.app.data.DeviceSummary
import com.linkview.app.data.Envelope
import com.linkview.app.data.ErrorPayload
import com.linkview.app.data.IceCandidatePayload
import com.linkview.app.data.IdentityStore
import com.linkview.app.data.JoinRequestPayload
import com.linkview.app.data.LogStore
import com.linkview.app.data.MessageType
import com.linkview.app.data.PairCreatePayload
import com.linkview.app.data.PairCreatedPayload
import com.linkview.app.data.PairDecisionPayload
import com.linkview.app.data.PairJoinPayload
import com.linkview.app.data.PairJoinedPayload
import com.linkview.app.data.PeerLeftPayload
import com.linkview.app.data.SdpPayload
import com.linkview.app.data.SessionEndPayload
import com.linkview.app.data.SettingsStore
import com.linkview.app.data.decodePayload
import com.linkview.app.data.envelope
import com.linkview.app.net.SignalingClient
import com.linkview.app.rtc.RtcStats
import com.linkview.app.rtc.WebRtcClient
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import okhttp3.OkHttpClient
import okhttp3.Request
import org.webrtc.PeerConnection
import org.webrtc.VideoTrack
import java.util.UUID

enum class SessionState {
    Idle, Pairing, AwaitingConsent, Negotiating, Connecting, Connected, Sharing,
    Reconnecting, Stopping, Disconnected, Failed
}

data class SessionUiState(
    val state: SessionState = SessionState.Idle,
    val peer: DeviceSummary? = null,
    val role: String? = null,
    val pairingCode: String? = null,
    val pairingExpiresAt: Long? = null,
    val pendingGuest: DeviceSummary? = null,
    val error: String? = null,
    val isSharing: Boolean = false,
    val stats: RtcStats = RtcStats(),
    val startedAt: Long? = null,
    val infoMessage: String? = null
)

class SessionManager(
    private val context: Context,
    private val settingsStore: SettingsStore,
    private val identityStore: IdentityStore,
    private val logStore: LogStore,
    private val scope: CoroutineScope
) {
    private val signaling = SignalingClient(identityStore, logStore, scope)
    private var rtc: WebRtcClient? = null
    private var statsJob: Job? = null
    private val _state = MutableStateFlow(SessionUiState())
    val state: StateFlow<SessionUiState> = _state
    private val _remoteVideoTrack = MutableStateFlow<VideoTrack?>(null)
    val remoteVideoTrack: StateFlow<VideoTrack?> = _remoteVideoTrack
    val eglContext get() = rtc?.eglBase?.eglBaseContext

    init {
        scope.launch {
            signaling.messages.collect { handleMessage(it) }
        }
        scope.launch {
            signaling.closed.collect { reason ->
                if (_state.value.state !in setOf(SessionState.Idle, SessionState.Disconnected, SessionState.Failed)) {
                    fail(reason ?: "信令连接已断开")
                }
            }
        }
    }

    private suspend fun ensureSignaling(): Boolean {
        val settings = settingsStore.settings.value
        val result = signaling.connect(settings.signalingServer, settings.deviceName)
        return if (result.isSuccess) {
            true
        } else {
            fail("无法连接信令服务：${result.exceptionOrNull()?.message}")
            false
        }
    }

    fun createPairing() {
        scope.launch {
            if (!ensureSignaling()) return@launch
            _state.value = _state.value.copy(state = SessionState.Pairing, error = null, pairingCode = null)
            signaling.send(
                envelope(MessageType.PairCreate, PairCreatePayload(300))
            )
        }
    }

    fun join(code: String) {
        val trimmed = code.trim()
        if (trimmed.length != 6 || trimmed.any { !it.isDigit() }) {
            _state.value = _state.value.copy(error = "请输入 6 位数字配对码")
            return
        }

        scope.launch {
            if (!ensureSignaling()) return@launch
            _state.value = _state.value.copy(state = SessionState.Pairing, error = null)
            signaling.send(envelope(MessageType.PairJoin, PairJoinPayload(trimmed)))
            _state.value = _state.value.copy(infoMessage = "已发送加入请求，等待对方确认…")
        }
    }

    fun acceptJoin() {
        val sessionId = signaling.sessionId ?: return
        scope.launch {
            signaling.send(
                envelope(
                    MessageType.PairDecision,
                    PairDecisionPayload(true),
                    sessionId
                )
            )
            _state.value = _state.value.copy(pendingGuest = null)
        }
    }

    fun rejectJoin() {
        val sessionId = signaling.sessionId ?: return
        scope.launch {
            signaling.send(
                envelope(
                    MessageType.PairDecision,
                    PairDecisionPayload(false, "user rejected"),
                    sessionId
                )
            )
            _state.value = _state.value.copy(pendingGuest = null)
        }
    }

    fun cancelPairing() {
        scope.launch {
            signaling.sessionId?.let {
                signaling.send(envelope(MessageType.SessionEnd, SessionEndPayload("取消配对"), it))
            }
            cleanupMedia()
            signaling.close()
            _state.value = SessionUiState(state = SessionState.Disconnected)
        }
    }

    fun attachScreenCapture(permissionData: Intent) {
        val client = rtc
        if (client == null) {
            logStore.warn("Session", "尚未建立会话，忽略采集请求")
            return
        }
        try {
            client.attachScreenCapturer(permissionData, settingsStore.settings.value)
            _state.value = _state.value.copy(state = SessionState.Sharing, isSharing = true, error = null)
            startStatsPolling()
        } catch (ex: Exception) {
            logStore.error("Session", "启动屏幕采集失败", ex)
            _state.value = _state.value.copy(state = SessionState.Connected, isSharing = false, error = "启动屏幕采集失败：${ex.message}")
        }
    }

    fun stopSharing() {
        rtc?.stopCapturer()
        _state.value = _state.value.copy(
            isSharing = false,
            state = if (_state.value.state == SessionState.Sharing) SessionState.Connected else _state.value.state
        )
    }

    fun onSystemStoppedCapture() {
        rtc?.stopCapturer()
        context.stopService(android.content.Intent(context, ScreenShareService::class.java))
        _state.value = _state.value.copy(
            isSharing = false,
            state = if (_state.value.state == SessionState.Sharing) SessionState.Connected else _state.value.state,
            infoMessage = "系统已停止屏幕共享"
        )
    }

    fun disconnect() {
        scope.launch {
            _state.value = _state.value.copy(state = SessionState.Stopping)
            signaling.sessionId?.let {
                signaling.send(envelope(MessageType.SessionEnd, SessionEndPayload("用户断开连接"), it))
            }
            cleanupMedia()
            signaling.close()
            _state.value = SessionUiState(state = SessionState.Disconnected)
        }
    }

    private suspend fun handleMessage(env: Envelope) {
        when (env.type) {
            MessageType.PairCreated -> {
                val payload = env.decodePayload<PairCreatedPayload>() ?: return
                signaling.sessionId = payload.sessionId
                _state.value = _state.value.copy(
                    state = SessionState.Pairing,
                    pairingCode = payload.pairingCode,
                    pairingExpiresAt = payload.expiresAtUnixMs,
                    error = null
                )
                logStore.info("Session", "配对码已生成")
            }
            MessageType.JoinRequest -> {
                val payload = env.decodePayload<JoinRequestPayload>() ?: return
                _state.value = _state.value.copy(
                    state = SessionState.AwaitingConsent,
                    pendingGuest = payload.guest
                )
                logStore.info("Session", "设备 ${payload.guest.deviceName} 请求连接")
                if (!settingsStore.settings.value.requireConfirmation) acceptJoin()
            }
            MessageType.PairJoined -> {
                val payload = env.decodePayload<PairJoinedPayload>() ?: return
                signaling.sessionId = payload.sessionId
                _state.value = _state.value.copy(
                    state = SessionState.Negotiating,
                    peer = payload.peer,
                    role = payload.role,
                    pendingGuest = null,
                    pairingCode = null,
                    error = null
                )
                startRtc(payload)
            }
            MessageType.SdpOffer -> {
                val payload = env.decodePayload<SdpPayload>() ?: return
                val client = rtc ?: return
                setState(SessionState.Connecting)
                scope.launch {
                    val answer = client.acceptOffer(payload.sdp, settingsStore.settings.value)
                    if (answer != null) {
                        signaling.send(
                            envelope(MessageType.SdpAnswer, SdpPayload("answer", answer), signaling.sessionId)
                        )
                    } else {
                        fail("无法处理对方的 SDP Offer")
                    }
                }
            }
            MessageType.SdpAnswer -> {
                val payload = env.decodePayload<SdpPayload>() ?: return
                setState(SessionState.Connecting)
                scope.launch {
                    if (rtc?.acceptAnswer(payload.sdp) != true) fail("无法应用对方的 SDP Answer")
                }
            }
            MessageType.IceCandidate -> {
                env.decodePayload<IceCandidatePayload>()?.let { rtc?.addRemoteCandidate(it) }
            }
            MessageType.PeerLeft -> {
                val payload = env.decodePayload<PeerLeftPayload>()
                handleRemoteEnded(payload?.reason ?: "对方已断开")
            }
            MessageType.SessionEnd -> {
                val payload = env.decodePayload<SessionEndPayload>()
                handleRemoteEnded(payload?.reason ?: "会话已结束")
            }
            MessageType.Error -> {
                val payload = env.decodePayload<ErrorPayload>()
                fail(payload?.message ?: "信令服务返回错误")
            }
        }
    }

    private fun startRtc(payload: PairJoinedPayload) {
        cleanupMedia(closeSignaling = false)
        val client = WebRtcClient(context, logStore, object : WebRtcClient.Listener {
            override fun onLocalCandidate(payloadCandidate: IceCandidatePayload) {
                scope.launch {
                    signaling.send(
                        envelope(MessageType.IceCandidate, payloadCandidate, signaling.sessionId)
                    )
                }
            }

            override fun onConnectionState(state: PeerConnection.PeerConnectionState) {
                when (state) {
                    PeerConnection.PeerConnectionState.CONNECTED -> {
                        _state.value = _state.value.copy(
                            state = if (_state.value.isSharing) SessionState.Sharing else SessionState.Connected,
                            startedAt = _state.value.startedAt ?: System.currentTimeMillis()
                        )
                        startStatsPolling()
                    }
                    PeerConnection.PeerConnectionState.FAILED -> fail("WebRTC 连接失败")
                    PeerConnection.PeerConnectionState.DISCONNECTED -> {
                        if (_state.value.state == SessionState.Connected || _state.value.state == SessionState.Sharing) {
                            setState(SessionState.Reconnecting)
                            scope.launch {
                                delay(6000)
                                if (_state.value.state == SessionState.Reconnecting) {
                                    fail("连接中断，自动恢复未成功")
                                }
                            }
                        }
                    }
                    PeerConnection.PeerConnectionState.CLOSED -> {
                        if (_state.value.state !in setOf(SessionState.Disconnected, SessionState.Failed)) {
                            handleRemoteEnded("连接已关闭")
                        }
                    }
                    else -> Unit
                }
            }

            override fun onRemoteVideoTrack(track: VideoTrack) {
                _remoteVideoTrack.value = track
            }

            override fun onScreenCaptureStopped() {
                onSystemStoppedCapture()
            }
        })

        client.initialize(settingsStore.settings.value, settingsStore.turnCredential())
        rtc = client

        if (payload.role == "host") {
            scope.launch {
                val offer = client.createOffer(settingsStore.settings.value)
                if (offer != null) {
                    signaling.send(envelope(MessageType.SdpOffer, SdpPayload("offer", offer), signaling.sessionId))
                    setState(SessionState.Connecting)
                } else {
                    fail("无法创建 SDP Offer")
                }
            }
        }
    }

    private fun startStatsPolling() {
        statsJob?.cancel()
        statsJob = scope.launch {
            while (_state.value.state in setOf(SessionState.Connected, SessionState.Sharing, SessionState.Reconnecting)) {
                val stats = rtc?.getStats()
                if (stats != null) {
                    _state.value = _state.value.copy(stats = stats)
                }
                delay(1000)
            }
        }
    }

    private fun handleRemoteEnded(reason: String) {
        cleanupMedia()
        signaling.close()
        _state.value = SessionUiState(state = SessionState.Disconnected, infoMessage = reason)
        logStore.info("Session", reason)
    }

    private fun fail(reason: String) {
        cleanupMedia()
        _state.value = _state.value.copy(state = SessionState.Failed, error = reason, isSharing = false)
        logStore.error("Session", reason)
    }

    private fun setState(state: SessionState) {
        _state.value = _state.value.copy(state = state)
    }

    private fun cleanupMedia(closeSignaling: Boolean = false) {
        if (_state.value.isSharing) {
            context.stopService(android.content.Intent(context, ScreenShareService::class.java))
        }
        statsJob?.cancel()
        statsJob = null
        _remoteVideoTrack.value = null
        rtc?.dispose()
        rtc = null
        if (closeSignaling) signaling.close()
    }

    fun currentRtc(): WebRtcClient? = rtc
}
