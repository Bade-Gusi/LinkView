using System.Diagnostics;
using System.Net;
using System.Threading.Channels;
using LinkView.Core.Networking;
using LinkView.Core.Settings;
using LinkView.Protocol;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.Encoders;

namespace LinkView.Windows.Media;

public sealed record DecodedFrame(byte[] Bgr, int Width, int Height, long TimestampTicks);

public sealed record LocalIceCandidate(string Candidate, string? SdpMid, int? SdpMLineIndex, string? UsernameFragment);

public sealed record WebRtcRuntimeStats(
    long FramesSent,
    long BytesSent,
    long FramesReceived,
    long BytesReceived,
    double AverageEncodeMs,
    double AverageDecodeMs,
    int SendWidth,
    int SendHeight,
    int LastDecodedWidth,
    int LastDecodedHeight,
    string Codec,
    string ConnectionState,
    string IceState);

/// <summary>
/// 单个 WebRTC 会话。负责 SDP/ICE、VP8 编解码与收发统计。
/// 应用层只通过该类型接触 SIPSorcery，避免协议细节泄漏到 ViewModel。
/// </summary>
public sealed class WebRtcSession : IAsyncDisposable
{
    private readonly ILogger<WebRtcSession> _logger;
    private readonly Channel<VideoPacket> _decodeQueue;
    private readonly VpxVideoEncoder _encoder = new();
    private RTCPeerConnection? _pc;
    private MediaStreamTrack? _videoTrack;
    private CancellationTokenSource? _decodeCts;
    private Task? _decodeTask;
    private long _framesSent;
    private long _bytesSent;
    private long _framesReceived;
    private long _bytesReceived;
    private long _encodeTotalUs;
    private long _encodeSamples;
    private long _decodeTotalUs;
    private long _decodeSamples;
    private int _sendWidth;
    private int _sendHeight;
    private int _lastDecodedWidth;
    private int _lastDecodedHeight;
    private const uint RtpClockRate = 90000;
    private NetworkPreference _preference = NetworkPreference.Auto;

    private readonly record struct VideoPacket(byte[] Payload, long ArrivedTicks);

    public WebRtcSession(ILogger<WebRtcSession> logger)
    {
        _logger = logger;
        _decodeQueue = Channel.CreateBounded<VideoPacket>(new BoundedChannelOptions(2)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public event EventHandler<LocalIceCandidate>? LocalCandidate;
    public event EventHandler<DecodedFrame>? FrameDecoded;
    public event EventHandler<RTCPeerConnectionState>? ConnectionStateChanged;
    public event EventHandler<RTCIceConnectionState>? IceStateChanged;
    public event EventHandler<string>? DecodeError;

    public RTCPeerConnectionState ConnectionState => _pc?.connectionState ?? RTCPeerConnectionState.closed;
    public RTCIceConnectionState IceState => _pc?.iceConnectionState ?? RTCIceConnectionState.closed;
    public bool IsConnected => ConnectionState == RTCPeerConnectionState.connected;

    public void Initialize(AppSettings settings, IReadOnlyList<TurnServerSettings> turnWithSecrets, NetworkPreference preference)
    {
        _preference = preference;
        if (_pc is not null)
        {
            throw new InvalidOperationException("WebRTC 会话已经初始化。");
        }

        var config = new RTCConfiguration
        {
            iceServers = BuildIceServers(settings, turnWithSecrets),
            iceTransportPolicy = settings.Network.IceTransport == IceTransportMode.RelayOnly
                ? RTCIceTransportPolicy.relay
                : RTCIceTransportPolicy.all,
            X_ICEIncludeAllInterfaceAddresses = true,
            X_GatherTimeoutMs = Math.Max(3000, settings.Network.IceTimeoutSeconds * 1000)
        };

        _encoder.TargetKbps = (uint)settings.Video.TargetBitrateKbps;
        _pc = new RTCPeerConnection(config);
        _videoTrack = new MediaStreamTrack(_encoder.SupportedFormats, MediaStreamStatusEnum.SendRecv);
        _pc.addTrack(_videoTrack);

        _pc.onicecandidate += candidate => OnLocalCandidate(candidate, settings);
        _pc.onconnectionstatechange += state =>
        {
            _logger.LogInformation("webrtc_connection_state {State}", state);
            ConnectionStateChanged?.Invoke(this, state);
        };
        _pc.oniceconnectionstatechange += state =>
        {
            _logger.LogInformation("webrtc_ice_state {State}", state);
            IceStateChanged?.Invoke(this, state);
        };
        _pc.OnVideoFrameReceived += (remote, timestamp, payload, format) =>
        {
            if (format.Codec != VideoCodecsEnum.VP8)
            {
                return;
            }

            Interlocked.Increment(ref _framesReceived);
            Interlocked.Add(ref _bytesReceived, payload.Length);
            _decodeQueue.Writer.TryWrite(new VideoPacket(payload, Stopwatch.GetTimestamp()));
        };

        _decodeCts = new CancellationTokenSource();
        _decodeTask = Task.Run(() => DecodeLoopAsync(_decodeCts.Token));
        _logger.LogInformation("webrtc_initialized codec=VP8 iceServers={Count} policy={Policy}",
            config.iceServers.Count, config.iceTransportPolicy);
    }

    private static List<RTCIceServer> BuildIceServers(AppSettings settings, IReadOnlyList<TurnServerSettings> turnServers)
    {
        var servers = new List<RTCIceServer>();
        foreach (var stun in settings.Network.StunServers.Where(s => !string.IsNullOrWhiteSpace(s)))
        {
            servers.Add(new RTCIceServer { urls = stun });
        }

        foreach (var turn in turnServers)
        {
            if (string.IsNullOrWhiteSpace(turn.Uri))
            {
                continue;
            }

            servers.Add(new RTCIceServer
            {
                urls = turn.Uri,
                username = turn.Username,
                credential = turn.Credential
            });
        }

        return servers;
    }

    private void OnLocalCandidate(RTCIceCandidate candidate, AppSettings settings)
    {
        if (!CandidateAllowed(candidate.candidate, _preference))
        {
            return;
        }

        LocalCandidate?.Invoke(this, new LocalIceCandidate(
            candidate.candidate,
            candidate.sdpMid,
            candidate.sdpMLineIndex,
            candidate.usernameFragment));
    }

    private static bool CandidateAllowed(string candidate, NetworkPreference preference)
    {
        if (preference is not (NetworkPreference.Ipv4Only or NetworkPreference.Ipv6Only))
        {
            if (preference == NetworkPreference.Auto && candidate.Contains(" fe80:", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        try
        {
            var parsed = RTCIceCandidate.Parse(candidate);
            if (!IPAddress.TryParse(parsed.address, out var address))
            {
                return true;
            }

            var isV6 = address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6;
            if (preference == NetworkPreference.Ipv4Only && isV6) return false;
            if (preference == NetworkPreference.Ipv6Only && !isV6) return false;
            // 链路本地 IPv6 地址缺少可跨设备使用的 scope，默认不作为通用候选。
            if (isV6 && address.IsIPv6LinkLocal) return false;
            return true;
        }
        catch (FormatException)
        {
            return true;
        }
    }

    public async Task<string> CreateOfferAsync(CancellationToken ct)
    {
        var pc = _pc ?? throw new InvalidOperationException("WebRTC 会话未初始化。");
        var offer = pc.createOffer();
        await pc.setLocalDescription(offer).WaitAsync(ct).ConfigureAwait(false);
        return offer.sdp;
    }

    public async Task<string> AcceptOfferAsync(string sdp, CancellationToken ct)
    {
        var pc = _pc ?? throw new InvalidOperationException("WebRTC 会话未初始化。");
        var offer = new RTCSessionDescriptionInit { type = RTCSdpType.offer, sdp = sdp };
        var result = pc.setRemoteDescription(offer);
        if (result != SetDescriptionResultEnum.OK)
        {
            throw new InvalidOperationException($"无法接受远端 SDP：{result}");
        }

        var answer = pc.createAnswer();
        await pc.setLocalDescription(answer).WaitAsync(ct).ConfigureAwait(false);
        return answer.sdp;
    }

    public void AcceptAnswer(string sdp)
    {
        var pc = _pc ?? throw new InvalidOperationException("WebRTC 会话未初始化。");
        var answer = new RTCSessionDescriptionInit { type = RTCSdpType.answer, sdp = sdp };
        var result = pc.setRemoteDescription(answer);
        if (result != SetDescriptionResultEnum.OK)
        {
            throw new InvalidOperationException($"无法接受远端 SDP：{result}");
        }
    }

    public void AddRemoteCandidate(IceCandidatePayload payload)
    {
        var pc = _pc;
        if (pc is null || string.IsNullOrWhiteSpace(payload.Candidate))
        {
            return;
        }

        if (!CandidateAllowed(payload.Candidate, _preference))
        {
            return;
        }

        var init = new RTCIceCandidateInit
        {
            candidate = payload.Candidate,
            sdpMid = payload.SdpMid ?? "0",
            sdpMLineIndex = (ushort)(payload.SdpMLineIndex ?? 0),
            usernameFragment = payload.UsernameFragment
        };
        pc.addIceCandidate(init);
    }

    /// <summary>编码并排队发送一帧。必须在单一编码线程调用。</summary>
    public byte[]? EncodeAndSend(byte[] bgra, int width, int height, int fps)
    {
        var pc = _pc;
        if (pc is null || _videoTrack is null)
        {
            return null;
        }

        var start = Stopwatch.GetTimestamp();
        var encoded = _encoder.EncodeVideo(width, height, bgra, VideoPixelFormatsEnum.Bgra, VideoCodecsEnum.VP8);
        var elapsedUs = (long)Stopwatch.GetElapsedTime(start).TotalMicroseconds;
        if (encoded is not { Length: > 0 })
        {
            return null;
        }

        _sendWidth = width;
        _sendHeight = height;
        Interlocked.Add(ref _encodeTotalUs, elapsedUs);
        Interlocked.Increment(ref _encodeSamples);
        Interlocked.Increment(ref _framesSent);
        Interlocked.Add(ref _bytesSent, encoded.Length);
        pc.SendVideo(RtpClockRate / (uint)Math.Max(1, fps), encoded);
        return encoded;
    }

    public void ForceKeyFrame() => _encoder.ForceKeyFrame();

    /// <summary>触发 ICE 重启后必须重新交换 SDP。</summary>
    public void RestartIce() => _pc?.restartIce();

    public void SetTargetBitrate(int kbps) => _encoder.TargetKbps = (uint)Math.Clamp(kbps, 100, 100_000);

    private async Task DecodeLoopAsync(CancellationToken ct)
    {
        var reader = _decodeQueue.Reader;
        try
        {
            while (await reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                while (reader.TryRead(out var packet))
                {
                    var start = Stopwatch.GetTimestamp();
                    try
                    {
                        foreach (var sample in _encoder.DecodeVideo(packet.Payload, VideoPixelFormatsEnum.Bgra, VideoCodecsEnum.VP8))
                        {
                            _lastDecodedWidth = (int)sample.Width;
                            _lastDecodedHeight = (int)sample.Height;
                            FrameDecoded?.Invoke(this,
                                new DecodedFrame(sample.Sample, (int)sample.Width, (int)sample.Height, packet.ArrivedTicks));
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("video_decode_failed {Error}", ex.Message);
                        DecodeError?.Invoke(this, ex.Message);
                    }
                    finally
                    {
                        Interlocked.Add(ref _decodeTotalUs, (long)Stopwatch.GetElapsedTime(start).TotalMicroseconds);
                        Interlocked.Increment(ref _decodeSamples);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public WebRtcRuntimeStats GetStats()
    {
        var encodeSamples = Interlocked.Read(ref _encodeSamples);
        var decodeSamples = Interlocked.Read(ref _decodeSamples);
        return new WebRtcRuntimeStats(
            Interlocked.Read(ref _framesSent),
            Interlocked.Read(ref _bytesSent),
            Interlocked.Read(ref _framesReceived),
            Interlocked.Read(ref _bytesReceived),
            encodeSamples == 0 ? 0 : Interlocked.Read(ref _encodeTotalUs) / (double)encodeSamples / 1000.0,
            decodeSamples == 0 ? 0 : Interlocked.Read(ref _decodeTotalUs) / (double)decodeSamples / 1000.0,
            _sendWidth,
            _sendHeight,
            _lastDecodedWidth,
            _lastDecodedHeight,
            "VP8",
            ConnectionState.ToString(),
            IceState.ToString());
    }

    public async ValueTask DisposeAsync()
    {
        _decodeCts?.Cancel();
        _decodeQueue.Writer.TryComplete();
        if (_decodeTask is not null)
        {
            try { await _decodeTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
        }

        _decodeCts?.Dispose();
        if (_pc is not null)
        {
            try { _pc.Close("dispose"); } catch (Exception) { }
            _pc.Dispose();
            _pc = null;
        }

        _encoder.Dispose();
    }
}
