package com.linkview.app.rtc

import android.content.Context
import android.content.Intent
import com.linkview.app.data.AppSettings
import com.linkview.app.data.IceCandidatePayload
import com.linkview.app.data.LogStore
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.suspendCancellableCoroutine
import kotlinx.coroutines.withContext
import org.webrtc.DataChannel
import org.webrtc.DefaultVideoDecoderFactory
import org.webrtc.DefaultVideoEncoderFactory
import org.webrtc.EglBase
import org.webrtc.IceCandidate
import org.webrtc.MediaConstraints
import org.webrtc.MediaStream
import org.webrtc.MediaStreamTrack
import org.webrtc.PeerConnection
import org.webrtc.PeerConnectionFactory
import org.webrtc.RTCStatsReport
import org.webrtc.RtpReceiver
import org.webrtc.ScreenCapturerAndroid
import org.webrtc.SdpObserver
import org.webrtc.SessionDescription
import org.webrtc.SurfaceTextureHelper
import org.webrtc.SurfaceViewRenderer
import org.webrtc.VideoCapturer
import org.webrtc.VideoFrame
import org.webrtc.VideoSink
import org.webrtc.VideoSource
import org.webrtc.VideoTrack
import java.util.concurrent.atomic.AtomicBoolean
import kotlin.coroutines.resume

data class RtcStats(
    val sentFps: Int = 0,
    val recvFps: Int = 0,
    val sentKbps: Double = 0.0,
    val recvKbps: Double = 0.0,
    val encodeMs: Double = 0.0,
    val decodeMs: Double = 0.0,
    val sendWidth: Int = 0,
    val sendHeight: Int = 0,
    val recvWidth: Int = 0,
    val recvHeight: Int = 0,
    val rttMs: Double? = null,
    val packetsLost: Int? = null,
    val jitterMs: Double? = null,
    val codec: String = "VP8"
)

/**
 * WebRTC 媒体端。采集使用 MediaProjection (ScreenCapturerAndroid)，远端画面渲染到 SurfaceViewRenderer。
 * 仅启用视频轨；音频未在本版本实现。
 */
class WebRtcClient(
    private val context: Context,
    private val logStore: LogStore,
    private val listener: Listener
) {
    interface Listener {
        fun onLocalCandidate(payload: IceCandidatePayload)
        fun onConnectionState(state: PeerConnection.PeerConnectionState)
        fun onRemoteVideoTrack(track: VideoTrack)
        fun onScreenCaptureStopped()
    }

    val eglBase: EglBase = EglBase.create()
    private var factory: PeerConnectionFactory? = null
    private var peerConnection: PeerConnection? = null
    private var videoSource: VideoSource? = null
    private var videoTrack: VideoTrack? = null
    private var remoteVideoTrack: VideoTrack? = null
    private var capturer: VideoCapturer? = null
    private var surfaceTextureHelper: SurfaceTextureHelper? = null
    private var remoteRenderer: SurfaceViewRenderer? = null
    private var remoteSinkProxy: ProxySink? = null
    private val closed = AtomicBoolean(false)
    private var lastBytesSent = 0L
    private var lastBytesReceived = 0L
    private var lastStatsAt = 0L
    private var currentStats = RtcStats()

    val isActive: Boolean get() = peerConnection != null

    fun initialize(settings: AppSettings, turnCredential: String?) {
        val eglContext = eglBase.eglBaseContext
        factory = PeerConnectionFactory.builder()
            .setOptions(PeerConnectionFactory.Options())
            .setVideoEncoderFactory(DefaultVideoEncoderFactory(eglContext, true, true))
            .setVideoDecoderFactory(DefaultVideoDecoderFactory(eglContext))
            .createPeerConnectionFactory()

        val iceServers = buildIceServers(settings, turnCredential)
        val configuration = PeerConnection.RTCConfiguration(iceServers).apply {
            sdpSemantics = PeerConnection.SdpSemantics.UNIFIED_PLAN
            bundlePolicy = PeerConnection.BundlePolicy.MAXBUNDLE
            continualGatheringPolicy = PeerConnection.ContinualGatheringPolicy.GATHER_CONTINUALLY
            iceTransportsType = if (settings.iceRelayOnly) {
                PeerConnection.IceTransportsType.RELAY
            } else {
                PeerConnection.IceTransportsType.ALL
            }
        }

        val pc = factory!!.createPeerConnection(configuration, object : PeerConnection.Observer {
            override fun onIceCandidate(candidate: IceCandidate) {
                listener.onLocalCandidate(
                    IceCandidatePayload(
                        candidate = candidate.sdp,
                        sdpMid = candidate.sdpMid,
                        sdpMLineIndex = candidate.sdpMLineIndex,
                        usernameFragment = null
                    )
                )
            }

            override fun onIceCandidatesRemoved(candidates: Array<out IceCandidate>) {
                // Trickle ICE 不支持移除候选，忽略。
            }

            override fun onSignalingChange(state: PeerConnection.SignalingState) = Unit

            override fun onIceConnectionChange(state: PeerConnection.IceConnectionState) {
                logStore.info("WebRTC", "ICE 状态：$state")
            }

            override fun onIceConnectionReceivingChange(receiving: Boolean) = Unit

            override fun onIceGatheringChange(state: PeerConnection.IceGatheringState) = Unit

            override fun onAddStream(stream: MediaStream) = Unit

            override fun onRemoveStream(stream: MediaStream) = Unit

            override fun onDataChannel(channel: DataChannel) = Unit

            override fun onRenegotiationNeeded() = Unit

            override fun onAddTrack(receiver: RtpReceiver, streams: Array<out MediaStream>) {
                val track = receiver.track()
                if (track is VideoTrack) {
                    remoteVideoTrack = track
                    remoteSinkProxy?.let { track.addSink(it) }
                    listener.onRemoteVideoTrack(track)
                }
            }

            override fun onConnectionChange(newState: PeerConnection.PeerConnectionState) {
                logStore.info("WebRTC", "连接状态：$newState")
                listener.onConnectionState(newState)
            }
        }) ?: error("无法创建 PeerConnection")
        peerConnection = pc

        val source = factory!!.createVideoSource(false)
        val track = factory!!.createVideoTrack("linkview-video", source)
        videoSource = source
        videoTrack = track
        pc.addTrack(track, listOf("linkview"))

        remoteSinkProxy = ProxySink()
        logStore.info("WebRTC", "媒体端已初始化（VP8）")
    }

    private fun buildIceServers(settings: AppSettings, turnCredential: String?): List<PeerConnection.IceServer> {
        val servers = mutableListOf<PeerConnection.IceServer>()
        settings.stunServers.split('\n', ',')
            .map { it.trim() }
            .filter { it.isNotEmpty() }
            .forEach { servers.add(PeerConnection.IceServer.builder(it).createIceServer()) }
        if (settings.turnUri.isNotBlank()) {
            servers.add(
                PeerConnection.IceServer.builder(settings.turnUri.trim())
                    .setUsername(settings.turnUsername.trim())
                    .setPassword(turnCredential ?: "")
                    .createIceServer()
            )
        }
        return servers
    }

    suspend fun createOffer(settings: AppSettings): String? {
        val pc = peerConnection ?: return null
        val offer = awaitSdp { observer -> pc.createOffer(observer, MediaConstraints()) } ?: return null
        val applied = awaitSetSdp(offer) { observer -> pc.setLocalDescription(observer, offer) }
        if (!applied) return null
        applyBitrate(settings)
        return offer.description
    }

    suspend fun acceptOffer(offerSdp: String, settings: AppSettings): String? {
        val pc = peerConnection ?: return null
        val offer = SessionDescription(SessionDescription.Type.OFFER, offerSdp)
        if (!awaitSetSdp(offer) { observer -> pc.setRemoteDescription(observer, offer) }) return null
        applyBitrate(settings)
        val answer = awaitSdp { observer -> pc.createAnswer(observer, MediaConstraints()) } ?: return null
        if (!awaitSetSdp(answer) { observer -> pc.setLocalDescription(observer, answer) }) return null
        return answer.description
    }

    suspend fun acceptAnswer(answerSdp: String): Boolean {
        val pc = peerConnection ?: return false
        val answer = SessionDescription(SessionDescription.Type.ANSWER, answerSdp)
        return awaitSetSdp(answer) { observer -> pc.setRemoteDescription(observer, answer) }
    }

    fun addRemoteCandidate(payload: IceCandidatePayload) {
        val pc = peerConnection ?: return
        if (payload.sdpMid == null && payload.sdpMLineIndex == null) return
        pc.addIceCandidate(
            IceCandidate(
                payload.sdpMid ?: "0",
                payload.sdpMLineIndex ?: 0,
                payload.candidate
            )
        )
    }

    fun attachScreenCapturer(permissionData: Intent, settings: AppSettings) {
        val source = videoSource ?: return
        stopCapturer()
        val screenCapturer = ScreenCapturerAndroid(
            permissionData,
            object : android.media.projection.MediaProjection.Callback() {
                override fun onStop() {
                    logStore.warn("WebRTC", "系统停止了屏幕共享")
                    listener.onScreenCaptureStopped()
                }
            }
        )
        val helper = SurfaceTextureHelper.create("LinkViewCapture", eglBase.eglBaseContext)
        screenCapturer.initialize(helper, context, source.capturerObserver)
        screenCapturer.startCapture(
            settings.maxWidth.coerceAtLeast(320),
            settings.maxHeight.coerceAtLeast(240),
            settings.targetFps.coerceIn(5, 60)
        )
        surfaceTextureHelper = helper
        capturer = screenCapturer
        logStore.info("WebRTC", "屏幕采集已开始 ${settings.maxWidth}x${settings.maxHeight}@${settings.targetFps}")
    }

    fun setTargetBitrate(kbps: Int) {
        val sender = peerConnection?.senders?.firstOrNull() ?: return
        val params = sender.parameters
        params.encodings.forEach { encoding ->
            encoding.maxBitrateBps = kbps * 1000
            encoding.minBitrateBps = (kbps * 350)
        }
        sender.parameters = params
    }

    private fun applyBitrate(settings: AppSettings) {
        if (settings.targetBitrateKbps > 0) setTargetBitrate(settings.targetBitrateKbps)
    }

    fun stopCapturer() {
        try {
            capturer?.stopCapture()
        } catch (_: InterruptedException) {
        }
        capturer?.dispose()
        capturer = null
        surfaceTextureHelper?.dispose()
        surfaceTextureHelper = null
    }

    fun attachRemoteRenderer(renderer: SurfaceViewRenderer) {
        remoteRenderer = renderer
        remoteSinkProxy?.let { sink ->
            sink.target = renderer
            remoteVideoTrack?.removeSink(sink)
            remoteVideoTrack?.addSink(sink)
        }
    }

    fun detachRemoteRenderer() {
        remoteSinkProxy?.let { remoteVideoTrack?.removeSink(it) }
        remoteSinkProxy?.target = null
        remoteRenderer = null
    }

    suspend fun getStats(): RtcStats = withContext(Dispatchers.Main) {
        val pc = peerConnection ?: return@withContext currentStats
        suspendCancellableCoroutine { continuation ->
            pc.getStats { report ->
                currentStats = parseStats(report)
                if (continuation.isActive) continuation.resume(currentStats)
            }
        }
    }

    private fun parseStats(report: RTCStatsReport): RtcStats {
        var sentFps = 0
        var recvFps = 0
        var sentWidth = 0
        var sentHeight = 0
        var recvWidth = 0
        var recvHeight = 0
        var packetsLost: Int? = null
        var jitterMs: Double? = null
        var rttMs: Double? = null
        var codec = "VP8"
        var bytesSent = 0L
        var bytesReceived = 0L
        var encodeMs = 0.0
        var decodeMs = 0.0
        val codecNames = mutableMapOf<String, String>()

        for (stat in report.statsMap.values) {
            val members = stat.members
            when (members["type"]) {
                "codec" -> {
                    val mime = members["mimeType"]?.toString() ?: continue
                    if (mime.contains("VP8", true)) {
                        stat.id.let { codecNames[it] = "VP8" }
                    } else if (mime.contains("H264", true)) {
                        codecNames[stat.id] = "H.264"
                    }
                }
                "outbound-rtp" -> {
                    if (members["kind"] != "video") continue
                    sentFps = (members["framesPerSecond"] as? Number)?.toInt() ?: sentFps
                    sentWidth = (members["frameWidth"] as? Number)?.toInt() ?: sentWidth
                    sentHeight = (members["frameHeight"] as? Number)?.toInt() ?: sentHeight
                    bytesSent = (members["bytesSent"] as? Number)?.toLong() ?: bytesSent
                    val total = (members["totalEncodeTime"] as? Number)?.toDouble() ?: 0.0
                    val frames = (members["framesEncoded"] as? Number)?.toDouble() ?: 0.0
                    if (frames > 0) encodeMs = total / frames * 1000.0
                    val codecId = members["codecId"]?.toString()
                    codecId?.let { codecNames[it]?.let { name -> codec = name } }
                }
                "inbound-rtp" -> {
                    if (members["kind"] != "video") continue
                    recvFps = (members["framesPerSecond"] as? Number)?.toInt() ?: recvFps
                    recvWidth = (members["frameWidth"] as? Number)?.toInt() ?: recvWidth
                    recvHeight = (members["frameHeight"] as? Number)?.toInt() ?: recvHeight
                    bytesReceived = (members["bytesReceived"] as? Number)?.toLong() ?: bytesReceived
                    packetsLost = (members["packetsLost"] as? Number)?.toInt()
                    jitterMs = (members["jitter"] as? Number)?.toDouble()?.times(1000.0)
                    val total = (members["totalDecodeTime"] as? Number)?.toDouble() ?: 0.0
                    val frames = (members["framesDecoded"] as? Number)?.toDouble() ?: 0.0
                    if (frames > 0) decodeMs = total / frames * 1000.0
                    val codecId = members["codecId"]?.toString()
                    codecId?.let { codecNames[it]?.let { name -> codec = name } }
                }
                "candidate-pair" -> {
                    val state = members["state"]?.toString()
                    if (state == "succeeded") {
                        rttMs = (members["currentRoundTripTime"] as? Number)?.toDouble()?.times(1000.0)
                    }
                }
            }
        }

        val now = System.currentTimeMillis()
        val seconds = if (lastStatsAt == 0L) 1.0 else ((now - lastStatsAt).coerceAtLeast(200) / 1000.0)
        val sentKbps = ((bytesSent - lastBytesSent).coerceAtLeast(0) * 8 / 1000.0 / seconds)
        val recvKbps = ((bytesReceived - lastBytesReceived).coerceAtLeast(0) * 8 / 1000.0 / seconds)
        lastBytesSent = bytesSent
        lastBytesReceived = bytesReceived
        lastStatsAt = now

        return RtcStats(
            sentFps = sentFps,
            recvFps = recvFps,
            sentKbps = sentKbps,
            recvKbps = recvKbps,
            encodeMs = encodeMs,
            decodeMs = decodeMs,
            sendWidth = sentWidth,
            sendHeight = sentHeight,
            recvWidth = recvWidth,
            recvHeight = recvHeight,
            rttMs = rttMs,
            packetsLost = packetsLost,
            jitterMs = jitterMs,
            codec = codec
        )
    }

    private suspend fun awaitSdp(create: (SdpObserver) -> Unit): SessionDescription? =
        suspendCancellableCoroutine { continuation ->
            create(object : SdpObserver {
                override fun onCreateSuccess(description: SessionDescription) {
                    if (continuation.isActive) continuation.resume(description)
                }

                override fun onCreateFailure(error: String?) {
                    logStore.error("WebRTC", "创建 SDP 失败：$error")
                    if (continuation.isActive) continuation.resume(null)
                }

                override fun onSetSuccess() = Unit

                override fun onSetFailure(error: String?) = Unit
            })
        }

    private suspend fun awaitSetSdp(description: SessionDescription, apply: (SdpObserver) -> Unit): Boolean =
        suspendCancellableCoroutine { continuation ->
            apply(object : SdpObserver {
                override fun onCreateSuccess(description: SessionDescription) = Unit

                override fun onCreateFailure(error: String?) = Unit

                override fun onSetSuccess() {
                    if (continuation.isActive) continuation.resume(true)
                }

                override fun onSetFailure(error: String?) {
                    logStore.error("WebRTC", "应用 SDP 失败：$error")
                    if (continuation.isActive) continuation.resume(false)
                }
            })
        }

    fun dispose() {
        if (!closed.compareAndSet(false, true)) return
        stopCapturer()
        detachRemoteRenderer()
        try {
            peerConnection?.close()
        } catch (_: Exception) {
        }
        peerConnection?.dispose()
        peerConnection = null
        videoTrack?.dispose()
        videoTrack = null
        videoSource?.dispose()
        videoSource = null
        remoteVideoTrack?.dispose()
        remoteVideoTrack = null
        factory?.dispose()
        factory = null
        try {
            eglBase.release()
        } catch (_: Exception) {
        }
    }

    private class ProxySink : VideoSink {
        @Volatile
        var target: VideoSink? = null

        override fun onFrame(frame: VideoFrame) {
            target?.onFrame(frame)
        }
    }
}
