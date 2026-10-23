namespace LinkView.Core.Errors;

/// <summary>可检索、可测试的错误目录。禁止在业务代码里散落临时字符串错误。</summary>
public static class LinkViewErrors
{
    public static LinkViewError CapturePermissionDenied(string detail) => new(
        ErrorCategory.Permissions, "CAPTURE_PERMISSION_DENIED",
        "系统未授予屏幕采集权限，请重新发起共享并确认授权。", detail, false, RecoveryAction.Reauthorize);

    public static LinkViewError CaptureSourceLost(string detail) => new(
        ErrorCategory.MediaCapture, "CAPTURE_SOURCE_LOST",
        "采集源已关闭或显示器发生变化，需要重新选择采集源。", detail, false, RecoveryAction.RestartCapture);

    public static LinkViewError CaptureStartFailed(string detail) => new(
        ErrorCategory.MediaCapture, "CAPTURE_START_FAILED",
        "无法开始采集屏幕。请检查采集源是否仍然可用。", detail, true, RecoveryAction.RestartCapture);

    public static LinkViewError EncoderInitFailed(string codec, string detail) => new(
        ErrorCategory.Encoding, "ENCODER_INIT_FAILED",
        $"无法初始化 {codec} 编码器。可尝试降低分辨率或切换编码格式。", detail, false, RecoveryAction.ChangeSettings);

    public static LinkViewError EncoderFailed(string detail) => new(
        ErrorCategory.Encoding, "ENCODER_FAILED",
        "视频编码失败，画面已停止发送。", detail, true, RecoveryAction.RestartCapture);

    public static LinkViewError DecoderInitFailed(string codec, string detail) => new(
        ErrorCategory.Decoding, "DECODER_INIT_FAILED",
        $"无法初始化 {codec} 解码器，当前设备可能不支持该编码格式。", detail, false, RecoveryAction.ChangeSettings);

    public static LinkViewError SignalingConnectFailed(string endpoint, string detail) => new(
        ErrorCategory.Signaling, "SIGNALING_CONNECT_FAILED",
        $"无法连接信令服务（{endpoint}）。局域网直连可不需要信令服务。", detail, true, RecoveryAction.CheckNetwork);

    public static LinkViewError SignalingAuthFailed(string detail) => new(
        ErrorCategory.Security, "SIGNALING_AUTH_FAILED",
        "信令服务身份验证失败，请稍后重试。", detail, false, RecoveryAction.Retry);

    public static LinkViewError PairingInvalid(string detail) => new(
        ErrorCategory.Pairing, "PAIRING_INVALID",
        "配对码不正确或已失效，请向对方索取新的配对码。", detail, false, RecoveryAction.RePair);

    public static LinkViewError PairingLocked(string detail) => new(
        ErrorCategory.Security, "PAIRING_LOCKED",
        "配对尝试次数过多，本次配对已锁定。请重新创建配对码。", detail, false, RecoveryAction.RePair);

    public static LinkViewError PeerRejected(string detail) => new(
        ErrorCategory.Session, "PEER_REJECTED",
        "对方拒绝了本次连接请求。", detail, false, RecoveryAction.None);

    public static LinkViewError PeerOffline(string detail) => new(
        ErrorCategory.Session, "PEER_OFFLINE",
        "对方设备已离线或断开连接。", detail, true, RecoveryAction.Retry);

    public static LinkViewError SdpNegotiationFailed(string detail) => new(
        ErrorCategory.WebRtcNegotiation, "SDP_NEGOTIATION_FAILED",
        "双方未能协商出共同支持的媒体格式。", detail, false, RecoveryAction.ChangeSettings);

    public static LinkViewError IceFailed(string detail) => new(
        ErrorCategory.IceConnectivity, "ICE_FAILED",
        "无法建立端到端连接。双方可能位于受限 NAT 之后，或防火墙阻止了 UDP。", detail, true, RecoveryAction.EnableRelay);

    public static LinkViewError IceTimeout(string detail) => new(
        ErrorCategory.IceConnectivity, "ICE_TIMEOUT",
        "连接超时。请确认两台设备在同一网络，或检查防火墙设置。", detail, true, RecoveryAction.CheckNetwork);

    public static LinkViewError MediaNotArriving(string detail) => new(
        ErrorCategory.Network, "MEDIA_NOT_ARRIVING",
        "连接已建立，但未收到视频数据。请检查网络质量或对方的共享状态。", detail, true, RecoveryAction.Retry);

    public static LinkViewError NetworkLost(string detail) => new(
        ErrorCategory.Network, "NETWORK_LOST",
        "网络连接已中断，正在等待网络恢复。", detail, true, RecoveryAction.CheckNetwork);

    public static LinkViewError ConfigurationInvalid(string detail) => new(
        ErrorCategory.Configuration, "CONFIGURATION_INVALID",
        "当前设置不合法，请检查高亮字段。", detail, false, RecoveryAction.ChangeSettings);

    public static LinkViewError Unknown(string detail) => new(
        ErrorCategory.Unknown, "UNKNOWN",
        "发生未知错误，详细信息已写入诊断日志。", detail, false, RecoveryAction.Retry);
}
