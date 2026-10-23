using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LinkView.Windows.Services;

namespace LinkView.Windows.ViewModels;

public partial class DiagnosticsViewModel : ObservableObject
{
    private readonly DiagnosticsService _diagnostics;
    private readonly NetworkInfoService _network;
    private readonly Action<string, ToastKind> _notify;
    private CancellationTokenSource? _cts;

    public DiagnosticsViewModel(
        DiagnosticsService diagnostics,
        NetworkInfoService network,
        Action<string, ToastKind> notify)
    {
        _diagnostics = diagnostics;
        _network = network;
        _notify = notify;
        ReloadInterfaces();
    }

    [ObservableProperty] private bool isRunning;
    [ObservableProperty] private bool hasRun;
    [ObservableProperty] private string summary = "运行检查以获取本机网络与连接能力。";
    [ObservableProperty] private int passedCount;
    [ObservableProperty] private int warningCount;
    [ObservableProperty] private int failedCount;

    public ObservableCollection<DiagnosticCheck> Checks { get; } = [];
    public ObservableCollection<NetworkAddressInfo> Interfaces { get; } = [];

    public void ReloadInterfaces()
    {
        Interfaces.Clear();
        foreach (var address in _network.GetAddresses())
        {
            Interfaces.Add(address);
        }
    }

    [RelayCommand]
    private async Task RunAsync()
    {
        if (IsRunning) return;
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        Checks.Clear();
        HasRun = false;
        IsRunning = true;
        Summary = "正在执行真实网络检查…";

        var progress = new Progress<DiagnosticCheck>(check => OnUi(() =>
        {
            var index = Checks.ToList().FindIndex(c => c.Name == check.Name && c.Category == check.Category);
            if (index >= 0) Checks[index] = check;
            else Checks.Add(check);
        }));

        try
        {
            var results = await _diagnostics.RunAsync(progress, _cts.Token);
            OnUi(() =>
            {
                Checks.Clear();
                foreach (var check in results)
                {
                    Checks.Add(check);
                }

                PassedCount = results.Count(r => r.Status == CheckStatus.Passed);
                WarningCount = results.Count(r => r.Status == CheckStatus.Warning);
                FailedCount = results.Count(r => r.Status == CheckStatus.Failed);
                Summary = FailedCount > 0
                    ? $"{PassedCount} 项通过，{WarningCount} 项警告，{FailedCount} 项失败。"
                    : $"{PassedCount} 项通过，{WarningCount} 项警告。";
                HasRun = true;
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            OnUi(() =>
            {
                Summary = "检查失败：" + ex.Message;
                _notify("诊断检查失败：" + ex.Message, ToastKind.Error);
            });
        }
        finally
        {
            OnUi(() => IsRunning = false);
        }
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        try
        {
            var path = _diagnostics.ExportReport();
            _notify("诊断报告已导出：" + path, ToastKind.Success);
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _notify("导出诊断报告失败：" + ex.Message, ToastKind.Error);
        }
    }

    public void Cancel() => _cts?.Cancel();

    private static void OnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(action);
    }
}
