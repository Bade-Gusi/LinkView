using System.Diagnostics;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.Encoders;

namespace LinkView.WebRtcProbe;

/// <summary>
/// 最小化 WebRTC 媒体互通探针：两个真实 SIPSorcery PeerConnection 通过 ICE/DTLS/SRTP 建连，
/// 使用真实的 libvpx VP8 编解码器传输合成画面。用于验证媒体链路本身，
/// 不能替代 Windows 采集与 Android 真机互通验证。
/// </summary>
internal static class Program
{
    private const int Width = 640;
    private const int Height = 360;
    private const int Fps = 30;
    private const uint FrameDurationMs = 33;

    private static int _encodedFrames;
    private static int _decodedFrames;
    private static int _encodedBytes;
    private static string _firstDecoded = "none";

    private static async Task<int> Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine($"LinkView WebRTC probe | {Width}x{Height}@{Fps} VP8 | SIPSorcery 10.0.17");

        using var sendEncoder = new VpxVideoEncoder { TargetKbps = 2000 };
        using var receiveDecoder = new VpxVideoEncoder();

        var formats = sendEncoder.SupportedFormats;
        Console.WriteLine("Encoder formats: " + string.Join(", ", formats.Select(f => $"{f.Codec}/{f.FormatID}@{f.ClockRate}")));
        if (formats.All(f => f.Codec != VideoCodecsEnum.VP8))
        {
            Console.Error.WriteLine("Vendored encoder does not advertise VP8. The abstraction enum mismatch is still present.");
            return 2;
        }

        var sender = CreatePeer("sender", sendEncoder, isSender: true);
        var receiver = CreatePeer("receiver", receiveDecoder, isSender: false);

        var senderCandidates = new List<RTCIceCandidate>();
        var receiverCandidates = new List<RTCIceCandidate>();
        var senderRemoteSet = false;
        var receiverRemoteSet = false;

        sender.onicecandidate += candidate =>
        {
            lock (receiverCandidates)
            {
                if (receiverRemoteSet) AddCandidate(receiver, candidate);
                else receiverCandidates.Add(candidate);
            }
        };

        receiver.onicecandidate += candidate =>
        {
            lock (senderCandidates)
            {
                if (senderRemoteSet) AddCandidate(sender, candidate);
                else senderCandidates.Add(candidate);
            }
        };

        var connected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        sender.onconnectionstatechange += state =>
        {
            Console.WriteLine($"[sender] connectionState={state}");
            if (state == RTCPeerConnectionState.connected) connected.TrySetResult(true);
            else if (state is RTCPeerConnectionState.failed or RTCPeerConnectionState.closed)
                connected.TrySetException(new InvalidOperationException($"sender connection {state}"));
        };
        receiver.onconnectionstatechange += state => Console.WriteLine($"[receiver] connectionState={state}");

        var offer = sender.createOffer();
        if (!offer.sdp.Contains("VP8", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Offer does not contain VP8. SDP:\n" + offer.sdp);
            return 3;
        }

        await sender.setLocalDescription(offer);
        var setRemoteResult = receiver.setRemoteDescription(offer);
        if (setRemoteResult != SetDescriptionResultEnum.OK)
        {
            Console.Error.WriteLine("Receiver rejected offer: " + setRemoteResult);
            return 4;
        }

        lock (receiverCandidates)
        {
            receiverRemoteSet = true;
            foreach (var candidate in receiverCandidates) AddCandidate(receiver, candidate);
            receiverCandidates.Clear();
        }

        var answer = receiver.createAnswer();
        await receiver.setLocalDescription(answer);
        var answerResult = sender.setRemoteDescription(answer);
        if (answerResult != SetDescriptionResultEnum.OK)
        {
            Console.Error.WriteLine("Sender rejected answer: " + answerResult);
            return 5;
        }

        lock (senderCandidates)
        {
            senderRemoteSet = true;
            foreach (var candidate in senderCandidates) AddCandidate(sender, candidate);
            senderCandidates.Clear();
        }

        try
        {
            await connected.Task.WaitAsync(TimeSpan.FromSeconds(15));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Connection did not establish: " + ex.Message);
            return 6;
        }

        Console.WriteLine("Connected. Streaming synthetic frames...");
        var frame = new byte[Width * Height * 4];
        var sw = Stopwatch.StartNew();
        var frameIndex = 0;
        sendEncoder.ForceKeyFrame();

        while (sw.Elapsed < TimeSpan.FromSeconds(5))
        {
            FillFrame(frame, frameIndex++);
            var encoded = sendEncoder.EncodeVideo(Width, Height, frame, VideoPixelFormatsEnum.Bgra, VideoCodecsEnum.VP8);
            if (encoded is { Length: > 0 })
            {
                Interlocked.Increment(ref _encodedFrames);
                Interlocked.Add(ref _encodedBytes, encoded.Length);
                sender.SendVideo(90000u / Fps, encoded);
            }

            await Task.Delay(1000 / Fps);
        }

        await Task.Delay(700);

        Console.WriteLine($"encodedFrames={_encodedFrames} decodedFrames={_decodedFrames} encodedBytes={_encodedBytes}");
        Console.WriteLine($"firstDecoded={_firstDecoded}");

        sender.Close("probe done");
        receiver.Close("probe done");

        var ok = _encodedFrames > 10 && _decodedFrames > 10;
        Console.WriteLine(ok ? "RESULT: PASS" : "RESULT: FAIL");
        return ok ? 0 : 1;
    }

    private static void AddCandidate(RTCPeerConnection pc, RTCIceCandidate candidate)
    {
        if (RTCIceCandidateInit.TryParse(candidate.toJSON(), out var init) && init is not null)
        {
            pc.addIceCandidate(init);
        }
    }

    private static RTCPeerConnection CreatePeer(string label, VpxVideoEncoder encoder, bool isSender)
    {
        var config = new RTCConfiguration
        {
            iceServers = [],
            X_ICEIncludeAllInterfaceAddresses = true
        };

        var pc = new RTCPeerConnection(config);
        var track = new MediaStreamTrack(encoder.SupportedFormats, MediaStreamStatusEnum.SendRecv);
        pc.addTrack(track);

        pc.OnVideoFrameReceived += (remoteEp, timestamp, payload, format) =>
        {
            if (isSender || format.Codec != VideoCodecsEnum.VP8) return;
            try
            {
                foreach (var sample in encoder.DecodeVideo(payload, VideoPixelFormatsEnum.Bgra, VideoCodecsEnum.VP8))
                {
                    var count = Interlocked.Increment(ref _decodedFrames);
                    if (count == 1)
                    {
                        _firstDecoded = $"{sample.Width}x{sample.Height} {sample.Sample.Length} bytes";
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[{label}] decode error: {ex.Message}");
            }
        };

        return pc;
    }

    private static void FillFrame(byte[] bgra, int frameIndex)
    {
        for (var y = 0; y < Height; y++)
        {
            var row = y * Width * 4;
            for (var x = 0; x < Width; x++)
            {
                var i = row + x * 4;
                var bar = (x + frameIndex * 6) / 80 % 8;
                bgra[i + 0] = (byte)((bar & 1) != 0 ? 220 : 30);
                bgra[i + 1] = (byte)((bar & 2) != 0 ? 200 : 40);
                bgra[i + 2] = (byte)((bar & 4) != 0 ? 230 : 50);
                bgra[i + 3] = 255;
            }
        }
    }
}
