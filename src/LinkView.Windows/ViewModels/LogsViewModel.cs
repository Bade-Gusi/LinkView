using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LinkView.Windows.Services;
using LinkView.Windows.Services.Logging;
using Microsoft.Extensions.Logging;

namespace LinkView.Windows.ViewModels;

public partial class LogsViewModel : ObservableObject
{
    private readonly LogStore _store;
    private readonly DiagnosticsService _diagnostics;
    private readonly Action<string, ToastKind> _notify;
    private const int MaxDisplayed = 1_000;

    public LogsViewModel(LogStore store, DiagnosticsService diagnostics, Action<string, ToastKind> notify)
    {
        _store = store;
        _diagnostics = diagnostics;
        _notify = notify;
        store.EntryAdded += (_, entry) => OnUi(() => AppendIfMatches(entry));
        Reload();
    }

    [ObservableProperty] private int minimumLevelIndex = 2;
    [ObservableProperty] private string searchText = "";
    [ObservableProperty] private bool isEmpty = true;

    public ObservableCollection<LogEntry> Entries { get; } = [];

    public IReadOnlyList<string> LevelFilters { get; } = ["全部", "Debug 及以上", "Info 及以上", "Warning 及以上", "Error 及以上"];

    public void Reload()
    {
        Entries.Clear();
        foreach (var entry in _store.Snapshot().Where(Matches).TakeLast(MaxDisplayed))
        {
            Entries.Add(entry);
        }

        IsEmpty = Entries.Count == 0;
    }

    partial void OnMinimumLevelIndexChanged(int value) => Reload();
    partial void OnSearchTextChanged(string value) => Reload();

    private void AppendIfMatches(LogEntry entry)
    {
        if (!Matches(entry)) return;
        Entries.Add(entry);
        while (Entries.Count > MaxDisplayed) Entries.RemoveAt(0);
        IsEmpty = false;
    }

    private bool Matches(LogEntry entry)
    {
        var minimum = MinimumLevelIndex switch
        {
            0 => LogLevel.Trace,
            1 => LogLevel.Debug,
            2 => LogLevel.Information,
            3 => LogLevel.Warning,
            4 => LogLevel.Error,
            _ => LogLevel.Trace
        };
        if (entry.Level < minimum) return false;
        if (string.IsNullOrWhiteSpace(SearchText)) return true;
        return entry.Message.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
               entry.Category.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand]
    private void Clear()
    {
        _store.Clear();
        Entries.Clear();
        IsEmpty = true;
    }

    [RelayCommand]
    private void ExportDiagnostics()
    {
        try
        {
            var path = _diagnostics.ExportReport();
            _notify("诊断报告已导出：" + path, ToastKind.Success);
        }
        catch (Exception ex)
        {
            _notify("导出失败：" + ex.Message, ToastKind.Error);
        }
    }

    [RelayCommand]
    private void Copy(LogEntry? entry)
    {
        if (entry is null) return;
        try
        {
            Clipboard.SetText($"{entry.Timestamp:O} {entry.LevelLabel} {entry.Category} | {entry.Message}\n{entry.Exception}");
            _notify("日志已复制", ToastKind.Info);
        }
        catch (Exception)
        {
            _notify("无法访问剪贴板", ToastKind.Warning);
        }
    }

    private static void OnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(action);
    }
}
