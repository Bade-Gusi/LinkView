using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LinkView.Core.Sessions;
using LinkView.Core.Settings;
using LinkView.Windows.Media;
using LinkView.Windows.Services;

namespace LinkView.Windows.ViewModels;

public partial class ShareViewModel : ObservableObject
{
    private readonly SessionCoordinator _session;
    private readonly ScreenCaptureService _capture;
    private readonly SettingsService _settings;
    private readonly Action<string, ToastKind> _notify;
    private readonly Action<string> _navigate;
    private readonly Action<ShareViewModel> _openViewer;
    private WriteableBitmap? _previewBitmap;
    private WriteableBitmap? _remoteBitmap;
    private DateTimeOffset _lastPreview;
    private DateTimeOffset _lastRemote;
    private bool _previewPending;
    private bool _remotePending;
    private bool _suppressPresetSave;

    public ShareViewModel(
        SessionCoordinator session,
        ScreenCaptureService capture,
        SettingsService settings,
        Action<string> navigate,
        Action<ShareViewModel> openViewer,
        Action<string, ToastKind> notify)
    {
        _session = session;
        _capture = capture;
        _settings = settings;
        _navigate = navigate;
        _openViewer = openViewer;
        _notify = notify;

        session.PreviewFrame += OnPreviewFrame;
        session.RemoteFrame += OnRemoteFrame;
        session.StateChanged += (_, _) => OnUi(RefreshState);
        capture.SourceLost += (_, message) => OnUi(() => _notify(message, ToastKind.Warning));

        _suppressPresetSave = true;
        SelectedPresetIndex = (int)settings.Current.Video.Preset;
        _suppressPresetSave = false;
        ReloadSources();
        RefreshState();
    }

    [ObservableProperty] private CaptureSource? selectedSource;
    [ObservableProperty] private string statusText = "未连接";
    [ObservableProperty] private string peerName = "尚未连接";
    [ObservableProperty] private bool isConnected;
    [ObservableProperty] private bool isSharing;
    [ObservableProperty] private bool isPaused;
    [ObservableProperty] private bool hasRemoteVideo;
    [ObservableProperty] private int viewIndex;
    [ObservableProperty] private ImageSource? previewImage;
    [ObservableProperty] private ImageSource? remoteImage;
    [ObservableProperty] private string outputInfo = "—";
    [ObservableProperty] private string remoteInfo = "—";
    [ObservableProperty] private string captureHint = "连接建立后即可选择采集源并开始共享";
    [ObservableProperty] private int selectedPresetIndex;
    [ObservableProperty] private string? qualityNote;
    [ObservableProperty] private bool isBusy;

    public ObservableCollection<CaptureSource> Sources { get; } = [];
    public IReadOnlyList<QualityPreset> PresetOptions { get; } = Enum.GetValues<QualityPreset>();

    public bool CanStart => IsConnected && !IsSharing && !IsBusy;
    public bool CanPause => IsSharing && !IsPaused;
    public bool CanResume => IsSharing && IsPaused;
    public bool CanStop => IsSharing;

    partial void OnSelectedSourceChanged(CaptureSource? value) => OnPropertyChanged(nameof(CanStart));

    partial void OnSelectedPresetIndexChanged(int value)
    {
        if (_suppressPresetSave || value < 0 || value >= PresetOptions.Count)
        {
            return;
        }

        var preset = PresetOptions[value];
        var settings = _settings.Current;
        var video = QualityPresets.Apply(preset, settings.Video);
        _settings.Save(settings with { Video = video }, out var issues);
        QualityNote = preset == QualityPreset.Custom
            ? "自定义画质请在高级设置中调整。"
            : IsSharing
                ? "码率会即时调整；分辨率与帧率将在下次开始共享时生效。"
                : "画质预设已保存，下次开始共享时生效。";
    }

    public void ReloadSources()
    {
        var previous = SelectedSource?.Id;
        Sources.Clear();
        try
        {
            foreach (var source in ScreenCaptureService.EnumerateSources())
            {
                Sources.Add(source);
            }
        }
        catch (Exception ex)
        {
            _notify("枚举采集源失败：" + ex.Message, ToastKind.Error);
        }

        SelectedSource = Sources.FirstOrDefault(s => s.Id == previous)
                         ?? Sources.FirstOrDefault(s => s.Kind == CaptureSourceKind.Monitor && s.IsPrimary)
                         ?? Sources.FirstOrDefault();
        CaptureHint = Sources.Count == 0
            ? "没有找到可采集的显示器或窗口。"
            : $"{Sources.Count(s => s.Kind == CaptureSourceKind.Monitor)} 个显示器，{Sources.Count(s => s.Kind == CaptureSourceKind.Window)} 个窗口";
    }

    public void RefreshState()
    {
        IsConnected = _session.State is ShareSessionState.Connected or ShareSessionState.Sharing;
        IsSharing = _session.IsSharing && _session.State == ShareSessionState.Sharing;
        IsPaused = _session.IsSharing && _session.State == ShareSessionState.Connected;
        PeerName = _session.PeerDevice?.DeviceName ?? "尚未连接";
        StatusText = Formatters.StateDescription(_session.State);
        if (_session.State == ShareSessionState.Sharing)
        {
            StatusText = "正在共享屏幕";
        }
        else if (_session.State == ShareSessionState.Connected)
        {
            StatusText = _session.IsSharing ? "共享已暂停" : "已连接，等待开始共享";
        }

        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanPause));
        OnPropertyChanged(nameof(CanResume));
        OnPropertyChanged(nameof(CanStop));
        if (!_session.IsSharing)
        {
            IsSharing = false;
            IsPaused = false;
        }
    }

    [RelayCommand]
    private void RefreshSourceList() => ReloadSources();

    [RelayCommand]
    private void GoToConnect() => _navigate("connect");

    [RelayCommand]
    private async Task StartSharingAsync()
    {
        if (SelectedSource is null)
        {
            _notify("请选择要共享的显示器或窗口。", ToastKind.Warning);
            return;
        }

        IsBusy = true;
        try
        {
            await _session.StartSharingAsync(SelectedSource, CancellationToken.None);
            OutputInfo = $"{_capture.OutputWidth} × {_capture.OutputHeight}";
            ViewIndex = 0;
            _notify("已开始共享", ToastKind.Success);
        }
        catch (Exception ex)
        {
            _notify(ex.Message, ToastKind.Error);
        }
        finally
        {
            IsBusy = false;
            RefreshState();
        }
    }

    [RelayCommand]
    private void Pause()
    {
        _session.PauseSharing();
        RefreshState();
    }

    [RelayCommand]
    private void Resume()
    {
        _session.ResumeSharing();
        RefreshState();
    }

    [RelayCommand]
    private async Task StopSharingAsync()
    {
        await _session.StopSharingAsync();
        RefreshState();
    }

    [RelayCommand]
    private async Task ApplySourceAsync()
    {
        if (!_session.IsSharing || SelectedSource is null)
        {
            return;
        }

        try
        {
            await _session.StopSharingAsync();
            await _session.StartSharingAsync(SelectedSource, CancellationToken.None);
            OutputInfo = $"{_capture.OutputWidth} × {_capture.OutputHeight}";
            _notify("采集源已切换", ToastKind.Success);
        }
        catch (Exception ex)
        {
            _notify(ex.Message, ToastKind.Error);
        }
    }

    [RelayCommand]
    private void ShowLocal() => ViewIndex = 0;

    [RelayCommand]
    private void ShowRemote() => ViewIndex = 1;

    [RelayCommand]
    private void Fullscreen()
    {
        if (ViewIndex == 1 && !HasRemoteVideo)
        {
            _notify("尚未收到对方画面。", ToastKind.Warning);
            return;
        }

        _openViewer(this);
    }

    private void OnPreviewFrame(object? sender, CapturedFrame frame)
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastPreview < TimeSpan.FromMilliseconds(90) || _previewPending)
        {
            return;
        }

        _lastPreview = now;
        _previewPending = true;
        var copy = new byte[frame.Width * frame.Height * 4];
        Buffer.BlockCopy(frame.Bgra, 0, copy, 0, copy.Length);
        var width = frame.Width;
        var height = frame.Height;
        OnUi(() =>
        {
            try
            {
                if (_previewBitmap is null || _previewBitmap.PixelWidth != width || _previewBitmap.PixelHeight != height)
                {
                    _previewBitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
                    PreviewImage = _previewBitmap;
                }

                _previewBitmap.WritePixels(new Int32Rect(0, 0, width, height), copy, width * 4, 0);
                OutputInfo = $"{width} × {height}";
            }
            finally
            {
                _previewPending = false;
            }
        });
    }

    private void OnRemoteFrame(object? sender, DecodedFrame frame)
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastRemote < TimeSpan.FromMilliseconds(33) || _remotePending)
        {
            return;
        }

        _lastRemote = now;
        _remotePending = true;
        OnUi(() =>
        {
            try
            {
                if (_remoteBitmap is null || _remoteBitmap.PixelWidth != frame.Width || _remoteBitmap.PixelHeight != frame.Height)
                {
                    _remoteBitmap = new WriteableBitmap(frame.Width, frame.Height, 96, 96, PixelFormats.Bgr24, null);
                    RemoteImage = _remoteBitmap;
                }

                _remoteBitmap.WritePixels(new Int32Rect(0, 0, frame.Width, frame.Height), frame.Bgr,
                    frame.Width * 3, 0);
                HasRemoteVideo = true;
                RemoteInfo = $"{frame.Width} × {frame.Height}";
                if (!IsSharing)
                {
                    ViewIndex = 1;
                }
            }
            finally
            {
                _remotePending = false;
            }
        });
    }

    private static void OnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(action);
    }
}
