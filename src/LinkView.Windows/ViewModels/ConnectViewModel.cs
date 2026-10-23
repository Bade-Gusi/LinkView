using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LinkView.Core.Identity;
using LinkView.Core.Networking;
using LinkView.Core.Sessions;
using LinkView.Windows.Services;

namespace LinkView.Windows.ViewModels;

public partial class ConnectViewModel : ObservableObject
{
    private readonly SessionCoordinator _session;
    private readonly SettingsService _settings;
    private readonly Action<string, ToastKind> _notify;
    private readonly DispatcherTimer _countdownTimer;
    private DateTimeOffset? _expiresAt;

    public ConnectViewModel(
        SessionCoordinator session,
        SettingsService settings,
        Action<string, ToastKind> notify)
    {
        _session = session;
        _settings = settings;
        _notify = notify;
        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdownTimer.Tick += (_, _) => UpdateCountdown();
        session.StateChanged += (_, _) => System.Windows.Application.Current?.Dispatcher.BeginInvoke(RefreshState);
        RefreshState();
    }

    public string SignalingServer => _settings.Current.Network.SignalingServer;
    public string DeviceName => _settings.Current.General.DeviceName;

    [ObservableProperty]
    private int modeIndex;

    [ObservableProperty]
    private string joinCode = string.Empty;

    [ObservableProperty]
    private string? pairingCode;

    [ObservableProperty]
    private string pairingRemaining = "";

    [ObservableProperty]
    private bool isPairingActive;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string statusText = "尚未开始";

    [ObservableProperty]
    private string? errorText;

    [ObservableProperty]
    private string manualAddress = "";

    [ObservableProperty]
    private string? manualAddressResult;

    [ObservableProperty]
    private ShareSessionState state = ShareSessionState.Idle;

    public bool CanCreate => !IsBusy && State is ShareSessionState.Idle or ShareSessionState.Disconnected or ShareSessionState.Failed;
    public bool CanJoin => !IsBusy && State is ShareSessionState.Idle or ShareSessionState.Disconnected or ShareSessionState.Failed;

    partial void OnModeIndexChanged(int value)
    {
        ErrorText = null;
        StatusText = value == 0 ? "生成一个 6 位配对码，让对方输入" : "输入对方提供的 6 位配对码";
    }

    partial void OnJoinCodeChanged(string value)
    {
        var digits = new string(value.Where(char.IsAsciiDigit).Take(6).ToArray());
        if (digits != value)
        {
            JoinCode = digits;
        }
    }

    private void RefreshState()
    {
        State = _session.State;
        StatusText = Formatters.StateDescription(State);
        IsPairingActive = State is ShareSessionState.Pairing or ShareSessionState.AwaitingConsent;
        OnPropertyChanged(nameof(CanCreate));
        OnPropertyChanged(nameof(CanJoin));
        if (State is ShareSessionState.AwaitingConsent)
        {
            StatusText = "已发送加入请求，等待对方确认…";
        }
        else if (State is ShareSessionState.Negotiating or ShareSessionState.Connecting)
        {
            StatusText = "配对成功，正在建立媒体连接…";
        }
    }

    [RelayCommand]
    private async Task CreatePairingAsync()
    {
        ErrorText = null;
        IsBusy = true;
        try
        {
            var pairing = await _session.CreatePairingAsync(CancellationToken.None);
            PairingCode = pairing.Code;
            _expiresAt = pairing.ExpiresAt;
            IsPairingActive = true;
            _countdownTimer.Start();
            UpdateCountdown();
            _notify("配对码已生成，请让对方输入。", ToastKind.Success);
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
            _notify(ex.Message, ToastKind.Error);
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(CanCreate));
        }
    }

    [RelayCommand]
    private async Task JoinAsync()
    {
        if (!PairingCodeValidation(out var code))
        {
            return;
        }

        await JoinWithCodeAsync(code);
    }

    public async Task JoinWithCodeAsync(string code)
    {
        JoinCode = code;
        if (!PairingCodeValidation(out code))
        {
            return;
        }

        ErrorText = null;
        IsBusy = true;
        try
        {
            await _session.JoinAsync(code, CancellationToken.None);
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
            _notify(ex.Message, ToastKind.Error);
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(CanJoin));
        }
    }

    private bool PairingCodeValidation(out string code)
    {
        code = JoinCode.Trim();
        if (!LinkView.Core.Identity.PairingCode.IsWellFormed(code))
        {
            ErrorText = "请输入 6 位数字配对码。";
            return false;
        }

        return true;
    }

    [RelayCommand]
    private async Task CancelAsync()
    {
        _countdownTimer.Stop();
        IsPairingActive = false;
        PairingCode = null;
        await _session.DisconnectAsync("取消配对");
    }

    [RelayCommand]
    private void CopyCode()
    {
        if (string.IsNullOrWhiteSpace(PairingCode)) return;
        try
        {
            System.Windows.Clipboard.SetText(PairingCode);
            _notify("配对码已复制", ToastKind.Info);
        }
        catch (Exception)
        {
            _notify("无法访问剪贴板", ToastKind.Warning);
        }
    }

    [RelayCommand]
    private void ValidateManualAddress()
    {
        if (string.IsNullOrWhiteSpace(ManualAddress))
        {
            ManualAddressResult = "请输入主机地址，例如 192.168.1.20:5180 或 [fe80::1%12]:5180";
            return;
        }

        ManualAddressResult = IpAddressTools.TryParseEndpoint(ManualAddress, out var host, out var port, out var error)
            ? $"地址格式有效：{IpAddressTools.FormatEndpoint(host, port > 0 ? port : 5180)}。当前版本通过配对码建立连接，手动地址用于网络诊断。"
            : error;
    }

    private void UpdateCountdown()
    {
        if (_expiresAt is null)
        {
            return;
        }

        var remaining = _expiresAt.Value - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            _countdownTimer.Stop();
            PairingRemaining = "已过期";
            IsPairingActive = false;
            return;
        }

        PairingRemaining = $"{(int)remaining.TotalMinutes:00}:{remaining.Seconds:00} 后失效";
    }

    public void Reset()
    {
        _countdownTimer.Stop();
        PairingCode = null;
        IsPairingActive = false;
        ErrorText = null;
        JoinCode = string.Empty;
        RefreshState();
    }
}
