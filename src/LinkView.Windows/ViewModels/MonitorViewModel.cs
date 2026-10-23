using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LinkView.Core.Sessions;
using LinkView.Windows.Services;

namespace LinkView.Windows.ViewModels;

public partial class MonitorViewModel : ObservableObject
{
    private readonly SessionCoordinator _session;
    private readonly SettingsService _settings;
    private readonly DispatcherTimer _timer;
    private const int MaxPoints = 60;

    public MonitorViewModel(SessionCoordinator session, SettingsService settings)
    {
        _session = session;
        _settings = settings;
        _timer = new DispatcherTimer();
        _timer.Tick += (_, _) => Tick();
    }

    [ObservableProperty] private bool hasSession;
    [ObservableProperty] private string stateLabel = "无活动会话";
    [ObservableProperty] private string peerName = "—";
    [ObservableProperty] private string roleText = "—";
    [ObservableProperty] private string durationText = "00:00";
    [ObservableProperty] private string connectionState = "—";
    [ObservableProperty] private string iceState = "—";
    [ObservableProperty] private string pathLabel = "—";
    [ObservableProperty] private string codec = "VP8";
    [ObservableProperty] private string sendFps = "0";
    [ObservableProperty] private string sendBitrate = "0";
    [ObservableProperty] private string receiveFps = "0";
    [ObservableProperty] private string receiveBitrate = "0";
    [ObservableProperty] private string encodeMs = "—";
    [ObservableProperty] private string decodeMs = "—";
    [ObservableProperty] private string sendResolution = "—";
    [ObservableProperty] private string receiveResolution = "—";
    [ObservableProperty] private string captureSource = "—";
    [ObservableProperty] private string reconnects = "0";
    [ObservableProperty] private string lastVideo = "尚未收到";
    [ObservableProperty] private string emptyHint = "建立连接后，这里会显示真实的传输指标。";

    public ObservableCollection<double> SendKbpsSeries { get; } = [];
    public ObservableCollection<double> ReceiveKbpsSeries { get; } = [];
    public ObservableCollection<double> SendFpsSeries { get; } = [];
    public ObservableCollection<double> ReceiveFpsSeries { get; } = [];

    public string RttText => "不支持";
    public string LossText => "不支持";
    public string JitterText => "不支持";
    public string MetricsNote =>
        "SIPSorcery 当前公开 API 未提供稳定的 RTCP 接收报告读取入口，因此 RTT、丢包与抖动显示为“不支持”，不会用估算值冒充真实数据。";

    public void Activate()
    {
        _timer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(_settings.Current.Performance.StatsRefreshMs, 250, 5000));
        _timer.Start();
        Tick();
    }

    public void Deactivate() => _timer.Stop();

    private void Tick()
    {
        var stats = _session.GetLiveStats();
        HasSession = _session.State is not (ShareSessionState.Idle or ShareSessionState.Disconnected);
        StateLabel = Converters.SessionLabels.Describe(_session.State);
        PeerName = stats.PeerName ?? "—";
        RoleText = stats.Role switch
        {
            Protocol.SessionRole.Host => "发起方",
            Protocol.SessionRole.Guest => "加入方",
            _ => "—"
        };
        DurationText = stats.Duration.TotalHours >= 1
            ? $"{(int)stats.Duration.TotalHours:00}:{stats.Duration.Minutes:00}:{stats.Duration.Seconds:00}"
            : $"{stats.Duration.Minutes:00}:{stats.Duration.Seconds:00}";
        ConnectionState = stats.ConnectionState;
        IceState = stats.IceState;
        PathLabel = stats.PathLabel;
        Codec = stats.Codec;
        SendFps = stats.SentFps.ToString();
        SendBitrate = stats.SentKbps.ToString("0");
        ReceiveFps = stats.ReceivedFps.ToString();
        ReceiveBitrate = stats.ReceivedKbps.ToString("0");
        EncodeMs = stats.EncodeMs > 0 ? stats.EncodeMs.ToString("0.0") : "—";
        DecodeMs = stats.DecodeMs > 0 ? stats.DecodeMs.ToString("0.0") : "—";
        SendResolution = stats.SendWidth > 0 ? $"{stats.SendWidth} × {stats.SendHeight}" : "—";
        ReceiveResolution = stats.ReceiveWidth > 0 ? $"{stats.ReceiveWidth} × {stats.ReceiveHeight}" : "—";
        CaptureSource = stats.CaptureSourceName ?? "—";
        Reconnects = stats.Reconnects.ToString();
        LastVideo = stats.LastRemoteVideoAt is { } at
            ? at.ToLocalTime().ToString("HH:mm:ss")
            : "尚未收到";

        if (HasSession)
        {
            Append(SendKbpsSeries, stats.SentKbps);
            Append(ReceiveKbpsSeries, stats.ReceivedKbps);
            Append(SendFpsSeries, stats.SentFps);
            Append(ReceiveFpsSeries, stats.ReceivedFps);
        }
    }

    private static void Append(ObservableCollection<double> series, double value)
    {
        series.Add(value);
        while (series.Count > MaxPoints)
        {
            series.RemoveAt(0);
        }
    }

    [RelayCommand]
    private void Refresh() => Tick();
}
